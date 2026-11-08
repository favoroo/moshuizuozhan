using P1.Core;
using P1.Weapons;
using UnityEngine;
using UnityEngine.Rendering.Universal;

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

        /// <summary>
        /// 移动端「v0.1.8 新渲染路径」总开关：URP 后处理（tonemap/bloom/调色/暗角）+ 相机 FXAA。
        ///
        /// 背景（Android 真机回归，2026-11-01）：v0.1.7 移动端相机没有任何 UniversalAdditionalCameraData
        /// 覆盖 —— 既无后处理也无抗锯齿，URP 直接画进后缓冲；v0.1.8 新增了相机覆盖 + 后处理 + FXAA +
        /// 移动端全屏描边 Feature 之后，Android 切到别的 App 再切回来时 3D 画面整片消失，
        /// 只剩相机 clear color 被后处理上色（观感「全屏灰」），HUD / 触控 / 小地图 / 对局逻辑均正常。
        /// 这三项都属于「中间纹理 + 最终 Blit」链路，Android 恢复时重建 surface 后可能失效。
        ///
        /// `true`  = 保留 v0.1.8 的移动端后处理观感（先关掉移动端全屏描边 Feature 观察）；
        /// `false` = 完全回退 v0.1.7 的移动端直绘路径（无后处理、无 AA）——灰屏热修的兜底档。
        /// 桌面端不受影响（始终 SMAA + 后处理）。
        /// </summary>
        private static readonly bool MobilePostProcessingEnabled = true;

        // 开火镜头冲击（P16/P28）：只做「沿视轴后拉 + Roll + FOV」三种不改瞄准方向的偏移。
        // 相机朝向决定屏幕中心射线，任何 pitch/yaw 改动都会让准星漂移，所以这里绝不旋转照射轴。
        // P28：指数漏积分改为 AnimSpring1D 弹簧；后拉量由「热积累」驱动——
        // 单发重武器读作一次冲量，连发武器读作持续后拉，松手回弹。
        private AnimSpring1D _shotDistanceSpring = new AnimSpring1D(0f);
        private AnimSpring1D _shotRollSpring = new AnimSpring1D(0f);
        private AnimSpring1D _shotFovSpring = new AnimSpring1D(0f);
        private float _shotKickHeat;
        private float _shotKickDecayRate = 8.5f;
        private float _shotKickSpringOmega = 30f;

        // ---------------------------------------------------------------- Slam 镜头演出
        // 本地玩家释放 Tidal Slam 时只有「视觉体」离地 4.2m，Transform 与相机枢轴留在地面。
        // 默认机位（4.5m 吊臂 / 52° FOV / 6° 俯角）下顶点角色的纵向屏幕坐标约 1.17~1.50，
        // 整个人在画面上方之外 —— 这就是「放完大招看不见人跳到哪」的根因。
        // 演出只做「枢轴抬高 + 吊臂外拉 + FOV」三种不改照射轴的偏移，落地再用弹簧推近。
        private float _slamWeight;
        private float _slamWeightFrom;
        private float _slamWeightTo;
        private float _slamWeightElapsed;
        private float _slamWeightDuration;
        private int _slamStage; // 0=idle 1=wide 2=fall 3=impact-recover
        private AnimSpring1D _slamPushSpring = new AnimSpring1D(0f);
        private AnimSpring1D _slamFovSpring = new AnimSpring1D(0f);

        // ---------------------------------------------------------------- 阵亡观战镜头（对标 INKWAVE death cam）
        // 旧行为：本地玩家阵亡后身体立即隐藏，但相机仍以「已不可见的尸体」为跟随目标，
        // 整段复活倒计时就定格在空地上。新行为：先停在阵亡点看击杀爆墨，再转向并环绕击杀者，
        // 复活那一帧硬切回正常跟随（68m 对角线地图上做长距离混合会扫穿掩体，不做 INKWAVE 的 0.7s 回镜）。
        private bool _deathCamActive;
        private float _deathCamTime;
        private Transform _deathCamKiller;
        private CombatantHealth _deathCamKillerHealth;
        private bool _deathCamKillerLocked;
        private Vector3 _deathCamVictimPosition;
        private Vector3 _deathCamLastKillerPosition;
        private Vector3 _deathCamFocus;
        private Vector3 _deathCamFallbackDirection;

        // 跟随目标的生命值订阅（懒绑定：测试里的纯 Transform 目标没有 CombatantHealth 也安全）
        private Transform _boundTarget;
        private CombatantHealth _boundTargetHealth;

        /// <summary>观战机位球探针半径（米）。</summary>
        private const float DeathCamCollisionRadius = 0.3f;

        /// <summary>观战机位被墙挡住时允许收进的最近距离（米）。</summary>
        private const float DeathCamMinDistance = 1.4f;

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
            UnbindTargetHealth();
            UnbindDeathCamKiller();

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
        /// 开火镜头冲击（P16 旧签名，保留给击杀 FOV 冲击等调用方）：
        /// 沿视轴后拉 + 绕视轴 Roll + FOV 拉宽，弹簧回弹。三种偏移都不改变相机前向。
        /// </summary>
        public void AddShotPunch(
            float distanceMeters,
            float rollDegrees,
            float fovDegrees,
            float recoverSeconds = 0.12f)
        {
            float decayRate = 1f / Mathf.Clamp(recoverSeconds, 0.04f, 0.4f);
            AddShotPunchScaling(distanceMeters, rollDegrees, fovDegrees, decayRate, _shotKickSpringOmega);
        }

        /// <summary>
        /// P28 武器专用入口：按武器类别指定热积累衰减率与弹簧角频率。
        /// 后拉量走热积累（连发持续后拉、重武器单发冲一下），Roll/FOV 走速度冲量。
        /// 全部乘 <see cref="GameSettingsService.ShakeScale"/>，无障碍 0% 震屏时开火冲击完全消失。
        /// </summary>
        public void AddShotPunchScaling(
            float distanceMeters,
            float rollDegrees,
            float fovDegrees,
            float decayRatePerSecond,
            float springOmega)
        {
            if (distanceMeters <= 0f && rollDegrees <= 0f && fovDegrees <= 0f)
                return;

            float shakeScale = Mathf.Clamp01(GameSettingsService.ShakeScale);
            if (shakeScale <= 0f)
                return;

            distanceMeters = Mathf.Max(0f, distanceMeters) * shakeScale;
            rollDegrees *= shakeScale;
            fovDegrees = Mathf.Max(0f, fovDegrees) * shakeScale;

            _shotKickDecayRate = Mathf.Clamp(decayRatePerSecond, 2f, 20f);
            _shotKickSpringOmega = Mathf.Clamp(springOmega, 8f, 60f);

            // 热积累上限防止高射速武器把后拉累积成永久位移
            _shotKickHeat = Mathf.Min(0.22f, _shotKickHeat + distanceMeters);

            float impulseOmega = _shotKickSpringOmega * 1.15f;
            if (Mathf.Abs(rollDegrees) > 0.0001f)
            {
                _shotRollSpring.Kick(AnimSpring1D.ImpulseForPeak(
                    rollDegrees, impulseOmega, WeaponFeelMath.CameraKickDampingRatio));
            }
            if (fovDegrees > 0.0001f)
            {
                _shotFovSpring.Kick(AnimSpring1D.ImpulseForPeak(
                    fovDegrees, impulseOmega, WeaponFeelMath.CameraKickDampingRatio));
            }
        }

        /// <summary>当前开火后拉量（米），调试/测试可读。</summary>
        public float ShotKickDistance => _shotDistanceSpring.Value;

        /// <summary>当前开火 Roll 量（度），调试/测试可读。</summary>
        public float ShotKickRoll => _shotRollSpring.Value;

        /// <summary>当前开火 FOV 冲击量（度），调试/测试可读。</summary>
        public float ShotKickFov => _shotFovSpring.Value;

        /// <summary>当前 Slam 镜头演出权重（0=默认机位，1=完全展开），调试/测试可读。</summary>
        public float SlamPresentationWeight => _slamWeight;

        /// <summary>相机当前跟随的角色：大招演出只对「被跟随的本地玩家」生效。</summary>
        public Transform FollowTarget => target;

        /// <summary>阵亡观战镜头是否激活（复活/重开那一帧自动退出）。</summary>
        public bool IsDeathCamActive => _deathCamActive;

        /// <summary>当前观战的击杀者（环境死亡或观战结束时为 null）。</summary>
        public Transform DeathCamKiller => _deathCamKiller;

        /// <summary>
        /// 进入阵亡观战：前 0.8s 停在 <paramref name="victimPosition"/> 看击杀爆墨，
        /// 之后转向并在击杀者周围缓慢环绕（半径 4.2m → 5.5m）。
        /// 由跟随目标自己的 <see cref="CombatantHealth.OnKilled"/> 触发；也可被调试/测试直接调用。
        /// </summary>
        public void BeginDeathCam(Transform killer, Vector3 victimPosition)
        {
            if (killer == null)
                return;

            UnbindDeathCamKiller();

            _deathCamActive = true;
            _deathCamTime = 0f;
            _deathCamKiller = killer;
            _deathCamKillerHealth = killer.GetComponent<CombatantHealth>();
            _deathCamKillerLocked = false;
            _deathCamVictimPosition = victimPosition;
            _deathCamLastKillerPosition = killer.position;
            _deathCamFocus = victimPosition + Vector3.up * DeathCamMath.LookHeight;

            // 方向退化时保持死亡瞬间的机位方位（否则会凭空甩到世界 +Z）
            Vector3 fallback = transform.position - victimPosition;
            fallback.y = 0f;
            _deathCamFallbackDirection = fallback;

            if (_deathCamKillerHealth != null)
            {
                _deathCamKillerHealth.OnKilled += HandleDeathCamKillerKilled;
            }

            // 接管镜头：蓄力/开火后拉/大招演出/震屏残留全部清零，避免观战第一帧还在抖
            _shotKickHeat = 0f;
            _shotDistanceSpring.Reset();
            _shotRollSpring.Reset();
            _shotFovSpring.Reset();
            _slamWeight = 0f;
            _slamWeightFrom = 0f;
            _slamWeightTo = 0f;
            _slamWeightElapsed = 0f;
            _slamWeightDuration = 0f;
            _slamStage = 0;
            _slamPushSpring.Reset();
            _slamFovSpring.Reset();
            _trauma = 0f;
            _shakeRoll = 0f;
        }

        /// <summary>退出阵亡观战并硬切回正常跟随（复活瞬移回出生点，跨图长距离混合会扫穿掩体）。</summary>
        public void EndDeathCam()
        {
            if (!_deathCamActive)
                return;

            _deathCamActive = false;
            _deathCamKiller = null;
            _deathCamKillerLocked = false;
            UnbindDeathCamKiller();
            SnapImmediately();
        }

        private void UnbindDeathCamKiller()
        {
            if (_deathCamKillerHealth != null)
            {
                _deathCamKillerHealth.OnKilled -= HandleDeathCamKillerKilled;
            }
            _deathCamKillerHealth = null;
        }

        private void HandleDeathCamKillerKilled(CombatantHealth victim, Team killerTeam)
        {
            // 凶手中途阵亡：冻结最后已知位置，不把镜头拉回自己的尸体，也不跟去它的复活点
            if (victim != null && victim == _deathCamKillerHealth)
            {
                _deathCamKillerLocked = true;
            }
        }

        /// <summary>
        /// 推进阵亡观战镜头。位置与朝向都绕开正常跟随的 yaw/pitch/吊臂，
        /// 复活时 <see cref="EndDeathCam"/> 用 SnapImmediately 重置全部状态。
        /// </summary>
        private void StepDeathCam(float dt)
        {
            // 暂停 / 终场定格（timeScale=0）：观战镜头也跟着停，不能跳到目标机位
            if (dt <= 0.0001f)
                return;

            _deathCamTime += dt;

            // 凶手活着时才更新目标点；中途阵亡/被销毁就停在最后位置
            if (_deathCamKiller != null && !_deathCamKillerLocked &&
                (_deathCamKillerHealth == null || _deathCamKillerHealth.IsAlive))
            {
                _deathCamLastKillerPosition = _deathCamKiller.position;
            }

            // BeginDeathCam 保证有击杀者；凶手对象被销毁后仍保留最后已知位置（hasKiller 恒 true）
            Vector3 desiredFocus = DeathCamMath.ResolveFocusPoint(
                _deathCamTime, _deathCamVictimPosition, _deathCamLastKillerPosition, true);
            float lookRate = DeathCamMath.ResolveLookSmoothRate(_deathCamTime);
            _deathCamFocus = Vector3.Lerp(
                _deathCamFocus, desiredFocus, DeathCamMath.ExpSmoothFactor(lookRate, dt));

            Vector3 desiredPosition = DeathCamMath.ResolveCameraPosition(
                _deathCamTime, _deathCamVictimPosition, _deathCamFocus, _deathCamFallbackDirection);
            desiredPosition = ResolveDeathCamOcclusion(_deathCamFocus, desiredPosition);
            desiredPosition = ApplyGroundClearance(desiredPosition);

            transform.position = Vector3.Lerp(
                transform.position, desiredPosition,
                DeathCamMath.ExpSmoothFactor(DeathCamMath.PositionSmoothRate, dt));

            Vector3 lookDirection = _deathCamFocus - transform.position;
            if (lookDirection.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            }

            if (_cam != null)
            {
                float targetFov = DeathCamMath.ResolveFieldOfView(GetBaseFov());
                _smoothedFov = Mathf.MoveTowards(_smoothedFov, targetFov, 30f * dt);
                _cam.fieldOfView = _smoothedFov;
            }
        }

        /// <summary>
        /// 观战机位防穿墙：从视线焦点向机位做球探针，被墙挡住就把机位收到命中点前；
        /// 低于焦点的水平面（甲板/地面）交给 <see cref="ApplyGroundClearance"/>，
        /// 否则高机位俯视时会被脚下甲板把镜头吸到击杀者脸上。
        /// </summary>
        private Vector3 ResolveDeathCamOcclusion(Vector3 focus, Vector3 desired)
        {
            Vector3 offset = desired - focus;
            float distance = offset.magnitude;
            if (distance < 0.05f)
                return desired;

            Vector3 direction = offset / distance;
            int count = Physics.SphereCastNonAlloc(
                focus, DeathCamCollisionRadius, direction, _castHits, distance,
                GameLayers.WorldMask, QueryTriggerInteraction.Ignore);

            float allowed = distance;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _castHits[i];
                Collider col = hit.collider;
                if (col == null)
                    continue;
                if (col.GetComponentInParent<CombatantHealth>() != null)
                    continue;
                if (hit.normal.y > 0.7f && hit.point.y < focus.y - 0.8f)
                    continue;

                float clamped = Mathf.Max(0f, hit.distance - 0.05f);
                if (clamped < allowed)
                    allowed = clamped;
            }

            allowed = Mathf.Max(DeathCamMinDistance, allowed);
            return focus + direction * allowed;
        }


        /// <summary>
        /// Tidal Slam 起跳：缓出到广角机位（枢轴抬高 + 吊臂外拉 + FOV 加宽），
        /// 让 4.2m 升空的角色与脚下 5.2m 预警圈同时留在画面内。
        /// 只接受被跟随者本人的请求，Bot 大招不抢镜头。
        /// </summary>
        public void BeginSlamPresentation(Transform caster)
        {
            if (!IsSlamCaster(caster))
                return;

            _slamStage = 1;
            _slamPushSpring.Reset();
            _slamFovSpring.Reset();
            BlendSlamWeight(1f, SlamBlendOutSeconds);
        }

        /// <summary>下砸开始：快速收回高机位，镜头跟着角色俯冲。</summary>
        public void BeginSlamFall(Transform caster)
        {
            if (!IsSlamCaster(caster) || _slamStage == 0)
                return;

            _slamStage = 2;
            // 收回到接近默认机位：落地帧角色已回到地面，镜头不能还吊在 3.5m 高
            BlendSlamWeight(0.1f, SlamFallBlendSeconds);
        }

        /// <summary>落地：机位回默认 + 弹簧快速推近/FOV 收紧，配合震屏形成冲击。</summary>
        public void PlaySlamImpact(Transform caster)
        {
            if (!IsSlamCaster(caster))
                return;

            _slamStage = 3;
            BlendSlamWeight(0f, SlamRecoverSeconds);

            float omega = Mathf.Clamp(SlamImpactSpringOmega, 4f, 40f);
            float damping = Mathf.Clamp(SlamImpactDamping, 0.3f, 1f);
            _slamPushSpring.Kick(AnimSpring1D.ImpulseForPeak(
                Mathf.Max(0f, SlamImpactPush), omega, damping));
            _slamFovSpring.Kick(AnimSpring1D.ImpulseForPeak(
                Mathf.Max(0f, SlamImpactFov), omega * 1.1f, damping));
        }

        /// <summary>
        /// 异常收尾（终场哨 / 被取消）：快速回到默认机位。
        /// 落地后的恢复段（stage=3）不在这里截断，否则会把冲击推近吃掉。
        /// </summary>
        public void EndSlamPresentation(Transform caster)
        {
            if (!IsSlamCaster(caster) || _slamStage >= 3)
                return;

            _slamStage = 0;
            BlendSlamWeight(0f, 0.12f);
        }

        private bool IsSlamCaster(Transform caster)
        {
            return caster != null && target != null && caster == target;
        }

        private void BlendSlamWeight(float targetWeight, float duration)
        {
            _slamWeightFrom = _slamWeight;
            _slamWeightTo = Mathf.Clamp01(targetWeight);
            _slamWeightElapsed = 0f;
            _slamWeightDuration = Mathf.Max(0.01f, duration);
        }

        /// <summary>
        /// 推进 Slam 演出包络（真实时间）：权重用 smoothstep 缓入缓出，
        /// 落地冲击走 AnimSpring1D，Hitstop 定格期间镜头仍在动。
        /// </summary>
        private void StepSlamPresentation(float dt)
        {
            if (dt <= 0f)
                return;

            if (_slamWeightDuration > 0f)
            {
                _slamWeightElapsed += dt;
                float t = Mathf.Clamp01(_slamWeightElapsed / _slamWeightDuration);
                float eased = t * t * (3f - 2f * t);
                _slamWeight = Mathf.Lerp(_slamWeightFrom, _slamWeightTo, eased);
                if (t >= 1f)
                {
                    _slamWeight = _slamWeightTo;
                    _slamWeightDuration = 0f;
                }
            }

            float omega = Mathf.Clamp(SlamImpactSpringOmega, 4f, 40f);
            float damping = Mathf.Clamp(SlamImpactDamping, 0.3f, 1f);
            _slamPushSpring.Step(0f, omega, damping, dt);
            _slamFovSpring.Step(0f, omega * 1.1f, damping, dt);

            // 安全钳制：允许一丁点回弹，但绝不把吊臂/视野拉飞
            _slamPushSpring.Value = Mathf.Clamp(_slamPushSpring.Value, -0.15f, 1.6f);
            _slamFovSpring.Value = Mathf.Clamp(_slamFovSpring.Value, -0.3f, 6f);
        }

        private float SlamPivotRise => gameConfig != null ? Mathf.Max(0f, gameConfig.slamCameraPivotRise) : 2f;
        private float SlamDistanceBonus => gameConfig != null ? Mathf.Max(0f, gameConfig.slamCameraDistanceBonus) : 3f;
        private float SlamFovBonus => gameConfig != null ? Mathf.Max(0f, gameConfig.slamCameraFovBonus) : 4f;
        private float SlamBlendOutSeconds => gameConfig != null ? Mathf.Max(0.01f, gameConfig.slamCameraBlendOutSeconds) : 0.22f;
        private float SlamFallBlendSeconds => gameConfig != null ? Mathf.Max(0.01f, gameConfig.slamCameraFallBlendSeconds) : 0.12f;
        private float SlamRecoverSeconds => gameConfig != null ? Mathf.Max(0.01f, gameConfig.slamCameraRecoverSeconds) : 0.4f;
        private float SlamImpactPush => gameConfig != null ? Mathf.Max(0f, gameConfig.slamCameraImpactPush) : 0.9f;
        private float SlamImpactFov => gameConfig != null ? Mathf.Max(0f, gameConfig.slamCameraImpactFov) : 2.5f;
        private float SlamImpactSpringOmega => gameConfig != null ? gameConfig.slamCameraImpactSpringOmega : 14f;
        private float SlamImpactDamping => gameConfig != null ? gameConfig.slamCameraImpactDamping : 0.65f;

        private void StepShotKick(float unscaledDt)
        {
            if (unscaledDt <= 0f)
                return;

            // 热积累衰减：停止开火后视轴缓慢回弹；连续开火时形成稳定的持续后拉
            _shotKickHeat *= Mathf.Exp(-_shotKickDecayRate * unscaledDt);
            if (_shotKickHeat < 0.0002f) _shotKickHeat = 0f;

            float omega = _shotKickSpringOmega;
            _shotDistanceSpring.Step(_shotKickHeat, omega, WeaponFeelMath.CameraKickDampingRatio, unscaledDt);
            _shotRollSpring.Step(0f, omega * 1.15f, WeaponFeelMath.CameraKickDampingRatio, unscaledDt);
            _shotFovSpring.Step(0f, omega * 1.15f, WeaponFeelMath.CameraKickDampingRatio, unscaledDt);

            // 安全钳制：与旧上限一致，避免极端配置把镜头拉飞
            _shotDistanceSpring.Value = Mathf.Clamp(_shotDistanceSpring.Value, 0f, 0.22f);
            _shotRollSpring.Value = Mathf.Clamp(_shotRollSpring.Value, -2.5f, 2.5f);
            _shotFovSpring.Value = Mathf.Clamp(_shotFovSpring.Value, 0f, 4f);
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
            _shotDistanceSpring.Reset();
            _shotRollSpring.Reset();
            _shotFovSpring.Reset();
            _shotKickHeat = 0f;
            _slamWeight = 0f;
            _slamWeightFrom = 0f;
            _slamWeightTo = 0f;
            _slamWeightElapsed = 0f;
            _slamWeightDuration = 0f;
            _slamStage = 0;
            _slamPushSpring.Reset();
            _slamFovSpring.Reset();
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

            // 阵亡观战：完全接管机位/朝向/FOV，跳过正常跟随（复活那一帧 EndDeathCam 里 SnapImmediately）
            if (_deathCamActive)
            {
                StepDeathCam(dt);
                return;
            }

            // 开火冲击始终按真实时间回弹：Hitstop 定格期间也要把镜头拉回来，否则会「卡住」
            StepShotKick(Time.unscaledDeltaTime);
            // Slam 演出同理：落地微定格期间镜头推近/震屏要继续播完
            StepSlamPresentation(Time.unscaledDeltaTime);

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
            float pivotHeight = GetPivotHeight() + _slamWeight * SlamPivotRise;
            Vector3 pivotTarget = targetPosition + Vector3.up * pivotHeight + feedForward;
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
            // Slam 起跳外拉：与枢轴抬升配合，把升空角色与落点圈一起装进画面
            if (_slamWeight > 0f)
                targetDistance += _slamWeight * SlamDistanceBonus;

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
            position -= forward * _shotDistanceSpring.Value;
            // 落地推近：同样沿视轴滑动，不改变屏幕中心射线
            position += forward * _slamPushSpring.Value;

            // 二次夹紧：震屏位移、右肩偏移与开火后拉都发生在死角解算之后，
            // 贴墙 + 高 trauma 时可能把相机推出几何体（看到墙外/黑面）。这里沿枢轴→相机方向再查一次。
            position = ClampFinalCameraPosition(_pivot, position);

            transform.position = position;
            transform.rotation = lookRotation * Quaternion.Euler(0f, 0f, _shakeRoll + _shotRollSpring.Value);

            // ---- FOV：蓄力狙变焦 + 手雷瞄准微收 + 速度感拉伸
            // P28：开火 FOV 冲击不再进入 targetFov（否则会被 60°/s 平滑吃掉），改在最终值上叠加
            float targetFov = GetBaseFov();
            if (charge01 > 0f)
                targetFov -= charge01 * (gameConfig != null ? gameConfig.tpsChargerZoomFov : 14f);
            if (_isSubAimActive)
                targetFov -= gameConfig != null ? gameConfig.tpsSubAimFovKick : 4f;
            // Slam 起跳：FOV 轻微加宽，配合吊臂外拉强化「拉出去」的演出感
            if (_slamWeight > 0f)
                targetFov += _slamWeight * SlamFovBonus;
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
                _cam.fieldOfView = Mathf.Clamp(_smoothedFov + _shotFovSpring.Value - _slamFovSpring.Value, 25f, 90f);
        }

        /// <summary>
        /// 最终位置二次夹紧（P16-C 补强）：<see cref="ResolveBoomDistance"/> 与
        /// <see cref="ApplyGroundClearance"/> 都在震屏/右肩/开火后拉之前解算，
        /// 贴墙同时叠加高 trauma 时相机可能穿出墙面。这里沿「枢轴 → 最终相机位置」再做一次
        /// 球体扫掠，被挡住就把相机拉回枢轴方向（不低于吊臂最小距离的一半）。
        /// 只改渲染位置，不影响 <see cref="GetAimCameraPosition"/> 的瞄准射线。
        /// </summary>
        private Vector3 ClampFinalCameraPosition(Vector3 pivot, Vector3 position)
        {
            Vector3 offset = position - pivot;
            float length = offset.magnitude;
            if (length < 0.05f)
                return position;

            Vector3 direction = offset / length;
            float allowed = ResolveBoomDistance(pivot, -direction, length);
            float minimumKeep = GetMinDistance() * 0.5f;
            float clamped = Mathf.Max(minimumKeep, allowed);
            if (clamped >= length)
                return position;

            return pivot + direction * clamped;
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

            var data = _cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                // ArenaStyleSO 的画质总开关；样式资产尚未加载时默认开启（旧观感回退只改 SO）。
                var style = RenderEnvGlobals.CurrentStyle;
                bool styleWantsPost = style == null || style.postProcessingEnabled;
                data.renderPostProcessing = styleWantsPost &&
                    (!Application.isMobilePlatform || MobilePostProcessingEnabled);
                data.renderShadows = true;
                if (Application.isMobilePlatform)
                {
                    // 移动端抗锯齿跟随「新渲染路径」总开关：两档都不开时完全对齐 v0.1.7（无 AA）。
                    data.antialiasing = MobilePostProcessingEnabled
                        ? AntialiasingMode.FastApproximateAntialiasing
                        : AntialiasingMode.None;
                    data.antialiasingQuality = AntialiasingQuality.Low;
                }
                else
                {
#if UNITY_WEBGL && !UNITY_EDITOR
                    // WebGL 单线程 + 浏览器合成，SMAA High 成本偏高；用 FXAA 换更稳的帧率。
                    data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
                    data.antialiasingQuality = AntialiasingQuality.Low;
#else
                    data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    data.antialiasingQuality = AntialiasingQuality.High;
#endif
                }
            }
        }

        private void ResolveActorRefs()
        {
            BindTargetHealth();

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

        /// <summary>
        /// 订阅跟随目标（本地玩家）的阵亡/复活事件。只在 target 变化时重新绑定：
        /// 同一具身体的 OnKilled/OnRespawned 已经能覆盖整局，逐帧 GetComponent 没有必要。
        /// </summary>
        private void BindTargetHealth()
        {
            if (_boundTarget == target)
                return;

            // 换了一具身体（换地图 / 重开）：观战状态直接作废。
            // 这里不能走 EndDeathCam（它会 SnapImmediately → ResolveActorRefs → 再次进来），直接清标志位。
            if (_deathCamActive)
            {
                _deathCamActive = false;
                _deathCamKiller = null;
                _deathCamKillerLocked = false;
                UnbindDeathCamKiller();
            }

            UnbindTargetHealth();
            _boundTarget = target;
            _boundTargetHealth = target != null ? target.GetComponent<CombatantHealth>() : null;
            if (_boundTargetHealth != null)
            {
                _boundTargetHealth.OnKilled += HandleFollowTargetKilled;
                _boundTargetHealth.OnRespawned += HandleFollowTargetRespawned;
            }
        }

        private void UnbindTargetHealth()
        {
            if (_boundTargetHealth != null)
            {
                _boundTargetHealth.OnKilled -= HandleFollowTargetKilled;
                _boundTargetHealth.OnRespawned -= HandleFollowTargetRespawned;
            }
            _boundTargetHealth = null;
            _boundTarget = null;
        }

        private void HandleFollowTargetKilled(CombatantHealth victim, Team killerTeam)
        {
            if (victim == null || victim != _boundTargetHealth)
                return;

            // 只有「有存活凶手」的死亡才进观战：踩敌墨 / 越界等环境死亡没有可看的人，保持原机位
            CombatantHealth killer = victim.LastAttacker;
            if (killer == null || killer == victim || !killer.IsAlive)
                return;

            BeginDeathCam(killer.transform, victim.transform.position);
        }

        private void HandleFollowTargetRespawned(CombatantHealth victim)
        {
            if (victim != null && victim == _boundTargetHealth)
            {
                EndDeathCam();
            }
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
                origin, radius, direction, _castHits, desiredDistance, GameLayers.WorldMask, QueryTriggerInteraction.Ignore);

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
                origin, direction, _castHits, maxDistance, GameLayers.WorldMask, QueryTriggerInteraction.Ignore);

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
