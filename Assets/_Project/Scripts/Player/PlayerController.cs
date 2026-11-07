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
        /// <summary>投掷链路调试日志开关：默认关闭，避免 Development 构建每次操作分配字符串污染性能采样。</summary>
        private const bool VerboseBombDebug = false;
        private const float TouchLookDegreesPerPixel = 0.18f;
        private const float GamepadLookYawPerSecond = 180f;
        private const float GamepadLookPitchPerSecond = 110f;
        private const float GamepadLookDeadZone = 0.15f;
        private const float AimChestHeight = 1.3f;

        private InkCharacterMotor _motor;
        private WeaponController _weapon;
        private CombatantHealth _health;

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
        private bool _cursorLockedByUs;
        private readonly SwimFormInputArbiter _swimInput = new SwimFormInputArbiter();
        private readonly SubBombThrowArbiter _bombArbiter = new SubBombThrowArbiter();
        private readonly RaycastHit[] _aimHits = new RaycastHit[8];
        private bool _bombHeldLastFrame;

        // 供移动端虚拟摇杆/滑屏注入的输入
        public Vector2 TouchMoveInput { get; set; }

        /// <summary>旧版双摇杆右摇杆瞄准输入；P9 起不再读取，保留公开属性以兼容旧调用方。</summary>
        public Vector2 TouchAimInput { get; set; }

        /// <summary>本帧累计的触屏滑屏像素增量（x=右, y=上）。PlayerController 读取后清零。</summary>
        public Vector2 TouchLookDelta { get; set; }

        public bool TouchFireHeld { get; set; }
        public bool TouchSwimHeld { get; set; }
        public bool TouchJumpTriggered { get; set; }

        /// <summary>触屏手雷键 pointer-down 注入（进入瞄准）。</summary>
        public bool TouchBombTriggered { get; set; }

        /// <summary>触屏手雷键是否按住（按住期间显示弹道预览）。</summary>
        public bool TouchBombHeld { get; set; }

        /// <summary>触屏手雷键 pointer-up 注入（松手投出）。</summary>
        public bool TouchBombReleased { get; set; }

        public bool TouchSpecialTriggered { get; set; }

        public bool GameplayInputEnabled { get; set; } = true;

        /// <summary>是否正按住手雷瞄准（显示弹道预览 / 相机微收）。</summary>
        public bool IsAimingBomb => _bombArbiter.IsAiming;
        public InkCharacterMotor Motor => _motor;
        public WeaponController Weapon => _weapon;
        public CombatantHealth Health => _health;

        /// <summary>副武器投掷被拒的原因（供触屏按钮反馈与诊断日志）。</summary>
        public enum SubBombDenyReason
        {
            Unknown,
            /// <summary>手雷冷却中（唯一会触发按钮闪红的原因）。</summary>
            Cooldown,
            /// <summary>大招演出中（旧版行为：直接屏蔽副武器）。</summary>
            SpecialActive,
            /// <summary>已死亡。</summary>
            Dead,
            /// <summary>墨水不足一次投掷：按下即拒绝，不进入瞄准（直接播空墨提示）。</summary>
            NotEnoughInk
        }

        /// <summary>
        /// 副武器投掷被拒计数戳：每次因冷却被拒时 +1。
        /// MobileTouchUI 每帧轮询该值变化触发按钮闪红——用轮询而非事件订阅，
        /// 避免 LocalPlayer 重开/复活换实例时的订阅生命周期管理。
        /// </summary>
        public int SubBombDenyStamp { get; private set; }

        /// <summary>最近一次投掷被拒的原因（诊断用）。</summary>
        public SubBombDenyReason LastSubBombDenyReason { get; private set; } = SubBombDenyReason.Unknown;

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

        /// <summary>瞄准点向下的地面投影；小地图朝向与风暴大招落点使用。</summary>
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
            _propBlock = new MaterialPropertyBlock();
            _subTrajectory = gameObject.AddComponent<SubWeaponThrowTrajectory>();
            MovementAudioDriver.EnsureDriver(gameObject);

            _aimDirection = transform.forward;
            _aimDirectionFlat = transform.forward;
            _aimPoint = transform.position + Vector3.up * AimChestHeight + transform.forward * 6f;
            _aimGroundPoint = transform.position + transform.forward * 5f;
            BuildGroundReticle();

            // 低难度给本地玩家加血（新手 ×1.5 / 轻松 ×1.25）；只影响玩家，Bot 与队友保持基准。
            ApplyDifficultyHealthScale();
            GameSettingsService.BotDifficultyChanged += HandleBotDifficultyChanged;
        }

        private void HandleBotDifficultyChanged(int level)
        {
            ApplyDifficultyHealthScale();
        }

        private void ApplyDifficultyHealthScale()
        {
            if (_health == null) return;
            _health.SetMaxHealthScale(PlayerDifficultyMath.GetMaxHealthScale(GameSettingsService.BotDifficulty));
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
            GameSettingsService.BotDifficultyChanged -= HandleBotDifficultyChanged;

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

            ApplyCursorState(CursorShouldLock);
        }

        private void Update()
        {
            bool gameplayActive = GameplayInputEnabled && MatchManager.InputAllowed;
            ApplyCursorState(CursorShouldLock);

            if (!_health.IsAlive || !gameplayActive)
            {
                // 暂停 / 终场 / 死亡：显式告知武器「本帧输入被抑制」。
                // 否则 WeaponController 会把「这一帧没收到扳机」误当成松手，
                // 让蓄力狙自动开炮、加特林自动扫射、滚筒补一次挥击。
                // 同时把真实按键状态传过去：暂停中松手后恢复不会被误当作仍按住。
                if (_weapon != null)
                    _weapon.SetTriggerSuppressed(true, ReadFireHeld());

                TouchLookDelta = Vector2.zero;
                _swimInput.Reset();
                _bombArbiter.Reset();
                _aimTarget = null;
                _aimFlatDistance = 0f;
                if (_subTrajectory != null) _subTrajectory.Hide();
                if (TopDownCameraFollow.Instance != null) TopDownCameraFollow.Instance.SetSubAimActive(false);
                // 清空排队的一次性触控指令，避免复活/重开瞬间补触发
                TouchJumpTriggered = false;
                TouchBombTriggered = false;
                TouchBombHeld = false;
                TouchBombReleased = false;
                TouchSpecialTriggered = false;
                _bombHeldLastFrame = false;
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
            bool bombPressed = ReadBombPressed();
            bool bombHeld = ReadBombHeld();
            bool bombReleased = _bombHeldLastFrame && !bombHeld;
            _bombHeldLastFrame = bombHeld;

            // 触屏手雷键：pointer-down = 进入瞄准，pointer-up = 松手投出
            bool touchBombPressed = TouchBombTriggered;
            bool touchBombHeld = TouchBombHeld;
            bool touchBombReleased = TouchBombReleased;
            TouchBombTriggered = false;
            TouchBombReleased = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (VerboseBombDebug && (touchBombPressed || touchBombReleased))
            {
                Debug.Log($"[Bomb] 触屏注入: press={touchBombPressed} release={touchBombReleased} held={touchBombHeld} special={_weapon.IsPerformingSpecial}");
            }
#endif
            bool specialReadyPressed = ReadSpecialPressed() && _weapon.IsSpecialReady;

            // 4. 屏幕中心射线瞄准（必须先于手雷仲裁：手雷落点 = 准星命中的世界点）
            UpdateAimPoint();

            // 4.1 手雷：按住瞄准（显示弹道预览）、松手按准星落点投出（inkwave 手感）
            // 墨水不足一次投掷时按下沿直接拒绝：不进入瞄准、不出抛物线，改播「弹药耗尽」提示。
            bool bombHasInk = _weapon == null || _weapon.BombInkCost <= 0.001f
                              || (_motor != null && _motor.CurrentInk >= _weapon.BombInkCost);
            SubBombThrowArbiter.Intent bombIntent = _bombArbiter.Advance(
                bombPressed,
                bombHeld,
                bombReleased,
                touchBombPressed,
                touchBombHeld,
                touchBombReleased,
                _weapon.IsPerformingSpecial,
                bombHasInk);

            if (bombIntent.DeniedByInk)
            {
                LastSubBombDenyReason = SubBombDenyReason.NotEnoughInk;
                // 复用主武器空墨反馈：空打音效 + 准星红叉/震颤 + 底部「弹药耗尽」toast
                _weapon.ReportEmptyFeedback();
            }

            // 预览与实飞共用同一初速：本帧瞄准/投掷只解算一次，所见即所得
            Vector3 bombThrowVelocity = default;
            if (bombIntent.IsAiming || bombIntent.ExecuteThrow)
            {
                bombThrowVelocity = ComputeBombThrowVelocity();
            }
            UpdateBombAimPreview(bombIntent.IsAiming, bombThrowVelocity);

            // 4.2 枪口与身体姿态跟随视角俯仰：抬头时枪口指天，胸腔只分担一小部分，身体保持直立
            CharacterVisualController visual = _motor != null ? _motor.VisualController : null;
            if (visual != null && TopDownCameraFollow.Instance != null)
            {
                visual.SetAimPitch(TopDownCameraFollow.Instance.PitchDegrees);
            }

            // 5. 形态仲裁：最后一次按下的键决定形态，射击与潜行可随时互切
            bool wantsBombIntent = bombIntent.StartedAiming;
            // 对齐 Inkwave：乌贼形态不再要求脚下有己方墨 —— 无墨地面也可进入（干地慢速蹦行）；
            // 潜墨加成（高速/回墨/回血/隐匿）由 Motor 内部按脚下墨色单独判定
            bool swimAvailable = true;
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

            // 双枪战术翻滚：人形状态下在移动或开火中按下跳跃，触发战术翻滚（而非垂直跳跃）
            if (wantsJump && !wantsSwim && _weapon != null && _weapon.CurrentWeaponKind == WeaponKind.Dualies)
            {
                if (!_weapon.IsRollingDodge && _weapon.RollsRemaining > 0 && (fireHeld || wantsFire || moveWorld.sqrMagnitude > 0.04f))
                {
                    Vector3 rollDir = moveWorld.sqrMagnitude > 0.04f
                        ? moveWorld.normalized
                        : (_aimDirection.sqrMagnitude > 0.01f ? new Vector3(_aimDirection.x, 0f, _aimDirection.z).normalized : transform.forward);
                    if (_weapon.TryDodgeRoll(rollDir))
                    {
                        wantsJump = false;
                        isFiringOrActing = true;
                    }
                }
            }

            if (_weapon != null && _weapon.IsRollingDodge)
            {
                wantsJump = false;
                isFiringOrActing = true;
            }

            if (specialReadyPressed)
            {
                // 本帧主武器没有吃到扳机（放大了大招），必须显式抑制，
                // 否则蓄力狙/加特林会把这一帧当成松手直接开火。
                _weapon.SetTriggerSuppressed();
                _weapon.TryActivateSpecial();
                isFiringOrActing = true;
            }
            else if (bombIntent.ExecuteThrow)
            {
                // 投掷仲裁占用了本帧：同样抑制主武器，避免蓄力狙自动开炮、滚筒多挥一次。
                _weapon.SetTriggerSuppressed();

                // 准星落点反解：手雷落在屏幕中心命中的地面上（超距取最远 22m）
                bool thrown = _weapon.TryThrowSubBombBallistic(bombThrowVelocity);
                if (!thrown)
                {
                    // 冷却中松手立刻给显性反馈（触屏手雷键闪红 / 桌面冷却条保持显示），不再静默
                    LastSubBombDenyReason = !_health.IsAlive
                        ? SubBombDenyReason.Dead
                        : _weapon.IsPerformingSpecial
                            ? SubBombDenyReason.SpecialActive
                            : SubBombDenyReason.Cooldown;
                    if (LastSubBombDenyReason == SubBombDenyReason.Cooldown)
                    {
                        SubBombDenyStamp++;
                    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    if (VerboseBombDebug)
                    {
                        Debug.Log($"[Bomb] 投掷被拒: reason={LastSubBombDenyReason} touchRelease={touchBombReleased} cooldownRemaining={_weapon.BombCooldownRemaining:0.00}");
                    }
#endif
                }
                isFiringOrActing = true;
            }
            else if (fireAllowed)
            {
                // 3D 瞄准点：弹道反解会从枪口解到准星命中的墙面/敌人/地面
                _weapon.TryFireMainWeapon(_aimDirection, _aimPoint);
                isFiringOrActing = true;
            }

            _motor.SetFrameIntent(
                moveWorld,
                _aimDirection,
                wantsSwim,
                wantsJump,
                isFiringOrActing || wantsFire,
                _weapon.IsPerformingSpecial,
                // 副武器按住瞄准：预览弹道/落点圈按准星解算，身体也必须朝准星
                // （INKWAVE weapons.js aimingSub → fireFacing = 0.3）
                bombIntent.IsAiming);

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
                origin, forward, _aimHits, maxDistance, GameLayers.QueryMask, QueryTriggerInteraction.Ignore);

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
        /// 按「准星命中的世界点」反解手雷出手初速（指哪落哪）：
        /// 准星命中点先经 <see cref="InkBombTrajectoryUtility.ResolveThrowTarget"/> 钳制到最远投掷距离并贴地，
        /// 再由飞行时间反解初速；预览与实飞使用同一个结果，保证所见即所得。
        /// </summary>
        private Vector3 ComputeBombThrowVelocity()
        {
            Vector3 startPos = _weapon.GetSubBombMuzzlePosition();
            Vector3 targetPos = InkBombTrajectoryUtility.ResolveThrowTarget(startPos, _aimPoint, _weapon.Config);
            return InkBombTrajectoryUtility.ComputeThrowVelocityToLandAt(startPos, targetPos, _weapon.Config);
        }

        /// <summary>
        /// 手雷按住瞄准的预览与相机状态：瞄准时实时显示虚线弹道 + 落点预警圈，
        /// 松手/取消时立刻收起。预览的初速与实飞完全相同，落点圈贴合法线。
        /// </summary>
        private void UpdateBombAimPreview(bool aiming, Vector3 throwVelocity)
        {
            TopDownCameraFollow camera = TopDownCameraFollow.Instance;

            if (aiming)
            {
                if (camera != null)
                    camera.SetSubAimActive(true);

                if (_subTrajectory != null)
                {
                    // 冷却/缺墨时 hasEnoughInk=false：虚线变灰红闪烁，松手会走拒绝反馈
                    _subTrajectory.UpdateTrajectorySim(
                        _weapon.GetSubBombMuzzlePosition(),
                        throwVelocity,
                        _weapon.Config,
                        _weapon.CanThrowSubBombNow,
                        _health.Team);
                }
                return;
            }

            if (_subTrajectory != null && _subTrajectory.IsShowing)
                _subTrajectory.Hide();
            if (camera != null && camera.IsSubAimActive)
                camera.SetSubAimActive(false);
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
            if (Physics.Linecast(origin, target, out RaycastHit hit, GameLayers.QueryMask, QueryTriggerInteraction.Ignore))
            {
                CombatantHealth hitHealth = hit.collider != null
                    ? hit.collider.GetComponentInParent<CombatantHealth>()
                    : null;
                if (hitHealth != targetHealth)
                    return false;
            }

            return true;
        }

        /// <summary>把 3D 瞄准点投影到脚下地面；无地面时退回玩家前方，供风暴/小地图使用。</summary>
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

        /// <summary>
        /// 桌面光标是否该锁：对局允许输入 + 本地玩家存活。
        /// 阵亡期间阵亡整备条可点选，必须释放光标（移动端没有光标概念，不受影响）；
        /// 复活后本方法每帧重入，自动重新锁定。
        /// </summary>
        private bool CursorShouldLock =>
            GameplayInputEnabled && MatchManager.InputAllowed && (_health == null || _health.IsAlive);

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

            float x = 0f, y = 0f;
            if (DesktopKeyBindingService.IsActionPressed(DesktopAction.MoveLeft)) x -= 1f;
            if (DesktopKeyBindingService.IsActionPressed(DesktopAction.MoveRight)) x += 1f;
            if (DesktopKeyBindingService.IsActionPressed(DesktopAction.MoveBackward)) y -= 1f;
            if (DesktopKeyBindingService.IsActionPressed(DesktopAction.MoveForward)) y += 1f;
            Vector2 kbVec = new Vector2(x, y);
            if (kbVec.sqrMagnitude > move.sqrMagnitude)
                move = kbVec.normalized;

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
            if (DesktopKeyBindingService.IsActionPressed(DesktopAction.Fire)) return true;
            if (Gamepad.current != null && Gamepad.current.rightTrigger.isPressed) return true;
            return false;
        }

        private bool ReadSwimHeld()
        {
            if (TouchSwimHeld) return true;
            if (DesktopKeyBindingService.IsActionPressed(DesktopAction.Swim)) return true;
            if (Gamepad.current != null && Gamepad.current.leftTrigger.isPressed) return true;
            return false;
        }

        private bool ReadBombPressed()
        {
            // 仅桌面端按下沿；触屏 press 由 TouchBombTriggered 承载，在副武器仲裁处单独消费
            if (DesktopKeyBindingService.WasActionPressedThisFrame(DesktopAction.SubWeapon)) return true;
            if (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame) return true;
            return false;
        }

        private bool ReadBombHeld()
        {
            if (DesktopKeyBindingService.IsActionPressed(DesktopAction.SubWeapon)) return true;
            if (Gamepad.current != null && Gamepad.current.rightShoulder.isPressed) return true;
            return false;
        }

        private bool ReadJumpPressed()
        {
            if (TouchJumpTriggered)
            {
                TouchJumpTriggered = false;
                return true;
            }
            if (DesktopKeyBindingService.WasActionPressedThisFrame(DesktopAction.Jump)) return true;
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
            if (DesktopKeyBindingService.WasActionPressedThisFrame(DesktopAction.Special))
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
