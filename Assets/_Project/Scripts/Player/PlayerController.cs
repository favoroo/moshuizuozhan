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

        private Vector3 _aimPoint;
        private Vector3 _aimGroundPoint;
        private Vector3 _aimDirection = Vector3.forward;
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
            _propBlock = new MaterialPropertyBlock();
            _subTrajectory = gameObject.AddComponent<SubWeaponThrowTrajectory>();
            MovementAudioDriver.EnsureDriver(gameObject);

            _aimDirection = transform.forward;
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
                _isAimingBomb = false;
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
            bool bombTap = ReadBombPressed();
            bool specialReadyPressed = ReadSpecialPressed() && _weapon.IsSpecialReady;

            // 副武器手雷瞄准与松手投掷仲裁
            bool executeBombThrow = false;
            if (bombTap)
            {
                executeBombThrow = true;
                _isAimingBomb = false;
            }
            else if (bombHeld && !_motor.IsSwimming && !_weapon.IsPerformingSpecial)
            {
                _isAimingBomb = true;
            }
            else if (_isAimingBomb && (bombReleased || !bombHeld))
            {
                executeBombThrow = true;
                _isAimingBomb = false;
            }

            if (swimHeld && _isAimingBomb)
            {
                _isAimingBomb = false;
            }

            // 4. 屏幕中心射线瞄准（手雷瞄准时额外保留地面落点）
            UpdateAimPoint();

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
                Vector3 throwTarget = _subTrajectory != null && _subTrajectory.IsShowing
                    ? _subTrajectory.CurrentLandingPosition
                    : _aimGroundPoint;
                _weapon.TryThrowSubBomb(throwTarget);
                isFiringOrActing = true;
            }
            else if (fireAllowed && !_isAimingBomb)
            {
                // 3D 瞄准点：弹道反解会从枪口解到准星命中的墙面/敌人/地面
                _weapon.TryFireMainWeapon(_aimDirection, _aimPoint);
                isFiringOrActing = true;
            }

            // 7. 副武器 64 点抛物线与 2.7m 爆炸红圈更新
            if (_isAimingBomb && _subTrajectory != null)
            {
                Vector3 startPos = transform.position + Vector3.up * 0.8f;
                float bombCost = _weapon.Config != null ? _weapon.Config.bombInkCost : 55f;
                bool enoughInk = _motor.CurrentInk >= bombCost;
                _subTrajectory.UpdateTrajectory(startPos, _aimGroundPoint, _weapon.Config, enoughInk, _health.Team);
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
            if (!touchDriven && Cursor.lockState == CursorLockMode.Locked && Mouse.current != null)
            {
                Vector2 mouseDelta = Vector2.ClampMagnitude(Mouse.current.delta.ReadValue(), 150f);
                yawDelta += mouseDelta.x * MouseLookDegreesPerPixel * mouseSensitivity;
                pitchDelta += -mouseDelta.y * MouseLookDegreesPerPixel * mouseSensitivity * invertSign;
            }

            // 手柄右摇杆：按住持续转向
            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Vector2 stick = gamepad.rightStick.ReadValue();
                if (stick.sqrMagnitude > GamepadLookDeadZone * GamepadLookDeadZone)
                {
                    yawDelta += stick.x * GamepadLookYawPerSecond * mouseSensitivity * Time.deltaTime;
                    pitchDelta += -stick.y * GamepadLookPitchPerSecond * mouseSensitivity * Time.deltaTime * invertSign;
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
                _aimPoint = transform.position + Vector3.up * AimChestHeight + _aimDirection * 6f;
                _aimGroundPoint = ProjectGroundPoint(_aimPoint, _aimDirection);
                return;
            }

            Vector3 forward = cameraFollow.LookForward;
            Vector3 chest = transform.position + Vector3.up * AimChestHeight;
            Vector3 origin = ThirdPersonCameraMath.ComputeAimRayOrigin(camera.transform.position, forward, chest);
            float maxDistance = Mathf.Max(5f, cameraFollow.AimRayDistance);

            Vector3 hitPoint = origin + forward * maxDistance;
            int hitCount = Physics.RaycastNonAlloc(
                origin, forward, _aimHits, maxDistance, ~0, QueryTriggerInteraction.Ignore);

            float nearest = float.PositiveInfinity;
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
                }
            }

            if (IsTouchDriven())
            {
                TryApplyMobileAimAssist(origin, forward, ref hitPoint);
            }

            _aimPoint = hitPoint;

            Vector3 flatAim = hitPoint - transform.position;
            flatAim.y = 0f;
            _aimDirection = flatAim.sqrMagnitude > 0.0001f
                ? flatAim.normalized
                : cameraFollow.PlanarForward;

            _aimGroundPoint = ProjectGroundPoint(hitPoint, _aimDirection);
        }

        /// <summary>
        /// 移动端轻量辅助瞄准（对齐 GamePlan「Android 一定角度内吸附最近敌人」）：
        /// 只在屏幕中心射线附近 0.8m 内、且在武器射程内、Linecast 无遮挡时，
        /// 把瞄准点吸附到敌人胸口；桌面不启用，不做自动锁定。
        /// </summary>
        private void TryApplyMobileAimAssist(Vector3 origin, Vector3 forward, ref Vector3 hitPoint)
        {
            var all = CombatantHealthRegistry.All;
            if (all == null)
                return;

            float maxRange = _weapon != null && _weapon.Config != null ? _weapon.Config.maxRange : 12f;
            maxRange = Mathf.Max(2f, maxRange);

            float bestScore = float.PositiveInfinity;
            Vector3 bestPoint = Vector3.zero;
            bool found = false;

            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth target = all[i];
                if (target == null || !target.IsAlive || target == _health)
                    continue;
                if (target.Team == _health.Team)
                    continue;

                InkCharacterMotor targetMotor = target.Motor;
                if (targetMotor != null && targetMotor.IsStealthed)
                    continue;

                Vector3 center = target.transform.position + Vector3.up * 0.95f;
                Vector3 toTarget = center - origin;
                float along = Vector3.Dot(toTarget, forward);
                if (along < 0.5f || along > maxRange)
                    continue;

                Vector3 closest = origin + forward * along;
                float perpendicular = Vector3.Distance(center, closest);
                if (perpendicular > 0.8f)
                    continue;

                if (!HasClearShot(origin, center, target))
                    continue;

                float score = perpendicular + along * 0.02f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestPoint = center;
                    found = true;
                }
            }

            if (found)
                hitPoint = bestPoint;
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
            if (TouchBombTriggered)
            {
                TouchBombTriggered = false;
                return true;
            }
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
