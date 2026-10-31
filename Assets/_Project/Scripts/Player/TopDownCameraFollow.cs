using P1.Core;
using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 第三人称轨道跟随相机（P9）：
    /// 类名 <c>TopDownCameraFollow</c> 保留以兼容旧调用方/测试，实现已从俯视跟随改为 Inkwave 式第三人称。
    /// 1. 鼠标 / 手柄右摇杆 / 触屏滑屏驱动 yaw-pitch；
    /// 2. 枢轴跟随角色（人形 1.45m、潜墨 1.05m，对齐 1.25m 蛋形角色），吊臂 4.5m / 4.1m；
    /// 3. 球探针防穿墙、贴墙自动收进、近距离右肩偏移、地面高度保护；
    /// 4. 透视 52° FOV，蓄力狙变焦、手雷瞄准微调；保留 Trauma 震屏与全部旧公共 API。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TopDownCameraFollow : MonoBehaviour
    {
        public static TopDownCameraFollow Instance { get; private set; }

        [SerializeField] private Transform target;
        [SerializeField] private GameConfigSO gameConfig;

        private Camera _cam;
        private InkCharacterMotor _motor;
        private WeaponController _weapon;

        private float _yaw;
        private float _pitch;
        private Vector3 _pivot;
        private Vector3 _pivotVelocity;
        private Vector3 _lastTargetPosition;
        private float _currentDistance;
        private float _shoulder;
        private float _smoothedFov;
        private float _trauma;
        private float _shakeRoll;
        private bool _isSubAimActive;
        private bool _hasSnapped;
        private Vector3 _aimGroundPoint;

        // 开火镜头冲击（P16）：只做「沿视轴后拉 + Roll + FOV」三种不改瞄准方向的偏移。
        // 相机朝向决定屏幕中心射线，任何 pitch/yaw 改动都会让准星漂移，所以这里绝不旋转照射轴。
        private float _shotKickDistance;
        private float _shotKickRoll;
        private float _shotKickFov;
        private float _shotKickRecover = 0.12f;

        private readonly RaycastHit[] _castHits = new RaycastHit[8];

        public Camera CameraComponent => _cam;
        public bool IsSubAimActive => _isSubAimActive;

        /// <summary>当前水平朝向（度）。世界 +Z 为 0，顺时针为正。</summary>
        public float YawDegrees => _yaw;

        /// <summary>当前俯仰角（度）。正数 = 低头俯视，负数 = 抬头仰视。</summary>
        public float PitchDegrees => _pitch;

        /// <summary>当前相机吊臂长度（米），供自动化测试与调试面板读取。</summary>
        public float CurrentDistance => _currentDistance;

        /// <summary>屏幕中心瞄准射线的最大探测距离（米）。</summary>
        public float AimRayDistance => gameConfig != null ? Mathf.Max(5f, gameConfig.tpsAimRayDistance) : 70f;

        /// <summary>相机实际朝向（含俯仰，不含震屏 Roll）。</summary>
        public Vector3 LookForward => Quaternion.Euler(_pitch, _yaw, 0f) * Vector3.forward;

        /// <summary>水平前向：WASD 前进方向。</summary>
        public Vector3 PlanarForward => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;

        /// <summary>水平右向：D 键方向。</summary>
        public Vector3 PlanarRight => Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

        /// <summary>
        /// 本帧瞄准射线应使用的相机位置：与 LateUpdate 的枢轴/吊臂/肩部/地面保护同一套几何，
        /// 但不叠加震屏、开火后拉与绕视轴 Roll（这些只做视觉偏移，不改变屏幕中心射线）。
        /// PlayerController.Update 早于相机 LateUpdate，直接用 transform.position 会拿到上一帧位置，
        /// 快速甩枪时射线会与渲染画面的中心线错开。
        /// </summary>
        public Vector3 GetAimCameraPosition()
        {
            if (!_hasSnapped || target == null)
                return transform.position;

            Vector3 forward = LookForward;
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;
            Vector3 position = _pivot - forward * Mathf.Max(0.01f, _currentDistance) + right * _shoulder;
            return ApplyGroundClearance(position);
        }

        private void Awake()
        {
            Instance = this;
            _cam = GetComponent<Camera>();
            ApplyCameraSettings();
            ResolveActorRefs();
            if (target != null)
                SnapImmediately();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public void Configure(Transform followTarget, GameConfigSO config)
        {
            target = followTarget;
            gameConfig = config;
            ResolveActorRefs();
            ApplyCameraSettings();
            SnapImmediately();
        }

        /// <summary>保留旧 API：TPS 下不再做瞄准前移，仅记录供调试/兼容。</summary>
        public void SetAimGroundPoint(Vector3 worldAimPoint)
        {
            _aimGroundPoint = worldAimPoint;
        }

        /// <summary>
        /// 副武器抛物线瞄准 / 远射程瞄准时激活：FOV 轻微收窄、吊臂略拉远，
        /// 让远端 2.7m 爆炸红圈落点清晰可见。
        /// </summary>
        public void SetSubAimActive(bool active)
        {
            _isSubAimActive = active;
        }

        /// <summary>
        /// 应用一次视角增量（度）。鼠标、右摇杆、触屏滑屏统一走这里；
        /// 旋转立即写入 transform，保证同一帧内的瞄准射线使用最新朝向。
        /// </summary>
        public void AddLookInput(float yawDeltaDegrees, float pitchDeltaDegrees)
        {
            if (float.IsNaN(yawDeltaDegrees) || float.IsInfinity(yawDeltaDegrees) ||
                float.IsNaN(pitchDeltaDegrees) || float.IsInfinity(pitchDeltaDegrees))
                return;

            _yaw = Mathf.Repeat(_yaw + yawDeltaDegrees + 180f, 360f) - 180f;

            float minPitch = gameConfig != null ? gameConfig.tpsPitchMin : -60f;
            float maxPitch = gameConfig != null ? gameConfig.tpsPitchMax : 66f;
            _pitch = ThirdPersonCameraMath.ClampPitch(_pitch + pitchDeltaDegrees, minPitch, maxPitch);

            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
        }

        /// <summary>安全将世界坐标转换为屏幕像素坐标，供 HUD/受击弧等组件使用。</summary>
        public Vector3 WorldToScreenPoint(Vector3 worldPos)
        {
            return _cam != null ? _cam.WorldToScreenPoint(worldPos) : Vector3.zero;
        }

        /// <summary>累加震屏创伤（0~1）。多次伤害叠加而非重置，符合 Inkwave 的 trauma 模型。</summary>
        public void AddTrauma(float amount)
        {
            _trauma = Mathf.Clamp01(_trauma + amount);
        }

        /// <summary>
        /// 开火镜头冲击（P16）：沿视轴后拉 <paramref name="distanceMeters"/>、绕视轴 Roll
        /// <paramref name="rollDegrees"/>、FOV 短暂拉宽 <paramref name="fovDegrees"/>，再指数回弹。
        /// 三种偏移都不改变相机前向，因此准星与弹道解算完全不受影响。
        /// </summary>
        public void AddShotPunch(
            float distanceMeters,
            float rollDegrees,
            float fovDegrees,
            float recoverSeconds = 0.12f)
        {
            if (distanceMeters <= 0f && rollDegrees <= 0f && fovDegrees <= 0f)
                return;

            // 上限防止高射速武器把冲击累积成永久位移
            _shotKickDistance = Mathf.Min(0.22f, _shotKickDistance + Mathf.Max(0f, distanceMeters));
            _shotKickRoll = Mathf.Clamp(_shotKickRoll + rollDegrees, -2.5f, 2.5f);
            _shotKickFov = Mathf.Clamp(_shotKickFov + Mathf.Max(0f, fovDegrees), 0f, 4f);
            _shotKickRecover = Mathf.Clamp(recoverSeconds, 0.04f, 0.4f);
        }

        /// <summary>当前开火后拉量（米），调试/测试可读。</summary>
        public float ShotKickDistance => _shotKickDistance;

        /// <summary>当前开火 FOV 冲击量（度），调试/测试可读。</summary>
        public float ShotKickFov => _shotKickFov;

        private void DecayShotKick(float unscaledDt)
        {
            if (unscaledDt <= 0f)
                return;

            float k = 1f - Mathf.Exp(-unscaledDt / Mathf.Max(0.02f, _shotKickRecover));
            _shotKickDistance = Mathf.Lerp(_shotKickDistance, 0f, k);
            _shotKickRoll = Mathf.Lerp(_shotKickRoll, 0f, k);
            _shotKickFov = Mathf.Lerp(_shotKickFov, 0f, k);

            if (_shotKickDistance < 0.0002f) _shotKickDistance = 0f;
            if (Mathf.Abs(_shotKickRoll) < 0.002f) _shotKickRoll = 0f;
            if (_shotKickFov < 0.002f) _shotKickFov = 0f;
        }

        /// <summary>
        /// 按世界坐标距离精准附加震屏（P6-A）：半径内按距离平方衰减，
        /// 距离以本地玩家为参照，保证「离得越近越震」。
        /// </summary>
        public void AddTraumaFromPoint(Vector3 worldPoint, float maxTrauma, float falloffRadius = -1f)
        {
            float radius = falloffRadius > 0f
                ? falloffRadius
                : (gameConfig != null ? gameConfig.shakeExplosionRadius : 14f);
            if (radius <= 0.01f || maxTrauma <= 0f)
                return;

            Vector3 focus = target != null ? target.position : transform.position;
            Vector3 delta = worldPoint - focus;
            delta.y = 0f;

            float distance01 = Mathf.Clamp01(delta.magnitude / radius);
            float falloff = 1f - distance01;
            falloff *= falloff;
            AddTrauma(Mathf.Clamp01(maxTrauma) * Mathf.Lerp(0.3f, 1f, falloff));
        }

        /// <summary>立即把相机吸附到合法位置（开局、复活、传送、测试用）。</summary>
        public void SnapImmediately()
        {
            if (target == null)
                return;

            ResolveActorRefs();
            _shotKickDistance = 0f;
            _shotKickRoll = 0f;
            _shotKickFov = 0f;
            _yaw = Mathf.Repeat(target.eulerAngles.y + 180f, 360f) - 180f;
            _pitch = gameConfig != null
                ? ThirdPersonCameraMath.ClampPitch(gameConfig.tpsPitchDefault, gameConfig.tpsPitchMin, gameConfig.tpsPitchMax)
                : 6f;

            _pivot = target.position + Vector3.up * GetPivotHeight();
            _pivotVelocity = Vector3.zero;
            _lastTargetPosition = target.position;
            _shoulder = 0f;

            Quaternion lookRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 forward = lookRotation * Vector3.forward;
            _currentDistance = ResolveBoomDistance(_pivot, forward, GetBaseDistance());

            Vector3 position = _pivot - forward * _currentDistance;
            position = ApplyGroundClearance(position);

            transform.position = position;
            transform.rotation = lookRotation;

            _smoothedFov = GetBaseFov();
            if (_cam != null)
                _cam.fieldOfView = _smoothedFov;

            _hasSnapped = true;
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            if (!_hasSnapped)
            {
                SnapImmediately();
                return;
            }

            ResolveActorRefs();

            float dt = Mathf.Max(0f, Time.deltaTime);

            // 开火冲击始终按真实时间回弹：Hitstop 定格期间也要把镜头拉回来，否则会「卡住」
            DecayShotKick(Time.unscaledDeltaTime);

            // ---- 枢轴跟随：水平/垂直统一平滑 + 少量速度前馈，减少高速移动时的落后感
            Vector3 targetPosition = target.position;
            Vector3 targetDelta = targetPosition - _lastTargetPosition;
            if (targetDelta.sqrMagnitude > 36f)
            {
                // 复活 / 传送：直接吸附，避免镜头横穿整张地图
                SnapImmediately();
                return;
            }

            Vector3 feedForward = dt > 0.0001f
                ? Vector3.ClampMagnitude(targetDelta / dt * 0.03f, 0.8f)
                : Vector3.zero;
            _lastTargetPosition = targetPosition;

            float smoothTime = gameConfig != null ? Mathf.Max(0.01f, gameConfig.tpsPivotSmoothTime) : 0.06f;
            Vector3 pivotTarget = targetPosition + Vector3.up * GetPivotHeight() + feedForward;
            if (dt > 0.0001f)
            {
                _pivot = Vector3.SmoothDamp(
                    _pivot, pivotTarget, ref _pivotVelocity, smoothTime, Mathf.Infinity, dt);
            }
            else
            {
                _pivot = pivotTarget;
            }

            // ---- 朝向与吊臂
            Quaternion lookRotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 forward = lookRotation * Vector3.forward;
            Vector3 right = Quaternion.Euler(0f, _yaw, 0f) * Vector3.right;

            // 只有蓄力狙在蓄力时拉近镜头；加特林等武器的 Charge01 用于自身机制，不应驱动相机变焦
            float charge01 = 0f;
            if (_weapon != null && _weapon.CurrentWeaponKind == WeaponKind.Charger)
                charge01 = Mathf.Clamp01(_weapon.Charge01);

            float targetDistance = GetBaseDistance();
            if (charge01 > 0f)
                targetDistance -= charge01 * (gameConfig != null ? gameConfig.tpsChargerZoomDistance : 0.6f);
            if (_isSubAimActive)
                targetDistance += gameConfig != null ? gameConfig.tpsSubAimDistanceKick : 0.35f;

            float allowedDistance = ResolveBoomDistance(_pivot, forward, targetDistance);
            if (dt > 0.0001f)
            {
                // 遇障瞬间收进（快），离开障碍缓慢退回（慢），避免镜头抽搐
                float speed = allowedDistance < _currentDistance ? 60f : 8f;
                _currentDistance = Mathf.MoveTowards(_currentDistance, allowedDistance, speed * dt);
            }
            else
            {
                _currentDistance = allowedDistance;
            }

            float minDistance = GetMinDistance();
            _currentDistance = Mathf.Max(minDistance, _currentDistance);

            // ---- 近距离右肩偏移：吊臂被墙压短时，把镜头向右移，避免角色后脑勺挡住准星
            float closeStart = gameConfig != null ? gameConfig.tpsShoulderCloseDistance : 2.8f;
            float close01 = Mathf.Clamp01((closeStart - _currentDistance) / Mathf.Max(0.01f, closeStart - minDistance));
            close01 = close01 * close01 * (3f - 2f * close01); // smoothstep
            float shoulderTarget = close01 * (gameConfig != null ? gameConfig.tpsShoulderOffset : 0.55f);

            Vector3 cameraBase = _pivot - forward * _currentDistance;
            if (shoulderTarget > 0.01f &&
                TryRaycastIgnoringCombatants(cameraBase, right, out RaycastHit shoulderHit, shoulderTarget + 0.25f))
            {
                shoulderTarget = Mathf.Min(shoulderTarget, Mathf.Max(0f, shoulderHit.distance - 0.25f));
            }

            _shoulder = dt > 0.0001f
                ? Mathf.MoveTowards(_shoulder, shoulderTarget, 6f * dt)
                : shoulderTarget;

            Vector3 position = cameraBase + right * _shoulder;
            position = ApplyGroundClearance(position);

            ApplyShake(ref position);

            // 沿视轴后拉：相机原点在中心射线上滑动，射线本身不变 → 弹道落点严格不变
            position -= forward * _shotKickDistance;

            transform.position = position;
            transform.rotation = lookRotation * Quaternion.Euler(0f, 0f, _shakeRoll + _shotKickRoll);

            // ---- FOV：蓄力狙变焦 + 手雷瞄准微收 + 开火冲击 + 速度感拉伸
            float targetFov = GetBaseFov() + _shotKickFov;
            if (charge01 > 0f)
                targetFov -= charge01 * (gameConfig != null ? gameConfig.tpsChargerZoomFov : 14f);
            if (_isSubAimActive)
                targetFov -= gameConfig != null ? gameConfig.tpsSubAimFovKick : 4f;
            if (gameConfig != null && gameConfig.tpsSpeedFovKick > 0f && _motor != null)
            {
                // 速度感：以人形跑速为起点、潜墨全速为终点做 0~1 归一，保守拉伸 FOV（INKWAVE 无速度线，速度感靠本体+镜头）
                float startSpeed = Mathf.Max(0.1f, gameConfig.tpsSpeedFovStartSpeed);
                float fullSpeed = _motor.Config != null ? _motor.Config.swimMoveSpeed : 11.8f;
                float speedK = Mathf.Clamp01((_motor.EffectiveMoveSpeed - startSpeed) / Mathf.Max(0.1f, fullSpeed - startSpeed));
                targetFov += speedK * gameConfig.tpsSpeedFovKick;
            }
            targetFov = Mathf.Clamp(targetFov, 25f, 90f);

            if (dt > 0.0001f)
                _smoothedFov = Mathf.MoveTowards(_smoothedFov, targetFov, 60f * dt);
            else
                _smoothedFov = targetFov;

            if (_cam != null)
                _cam.fieldOfView = _smoothedFov;
        }

        private void ApplyCameraSettings()
        {
            if (_cam == null)
                _cam = GetComponent<Camera>();

            _cam.orthographic = false;
            _cam.fieldOfView = GetBaseFov();
            _cam.nearClipPlane = 0.1f;
            _cam.farClipPlane = 160f;
            _smoothedFov = _cam.fieldOfView;
        }

        private void ResolveActorRefs()
        {
            if (target == null)
            {
                _motor = null;
                _weapon = null;
                return;
            }

            if (_motor == null)
                _motor = target.GetComponent<InkCharacterMotor>();
            if (_weapon == null)
                _weapon = target.GetComponent<WeaponController>();
        }

        private float GetPivotHeight()
        {
            float standing = gameConfig != null ? gameConfig.tpsPivotHeight : 1.45f;
            float swimming = gameConfig != null ? gameConfig.tpsPivotHeightSwimming : 1.05f;
            bool isSwimming = _motor != null && _motor.IsSwimming;
            return ThirdPersonCameraMath.ComputePivotHeight(isSwimming, standing, swimming);
        }

        private float GetBaseDistance()
        {
            bool isSwimming = _motor != null && _motor.IsSwimming;
            float standing = gameConfig != null ? gameConfig.tpsDistance : 4.5f;
            float swimming = gameConfig != null ? gameConfig.tpsDistanceSwimming : 4.1f;
            return isSwimming ? swimming : standing;
        }

        private float GetMinDistance()
        {
            return gameConfig != null ? Mathf.Max(0.1f, gameConfig.tpsMinDistance) : 0.55f;
        }

        private float GetBaseFov()
        {
            return gameConfig != null ? Mathf.Clamp(gameConfig.tpsFov, 25f, 100f) : 52f;
        }

        /// <summary>
        /// 从枢轴向后做球体扫掠，返回不被墙体截断的最大吊臂长度。
        /// 跳过角色碰撞体；跳过远低于枢轴的水平地面（由 <see cref="ApplyGroundClearance"/> 兜底）。
        /// </summary>
        private float ResolveBoomDistance(Vector3 pivot, Vector3 forward, float desiredDistance)
        {
            float minDistance = GetMinDistance();
            desiredDistance = Mathf.Max(minDistance, desiredDistance);

            float radius = gameConfig != null ? Mathf.Max(0.05f, gameConfig.tpsCollisionRadius) : 0.32f;
            Vector3 origin = pivot + Vector3.up * 0.1f;
            Vector3 direction = -forward;

            int count = Physics.SphereCastNonAlloc(
                origin, radius, direction, _castHits, desiredDistance, ~0, QueryTriggerInteraction.Ignore);

            float allowed = desiredDistance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _castHits[i];
                Collider col = hit.collider;
                if (col == null)
                    continue;

                if (col.GetComponentInParent<CombatantHealth>() != null)
                    continue;

                // 远低于枢轴的地面/平台交给地面保护，避免抬头时被地面把镜头吸近
                if (hit.normal.y > 0.7f && hit.point.y < origin.y - 0.8f)
                    continue;

                float distance = Mathf.Max(0f, hit.distance - 0.05f);
                if (distance < allowed)
                    allowed = distance;
            }

            return Mathf.Max(minDistance, allowed);
        }

        /// <summary>从相机向下探测地面，保证镜头不会钻进地面。</summary>
        private Vector3 ApplyGroundClearance(Vector3 position)
        {
            float clearance = gameConfig != null ? Mathf.Max(0f, gameConfig.tpsGroundClearance) : 0.25f;
            if (TryRaycastIgnoringCombatants(position + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 8f))
            {
                float minimumY = hit.point.y + clearance;
                if (position.y < minimumY)
                    position.y = minimumY;
            }

            return position;
        }

        /// <summary>忽略所有角色碰撞体的射线检测（地面保护、肩部避墙用）。</summary>
        private bool TryRaycastIgnoringCombatants(
            Vector3 origin,
            Vector3 direction,
            out RaycastHit result,
            float maxDistance)
        {
            result = default;
            int count = Physics.RaycastNonAlloc(
                origin, direction, _castHits, maxDistance, ~0, QueryTriggerInteraction.Ignore);

            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _castHits[i];
                if (hit.collider == null)
                    continue;
                if (hit.collider.GetComponentInParent<CombatantHealth>() != null)
                    continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    result = hit;
                }
            }

            return best < float.PositiveInfinity;
        }

        /// <summary>
        /// Trauma 驱动的三层高频震颤（水平 X/Z + 镜头 Roll）：
        /// 用 trauma² 做响应曲线（小伤害几乎不动、大爆炸才有劲），
        /// 用层叠正弦代替逐帧随机噪声，并用真实时间推进让 Hitstop 定格期间震屏仍在继续。
        /// TPS 下位移额外乘 tpsShakeScale，避免 4.5m 吊臂下 0.42m 震幅过强。
        /// </summary>
        private void ApplyShake(ref Vector3 position)
        {
            float decay = gameConfig != null ? gameConfig.shakeDecayPerSecond : 2.4f;
            float baseOffset = gameConfig != null ? gameConfig.shakeMaxOffset : 0.42f;
            float tpsScale = gameConfig != null ? Mathf.Clamp01(gameConfig.tpsShakeScale) : 0.45f;
            float maxOffset = baseOffset * tpsScale;
            float maxRoll = gameConfig != null ? gameConfig.shakeMaxRollDegrees : 1.3f;
            float frequency = gameConfig != null ? gameConfig.shakeFrequency : 38f;

            if (_trauma <= 0.001f)
            {
                _shakeRoll = Mathf.MoveTowards(_shakeRoll, 0f, 20f * Time.unscaledDeltaTime);
                return;
            }

            float t = Time.unscaledTime;
            float shake = _trauma * _trauma * GameSettingsService.ShakeScale;

            float noiseX = Mathf.Sin(t * frequency) * 0.62f + Mathf.Sin(t * frequency * 1.63f + 1.7f) * 0.38f;
            float noiseZ = Mathf.Sin(t * frequency * 0.86f + 0.9f) * 0.58f + Mathf.Sin(t * frequency * 1.47f + 2.4f) * 0.42f;
            float noiseR = Mathf.Sin(t * frequency * 0.71f + 2.1f) * 0.6f + Mathf.Sin(t * frequency * 1.31f + 0.4f) * 0.4f;

            position += new Vector3(noiseX * maxOffset, 0f, noiseZ * maxOffset) * shake;
            _shakeRoll = noiseR * maxRoll * shake;

            _trauma = Mathf.Max(0f, _trauma - decay * Time.unscaledDeltaTime);
        }
    }
}
