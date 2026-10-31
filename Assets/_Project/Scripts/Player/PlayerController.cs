using P1.Core;
using P1.Ink;
using P1.UI;
using P1.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace P1.Player
{
    /// <summary>
    /// 主角控制器（P9 第三人称）：
    /// 读取键鼠/手柄/触控输入，驱动相机相对移动、屏幕中心瞄准、潜行切换、主武器连发、延时手雷与砸地大招。
    /// 鼠标/右摇杆/右半屏滑屏控制视角；左键/开火键射击；准星固定在屏幕中心。
    /// </summary>
    [RequireComponent(typeof(InkCharacterMotor))]
    [RequireComponent(typeof(WeaponController))]
    [RequireComponent(typeof(CombatantHealth))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController LocalPlayer { get; private set; }

        private const float MouseLookDegreesPerPixel = 0.12f;
        private const float TouchLookDegreesPerPixel = 0.18f;
        private const float GamepadLookYawPerSecond = 180f;
        private const float GamepadLookPitchPerSecond = 110f;
        private const float GamepadLookDeadZone = 0.15f;
        private const float AimChestHeight = 1.3f;

        private InkCharacterMotor _motor;
        private WeaponController _weapon;
        private CombatantHealth _health;
        private CharacterController _cc;

        private Vector3 _aimPoint;
        private Vector3 _aimGroundPoint;
        private Vector3 _aimDirection = Vector3.forward;
        private Vector3 _aimDirectionFlat = Vector3.forward;
        private float _aimFlatDistance;
        private CombatantHealth _aimTarget;

        // 视角输入来源跟踪：辅助瞄准只在触屏/手柄驱动时生效，鼠标永不吸附
        private bool _lastLookWasGamepad;
        private float _gamepadEdgeTime;
        private Transform _reticleTransform;
        private MeshRenderer _reticleRenderer;
        private Mesh _reticleMesh;
        private Material _reticleMaterial;
        private MaterialPropertyBlock _propBlock;
        private GroundAimReticle _groundReticle;
        private SubWeaponThrowTrajectory _subTrajectory;
        private bool _isAimingBomb;
        private bool _cursorLockedByUs;
        private readonly SwimFormInputArbiter _swimInput = new SwimFormInputArbiter();
        private readonly SubBombThrowArbiter _bombArbiter = new SubBombThrowArbiter();
        private readonly RaycastHit[] _aimHits = new RaycastHit[8];

        // 供移动端虚拟摇杆/滑屏注入的输入
        public Vector2 TouchMoveInput { get; set; }

        /// <summary>旧版双摇杆右摇杆瞄准输入；P9 起不再读取，保留公开属性以兼容旧调用方。</summary>
        public Vector2 TouchAimInput { get; set; }

        /// <summary>本帧累计的触屏滑屏像素增量（x=右, y=上）。PlayerController 读取后清零。</summary>
        public Vector2 TouchLookDelta { get; set; }

        public bool TouchFireHeld { get; set; }
        public bool TouchSwimHeld { get; set; }
        public bool TouchJumpTriggered { get; set; }
        public bool TouchBombTriggered { get; set; }
        public bool TouchBombHeld { get; set; }
        public bool TouchSpecialTriggered { get; set; }

        public bool GameplayInputEnabled { get; set; } = true;
        public bool IsAimingBomb => _isAimingBomb;
        public InkCharacterMotor Motor => _motor;
        public WeaponController Weapon => _weapon;
        public CombatantHealth Health => _health;

        /// <summary>屏幕中心射线命中的 3D 世界点（可能是地面、墙面或敌人身体）。</summary>
        public Vector3 AimPoint => _aimPoint;

        /// <summary>瞄准点到玩家的水平距离（米）：与弹丸水平射程判定一致，供准星超距状态使用。</summary>
        public float AimFlatDistance => _aimFlatDistance;

        /// <summary>
        /// 屏幕中心射线命中的存活敌方；射向天空时可能是辅助瞄准锁定的目标。
        /// 跳过队友、自己与潜墨隐身目标。准星以此显示「锁定」状态。
        /// </summary>
        public CombatantHealth AimTarget => _aimTarget;

        /// <summary>含俯仰的 3D 瞄准方向（相机前向）；武器按它与 <see cref="AimPoint"/> 求实际弹道。</summary>
        public Vector3 AimDirection => _aimDirection;

        /// <summary>瞄准点向下的地面投影；小地图朝向、手雷落点与风暴大招使用。</summary>
        public Vector3 AimGroundPoint => _aimGroundPoint;

        public GroundAimReticle GroundReticle => _groundReticle;
        public SubWeaponThrowTrajectory SubTrajectory => _subTrajectory;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        private void Awake()
        {
            LocalPlayer = this;
            _motor = GetComponent<InkCharacterMotor>();
            _weapon = GetComponent<WeaponController>();
            _health = GetComponent<CombatantHealth>();
            _cc = GetComponent<CharacterController>();
            _propBlock = new MaterialPropertyBlock();
            _subTrajectory = gameObject.AddComponent<SubWeaponThrowTrajectory>();
            MovementAudioDriver.EnsureDriver(gameObject);

            _aimDirection = transform.forward;
            _aimDirectionFlat = transform.forward;
            _aimPoint = transform.position + Vector3.up * AimChestHeight + transform.forward * 6f;
            _aimGroundPoint = transform.position + transform.forward * 5f;
            BuildGroundReticle();
        }

        private void Start()
        {
            // 开局在主角脚下铺一圈本队墨水，方便直接测试潜行与小地图显示
            if (InkSurfaceController.Instance != null && _health != null)
            {
                InkSurfaceController.Instance.PaintCircle(transform.position, 2.6f, _health.Team);
            }

            // 出水快速过渡（0.07s）与开火输入缓冲（0.16s）参数走 PlayerConfigSO
            var cfg = _motor.Config;
            if (cfg != null)
            {
                _swimInput.EmergeTransitionTime = cfg.emergeTransitionTime;
                _swimInput.FireBufferDuration = cfg.fireBufferTime;
            }
        }

        private void OnDisable()
        {
            ReleaseCursor();
        }

        private void OnDestroy()
        {
            ReleaseCursor();

            if (LocalPlayer == this)
                LocalPlayer = null;
            if (_reticleTransform != null)
            {
                Destroy(_reticleTransform.gameObject);
                _reticleTransform = null;
            }
            if (_reticleMesh != null)
            {
                Destroy(_reticleMesh);
                _reticleMesh = null;
            }
            if (_reticleMaterial != null)
            {
                Destroy(_reticleMaterial);
                _reticleMaterial = null;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!Application.isPlaying)
                return;

            if (!hasFocus)
            {
                ReleaseCursor();
                return;
            }

            ApplyCursorState(GameplayInputEnabled && MatchManager.InputAllowed);
        }

        private void Update()
        {
            bool gameplayActive = GameplayInputEnabled && MatchManager.InputAllowed;
            ApplyCursorState(gameplayActive);

            if (!_health.IsAlive || !gameplayActive)
            {
                TouchLookDelta = Vector2.zero;
                _swimInput.Reset();
                _bombArbiter.Reset();
                _isAimingBomb = false;
                _aimTarget = null;
                _aimFlatDistance = 0f;
                if (_subTrajectory != null) _subTrajectory.Hide();
                if (TopDownCameraFollow.Instance != null) TopDownCameraFollow.Instance.SetSubAimActive(false);
                // 清空排队的一次性触控指令，避免复活/重开瞬间补触发
                TouchJumpTriggered = false;
                TouchBombTriggered = false;
                TouchBombHeld = false;
                TouchSpecialTriggered = false;
                _motor.SetFrameIntent(Vector3.zero, _aimDirection, false, false, false, _weapon.IsPerformingSpecial);
                if (_reticleTransform != null) _reticleTransform.gameObject.SetActive(false);
                return;
            }

            // 1. 视角输入（鼠标 / 右摇杆 / 触屏滑屏），必须先于移动与瞄准
            ApplyLookInput();

            // 2. 读取移动输入（WASD / 手柄左摇杆 / 触屏左摇杆）→ 相机相对世界方向
            Vector2 move2D = ReadMoveInput();
            Vector3 moveWorld = BuildCameraRelativeMove(move2D);

            // 3. 读取动作按键
            bool swimHeld = ReadSwimHeld();
            bool fireHeld = ReadFireHeld();
            bool wantsJump = ReadJumpPressed();
            bool bombHeld = ReadBombHeld();
            bool bombReleased = ReadBombReleased();
            bool bombPressed = ReadBombPressed();
            // 触屏「松手投掷」一次性事件在此消费（MobileTouchUI pointer-up 注入，语义与旧版一致）
            bool touchBombThrow = TouchBombTriggered;
            TouchBombTriggered = false;
            bool specialReadyPressed = ReadSpecialPressed() && _weapon.IsSpecialReady;

            // 副武器手雷「按住瞄准 / 松开投掷」仲裁（状态机见 SubBombThrowArbiter）：
            // 按下沿只瞄准，松开沿才投掷——修复「按下即投扣墨 + 松开补投被拒」的丢雷假象；
            // 瞄准经 wantsBombIntent 压住潜行并自动出水，潜泳中按雷键不再吞投掷
            bool executeBombThrow = _bombArbiter.Tick(
                bombPressed,
                bombHeld,
                bombReleased,
                touchBombThrow,
                _weapon.IsPerformingSpecial);
            _isAimingBomb = _bombArbiter.IsAiming;

            // 4. 屏幕中心射线瞄准（手雷瞄准时额外保留地面落点）
            UpdateAimPoint();

            // 4.1 枪口与身体姿态跟随视角俯仰：抬头时枪口指天，胸腔只分担一小部分，身体保持直立
            CharacterVisualController visual = _motor != null ? _motor.VisualController : null;
            if (visual != null && TopDownCameraFollow.Instance != null)
            {
                visual.SetAimPitch(TopDownCameraFollow.Instance.PitchDegrees);
            }

            // 5. 形态仲裁：最后一次按下的键决定形态，射击与潜行可随时互切
            bool wantsBombIntent = executeBombThrow || _isAimingBomb;
            bool swimAvailable = _motor.IsInOwnInk || _motor.IsSwimming || _motor.IsTouchingOwnInkWall;
            _swimInput.Update(
                swimHeld,
                fireHeld,
                wantsBombIntent,
                specialReadyPressed,
                swimAvailable,
                _weapon.IsPerformingSpecial,
                Time.deltaTime);

            bool wantsSwim = _swimInput.WantsSwim;
            bool wantsFire = _swimInput.WantsFire;
            // 出水快速过渡（0.07s）内暂扣扳机；过渡期内按下过的开火键进入缓冲队列，
            // 过渡结束立刻补射（含已松手的点击）——彻底杜绝吞键
            bool fireAllowed = !_swimInput.IsEmerging && (wantsFire || _swimInput.TryConsumeFireBuffer());

            // 6. 技能与开火触发（潜行中按开火会在本帧立即出枪）
            bool isFiringOrActing = false;
            if (specialReadyPressed)
            {
                _isAimingBomb = false;
                _weapon.TryActivateSpecial();
                isFiringOrActing = true;
            }
            else if (executeBombThrow)
            {
                // inkwave 弹道模型：以相机俯仰解算的初速真实抛出，远近由俯仰物理决定
                _weapon.TryThrowSubBombBallistic(ComputeBombThrowVelocity());
                isFiringOrActing = true;
            }
            else if (fireAllowed && !_isAimingBomb)
            {
                // 3D 瞄准点：弹道反解会从枪口解到准星命中的墙面/敌人/地面
                _weapon.TryFireMainWeapon(_aimDirection, _aimPoint);
                isFiringOrActing = true;
            }

            // 7. 副武器 64 点弹道仿真预览与预测落点环（与实飞同常数，所见即所得）
            if (_isAimingBomb && _subTrajectory != null)
            {
                Vector3 startPos = _weapon.GetSubBombMuzzlePosition();
                float bombCost = _weapon.Config != null ? _weapon.Config.bombInkCost : 70f;
                bool enoughInk = _motor.CurrentInk >= bombCost;
                _subTrajectory.UpdateTrajectorySim(
                    startPos, ComputeBombThrowVelocity(), _weapon.Config, enoughInk, _health.Team);
                if (TopDownCameraFollow.Instance != null)
                {
                    TopDownCameraFollow.Instance.SetSubAimActive(true);
                }
            }
            else
            {
                if (_subTrajectory != null && _subTrajectory.IsShowing)
                {
                    _subTrajectory.Hide();
                }
                if (TopDownCameraFollow.Instance != null && TopDownCameraFollow.Instance.IsSubAimActive)
                {
                    TopDownCameraFollow.Instance.SetSubAimActive(false);
                }
            }

            _motor.SetFrameIntent(
                moveWorld,
                _aimDirection,
                wantsSwim,
                wantsJump,
                isFiringOrActing || wantsFire,
                _weapon.IsPerformingSpecial);

            // 8. 兼容旧相机 API（TPS 下不做瞄准 Look-ahead，仅记录）
            if (TopDownCameraFollow.Instance != null)
            {
                TopDownCameraFollow.Instance.SetAimGroundPoint(_aimGroundPoint);
            }

            // 地面 3D 准星在第三人称下停用，准星完全由屏幕中心 WeaponReticleUI 表达
        }

        // ------------------------------------------------------------------
        // 视角输入
        // ------------------------------------------------------------------

        private void ApplyLookInput()
        {
            TopDownCameraFollow camera = TopDownCameraFollow.Instance;
            if (camera == null)
            {
                TouchLookDelta = Vector2.zero;
                return;
            }

            bool touchDriven = IsTouchDriven();
            float mouseSensitivity = Mathf.Max(0.01f, GameSettingsService.MouseLookSensitivity);
            float touchSensitivity = Mathf.Max(0.01f, GameSettingsService.TouchLookSensitivity);
            float invertSign = GameSettingsService.InvertLookY ? -1f : 1f;

            float yawDelta = 0f;
            float pitchDelta = 0f;

            // 桌面鼠标：仅在指针锁定时读取，避免暂停/选枪时误转视角
            bool mouseActive = false;
            if (!touchDriven && Cursor.lockState == CursorLockMode.Locked && Mouse.current != null)
            {
                Vector2 mouseDelta = Vector2.ClampMagnitude(Mouse.current.delta.ReadValue(), 150f);
                if (mouseDelta.sqrMagnitude > 0.01f)
                {
                    mouseActive = true;
                    yawDelta += mouseDelta.x * MouseLookDegreesPerPixel * mouseSensitivity;
                    pitchDelta += -mouseDelta.y * MouseLookDegreesPerPixel * mouseSensitivity * invertSign;
                }
            }

            if (mouseActive || touchDriven)
                _lastLookWasGamepad = false;

            // 手柄右摇杆：按住持续转向；推到边缘 0.16s 后偏航加速到 1.55x（对齐 INKWAVE 的快速 180° 手感）
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                float magnitude = stick.magnitude;
                if (magnitude > GamepadLookDeadZone)
                {
                    _gamepadEdgeTime = magnitude > 0.93f
                        ? Mathf.Min(0.5f, _gamepadEdgeTime + Time.deltaTime)
                        : Mathf.Max(0f, _gamepadEdgeTime - Time.deltaTime * 3f);
                    float edgeBoost = 1f + 0.55f * Mathf.Clamp01((_gamepadEdgeTime - 0.16f) / 0.3f);

                    yawDelta += stick.x * GamepadLookYawPerSecond * mouseSensitivity * edgeBoost * Time.deltaTime;
                    pitchDelta += -stick.y * GamepadLookPitchPerSecond * mouseSensitivity * Time.deltaTime * invertSign;
                    _lastLookWasGamepad = true;
                }
                else
                {
                    _gamepadEdgeTime = Mathf.Max(0f, _gamepadEdgeTime - Time.deltaTime * 3f);
                }
            }

            // 触屏：右半屏滑屏 / 开火键拖拽 / 手雷键拖拽累计的像素增量
            if (touchDriven)
            {
                Vector2 touchDelta = TouchLookDelta;
                TouchLookDelta = Vector2.zero;
                // 触屏增量是物理像素：按设备 DPI 归一到 400dpi 基准，
                // 否则同样一次物理滑动，500ppi 屏上的转角只有 260ppi 屏的一半左右。
                float dpiNorm = Mathf.Clamp(MobileUiMetrics.DefaultDpi / Mathf.Max(1f, Screen.dpi), 0.6f, 1.6f);
                yawDelta += touchDelta.x * TouchLookDegreesPerPixel * touchSensitivity * dpiNorm;
                pitchDelta += -touchDelta.y * TouchLookDegreesPerPixel * touchSensitivity * invertSign * dpiNorm;
            }
            else if (TouchLookDelta.sqrMagnitude > 0f)
            {
                TouchLookDelta = Vector2.zero;
            }

            if (Mathf.Abs(yawDelta) > 0.0001f || Mathf.Abs(pitchDelta) > 0.0001f)
            {
                camera.AddLookInput(yawDelta, pitchDelta);
            }
        }

        private Vector3 BuildCameraRelativeMove(Vector2 move2D)
        {
            TopDownCameraFollow camera = TopDownCameraFollow.Instance;
            if (camera == null)
            {
                // 裸 PlayerController 测试/极端兜底：维持旧世界轴映射
                return new Vector3(move2D.x, 0f, move2D.y);
            }

            return ThirdPersonCameraMath.ComputeMoveWorld(move2D, camera.PlanarForward, camera.PlanarRight);
        }

        // ------------------------------------------------------------------
        // 瞄准
        // ------------------------------------------------------------------

        private void UpdateAimPoint()
        {
            TopDownCameraFollow cameraFollow = TopDownCameraFollow.Instance;
            Camera camera = cameraFollow != null ? cameraFollow.CameraComponent : Camera.main;

            if (cameraFollow == null || camera == null)
            {
                _aimDirection = transform.forward;
                _aimDirectionFlat = transform.forward;
                _aimPoint = transform.position + Vector3.up * AimChestHeight + _aimDirection * 6f;
                _aimGroundPoint = ProjectGroundPoint(_aimPoint, _aimDirectionFlat);
                _aimTarget = null;
                _aimFlatDistance = FlatDistanceTo(_aimPoint);
                return;
            }

            Vector3 forward = cameraFollow.LookForward;
            Vector3 chest = transform.position + Vector3.up * AimChestHeight;
            // 相机 LateUpdate 尚未执行：用同一套几何预测本帧渲染位姿，
            // 否则快速甩枪时射线会与画面中心线错开（狙击枪最明显）。
            Vector3 origin = ThirdPersonCameraMath.ComputeAimRayOrigin(
                cameraFollow.GetAimCameraPosition(), forward, chest);
            float maxDistance = Mathf.Max(5f, cameraFollow.AimRayDistance);

            Vector3 hitPoint = origin + forward * maxDistance;
            int hitCount = Physics.RaycastNonAlloc(
                origin, forward, _aimHits, maxDistance, ~0, QueryTriggerInteraction.Ignore);

            float nearest = float.PositiveInfinity;
            bool rayHitWorld = false;
            CombatantHealth rayTarget = null;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _aimHits[i];
                if (hit.collider == null)
                    continue;
                if (hit.collider.transform.root == transform.root)
                    continue;

                if (hit.distance < nearest)
                {
                    nearest = hit.distance;
                    hitPoint = hit.point;
                    rayTarget = null;
                    rayHitWorld = false;

                    CombatantHealth candidate = hit.collider.GetComponentInParent<CombatantHealth>();
                    bool candidateIsTarget = candidate != null &&
                                             candidate != _health &&
                                             candidate.IsAlive &&
                                             candidate.Team.IsOpponentOf(_health.Team) &&
                                             !IsStealthed(candidate);
                    if (candidateIsTarget)
                    {
                        rayTarget = candidate;
                    }
                    else
                    {
                        rayHitWorld = true;
                    }
                }
            }

            CombatantHealth assistedTarget = TryApplyAimAssist(
                origin, forward, ref hitPoint, rayTarget, rayHitWorld);

            _aimTarget = assistedTarget != null ? assistedTarget : rayTarget;
            _aimPoint = hitPoint;
            _aimFlatDistance = FlatDistanceTo(hitPoint);

            // 3D 瞄准方向 = 相机前向（含俯仰）。武器再按「枪口 → 准星命中点」自行求解弹道，
            // 因此把准星抬到天上时，弹丸会真的朝天上飞，而不是被压平水平射出。
            _aimDirection = forward.sqrMagnitude > 0.0001f
                ? forward.normalized
                : cameraFollow.PlanarForward;

            // 水平方向只服务于角色朝向、地面投影与小地图（俯仰不参与朝向）
            Vector3 flatAim = new Vector3(_aimDirection.x, 0f, _aimDirection.z);
            _aimDirectionFlat = flatAim.sqrMagnitude > 0.0001f
                ? flatAim.normalized
                : cameraFollow.PlanarForward;

            _aimGroundPoint = ProjectGroundPoint(hitPoint, _aimDirectionFlat);
        }

        private float FlatDistanceTo(Vector3 worldPoint)
        {
            Vector3 delta = worldPoint - transform.position;
            delta.y = 0f;
            return delta.magnitude;
        }

        /// <summary>
        /// 按「相机俯仰 + 水平瞄准方向」解算手雷出手初速（inkwave throwVelocity 公式）：
        /// 平抛约 8m、满仰角最远约 10.5m、低头贴脚丢；附带 0.4× 自身水平速度继承，
        /// 跑动中投掷会顺势丢得更远。预览与实飞都走这里，保证所见即所得。
        /// </summary>
        private Vector3 ComputeBombThrowVelocity()
        {
            TopDownCameraFollow cameraFollow = TopDownCameraFollow.Instance;
            float pitchDegrees = cameraFollow != null ? cameraFollow.PitchDegrees : 0f;
            Vector3 carry = Vector3.zero;
            if (_cc != null)
            {
                carry = _cc.velocity;
                carry.y = 0f;
            }
            return InkBombTrajectoryUtility.ComputeThrowVelocity(_aimDirectionFlat, pitchDegrees, carry, _weapon.Config);
        }

        private static bool IsStealthed(CombatantHealth target)
        {
            InkCharacterMotor motor = target != null ? target.Motor : null;
            return motor != null && motor.IsStealthed;
        }

        /// <summary>
        /// 轻量辅助瞄准（触摸 / 手柄）：
        /// 1. 准星已压在敌人身上 → 只把这一个敌人的瞄准点微调到胸口；
        /// 2. 准星指向天空 → 按档位半径吸附附近敌人（远距甩枪辅助）；
        /// 3. 准星落在墙体 / 地面 → 绝不吸附，玩家瞄环境就必须打在环境上。
        /// 旧实现会在 0.5~1.2m 半径内把射线拽到准星旁边的敌人身上，
        /// 表现为「静止瞄墙也打偏」，因此收紧为「准星已经压住才微调」。
        /// 仅对触屏与手柄生效，鼠标永远不做自动锁定。
        /// </summary>
        private CombatantHealth TryApplyAimAssist(
            Vector3 origin,
            Vector3 forward,
            ref Vector3 hitPoint,
            CombatantHealth rayTarget,
            bool rayHitWorld)
        {
            if (!IsTouchDriven() && !_lastLookWasGamepad)
                return null;

            int aimAssistLevel = GameSettingsService.AimAssistLevel;
            if (aimAssistLevel <= GameSettingsMath.MinAimAssistLevel)
                return null;

            // 射线已经落在墙体 / 地面：任何吸附都会让弹道偏离准星。
            if (rayHitWorld)
                return null;

            float aimAssistRadius = GameSettingsMath.GetAimAssistRadius(aimAssistLevel);

            var all = CombatantHealthRegistry.All;
            if (all == null)
                return null;

            float maxRange = _weapon != null && _weapon.Config != null ? _weapon.Config.maxRange : 12f;
            maxRange = Mathf.Max(2f, maxRange);

            float bestScore = float.PositiveInfinity;
            Vector3 bestPoint = Vector3.zero;
            CombatantHealth bestTarget = null;

            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth target = all[i];
                if (target == null || !target.IsAlive || target == _health)
                    continue;
                if (target.Team == _health.Team)
                    continue;

                // 准星已经压在某个敌人身上时，只允许微调这个敌人，不能拐到旁边的另一个目标。
                if (rayTarget != null && target != rayTarget)
                    continue;

                if (IsStealthed(target))
                    continue;

                Vector3 center = target.transform.position + Vector3.up * 0.95f;
                Vector3 toTarget = center - origin;
                float along = Vector3.Dot(toTarget, forward);
                if (along < 0.5f || along > maxRange)
                    continue;

                Vector3 closest = origin + forward * along;
                float perpendicular = Vector3.Distance(center, closest);
                if (perpendicular > aimAssistRadius)
                    continue;

                if (!HasClearShot(origin, center, target))
                    continue;

                float score = perpendicular + along * 0.02f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestPoint = center;
                    bestTarget = target;
                }
            }

            if (bestTarget != null)
                hitPoint = bestPoint;

            return bestTarget;
        }

        private static bool HasClearShot(Vector3 origin, Vector3 target, CombatantHealth targetHealth)
        {
            if (Physics.Linecast(origin, target, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            {
                CombatantHealth hitHealth = hit.collider != null
                    ? hit.collider.GetComponentInParent<CombatantHealth>()
                    : null;
                if (hitHealth != targetHealth)
                    return false;
            }

            return true;
        }

        /// <summary>把 3D 瞄准点投影到脚下地面；无地面时退回玩家前方，供手雷/风暴/小地图使用。</summary>
        private Vector3 ProjectGroundPoint(Vector3 aimPoint, Vector3 flatDirection)
        {
            if (Physics.Raycast(
                    aimPoint + Vector3.up * 2f,
                    Vector3.down,
                    out RaycastHit groundHit,
                    12f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                if (groundHit.collider == null || groundHit.collider.transform.root != transform.root)
                {
                    return new Vector3(aimPoint.x, groundHit.point.y, aimPoint.z);
                }
            }

            float range = _weapon != null && _weapon.Config != null ? _weapon.Config.maxRange : 8f;
            Vector3 fallback = transform.position + flatDirection * Mathf.Min(Mathf.Max(1f, range), 12f);
            if (Physics.Raycast(
                    fallback + Vector3.up * 5f,
                    Vector3.down,
                    out groundHit,
                    12f,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return new Vector3(fallback.x, groundHit.point.y, fallback.z);
            }

            return fallback;
        }

        private static bool IsTouchDriven()
        {
            return Application.isMobilePlatform || MobileTouchUI.IsTouchSimulationActive;
        }

        // ------------------------------------------------------------------
        // 光标锁定
        // ------------------------------------------------------------------

        private void ApplyCursorState(bool gameplayActive)
        {
            if (!Application.isPlaying || Application.isMobilePlatform || Application.isBatchMode)
            {
                if (_cursorLockedByUs)
                    ReleaseCursor();
                return;
            }

            // 编辑器触屏模拟时用鼠标拖拽滑屏区，不再锁定鼠标
            if (MobileTouchUI.IsTouchSimulationActive)
            {
                if (_cursorLockedByUs)
                    ReleaseCursor();
                return;
            }

            if (gameplayActive)
            {
                if (Cursor.lockState != CursorLockMode.Locked)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
                _cursorLockedByUs = true;
            }
            else if (_cursorLockedByUs)
            {
                ReleaseCursor();
            }
        }

        private void ReleaseCursor()
        {
            if (!_cursorLockedByUs)
                return;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorLockedByUs = false;
        }

        // ------------------------------------------------------------------
        // 输入读取
        // ------------------------------------------------------------------

        private Vector2 ReadMoveInput()
        {
            Vector2 move = TouchMoveInput;

            var kb = Keyboard.current;
            if (kb != null)
            {
                float x = 0f, y = 0f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
                Vector2 kbVec = new Vector2(x, y);
                if (kbVec.sqrMagnitude > move.sqrMagnitude)
                    move = kbVec.normalized;
            }

            var gp = Gamepad.current;
            if (gp != null)
            {
                Vector2 stick = gp.leftStick.ReadValue();
                if (stick.sqrMagnitude > move.sqrMagnitude)
                    move = stick;
            }

            return Vector2.ClampMagnitude(move, 1f);
        }

        private bool ReadFireHeld()
        {
            if (TouchFireHeld) return true;
            if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;
            if (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed) return true;
            return false;
        }

        private bool ReadSwimHeld()
        {
            if (TouchSwimHeld) return true;
            if (Mouse.current != null && Mouse.current.rightButton.isPressed) return true;
            if (Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed) return true;
            if (Gamepad.current != null && Gamepad.current.leftTrigger.isPressed) return true;
            return false;
        }

        private bool ReadBombPressed()
        {
            // 仅桌面端按下沿；触屏「松手投掷」由 TouchBombTriggered 承载，在副武器仲裁处单独消费
            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame) return true;
            if (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame) return true;
            return false;
        }

        private bool ReadBombHeld()
        {
            if (TouchBombHeld) return true;
            if (Keyboard.current != null && Keyboard.current.eKey.isPressed) return true;
            if (Mouse.current != null && Mouse.current.middleButton.isPressed) return true;
            if (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed) return true;
            return false;
        }

        private bool ReadBombReleased()
        {
            if (Keyboard.current != null && Keyboard.current.eKey.wasReleasedThisFrame) return true;
            if (Mouse.current != null && Mouse.current.middleButton.wasReleasedThisFrame) return true;
            if (Gamepad.current != null && Gamepad.current.rightShoulder.wasReleasedThisFrame) return true;
            return false;
        }

        private bool ReadJumpPressed()
        {
            if (TouchJumpTriggered)
            {
                TouchJumpTriggered = false;
                return true;
            }
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) return true;
            if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame) return true;
            return false;
        }

        private bool ReadSpecialPressed()
        {
            if (TouchSpecialTriggered)
            {
                TouchSpecialTriggered = false;
                return true;
            }
            if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
                return true;
            if (Gamepad.current != null && Gamepad.current.leftShoulder.wasPressedThisFrame)
                return true;
            return false;
        }

        // ------------------------------------------------------------------
        // 旧版地面准星（TPS 下停用，保留组件与 API 供测试/未来使用）
        // ------------------------------------------------------------------

        private void BuildGroundReticle()
        {
            var go = new GameObject("PlayerGroundReticle");
            _groundReticle = go.AddComponent<GroundAimReticle>();

            // 保留原有 MeshFilter 与 MeshRenderer 字段以保持向后兼容性
            var mf = go.AddComponent<MeshFilter>();
            _reticleMesh = VfxPool.BuildRingMesh(28, 0.80f, 0.65f);
            mf.sharedMesh = _reticleMesh;
            _reticleRenderer = go.AddComponent<MeshRenderer>();

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _reticleMaterial = new Material(sh);
            _reticleRenderer.sharedMaterial = _reticleMaterial;
            _reticleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _reticleRenderer.enabled = false; // 由 GroundAimReticle 程序化组件负责渲染多武器形态

            _reticleTransform = go.transform;
            _reticleTransform.gameObject.SetActive(false);
        }

        /// <summary>
        /// 旧版贴地准星更新逻辑。第三人称下由屏幕中心准星取代，
        /// 保留该方法供调试开关注入或未来回归使用，Update 中不再调用。
        /// </summary>
        private void UpdateGroundReticle()
        {
            if (_reticleTransform == null) return;
            if (!_reticleTransform.gameObject.activeSelf)
                _reticleTransform.gameObject.SetActive(true);

            if (_groundReticle != null)
            {
                _groundReticle.UpdateReticle(_aimGroundPoint, transform.position, _weapon, _motor, _health);
            }
            else
            {
                _reticleTransform.position = new Vector3(_aimGroundPoint.x, _aimGroundPoint.y + 0.05f, _aimGroundPoint.z);

                Color c = _motor.Ink01 < 0.12f
                    ? new Color(1f, 0.2f, 0.2f, 0.9f)
                    : (_weapon.IsSpecialReady ? new Color(1f, 0.92f, 0.2f, 0.95f) : new Color(1f, 1f, 1f, 0.85f));

                _propBlock.SetColor(BaseColorProp, c);
                _propBlock.SetColor(ColorProp, c);
                if (_reticleRenderer != null)
                {
                    _reticleRenderer.enabled = true;
                    _reticleRenderer.SetPropertyBlock(_propBlock);
                }
            }
        }
    }
}
