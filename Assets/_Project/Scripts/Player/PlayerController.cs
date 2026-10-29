using P1.Core;
using P1.Ink;
using P1.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;

namespace P1.Player
{
    /// <summary>
    /// 主角控制器：读取键鼠/手柄/触控输入，驱动移动、俯视地面瞄准准星、潜行切换、主武器连发、延时手雷与砸地大招。
    /// </summary>
    [RequireComponent(typeof(InkCharacterMotor))]
    [RequireComponent(typeof(WeaponController))]
    [RequireComponent(typeof(CombatantHealth))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController LocalPlayer { get; private set; }

        private InkCharacterMotor _motor;
        private WeaponController _weapon;
        private CombatantHealth _health;

        private Vector3 _aimGroundPoint;
        private Vector3 _aimDirection = Vector3.forward;
        private Transform _reticleTransform;
        private MeshRenderer _reticleRenderer;
        private MaterialPropertyBlock _propBlock;
        private GroundAimReticle _groundReticle;
        private SubWeaponThrowTrajectory _subTrajectory;
        private bool _isAimingBomb;
        private readonly SwimFormInputArbiter _swimInput = new SwimFormInputArbiter();

        // 供移动端虚拟摇杆注入的覆盖输入
        public Vector2 TouchMoveInput { get; set; }
        public Vector2 TouchAimInput { get; set; }
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

        private void OnDestroy()
        {
            if (LocalPlayer == this)
                LocalPlayer = null;
            if (_reticleTransform != null)
                Destroy(_reticleTransform.gameObject);
        }

        private void Update()
        {
            if (!_health.IsAlive || !GameplayInputEnabled || !MatchManager.InputAllowed)
            {
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

            // 1. 读取移动输入（WASD / 手柄左摇杆 / 触屏左摇杆）
            Vector2 move2D = ReadMoveInput();
            Vector3 moveWorld = new Vector3(move2D.x, 0f, move2D.y);

            // 2. 读取动作按键
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

            // 3. 读取瞄准落点（鼠标地面射线 / 手柄右摇杆 / 触屏右摇杆）
            UpdateAimPoint(moveWorld);

            // 4. 形态仲裁：最后一次按下的键决定形态，射击与潜行可随时互切
            //    脚下己方墨水、当前已在潜行、或身前是己方墨墙（可贴墙潜行上爬）都允许进入潜行
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

            // 5. 技能与开火触发（潜行中按开火会在本帧立即出枪）
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
                _weapon.TryFireMainWeapon(_aimDirection);
                isFiringOrActing = true;
            }

            // 6. 副武器 64 点抛物线与 2.7m 爆炸红圈更新
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

            // 7. 更新镜头 Look-ahead 与地面动态准星
            if (TopDownCameraFollow.Instance != null)
            {
                TopDownCameraFollow.Instance.SetAimGroundPoint(_aimGroundPoint);
            }

            UpdateGroundReticle();
        }

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

        private void UpdateAimPoint(Vector3 fallbackMoveDir)
        {
            float maxRange = _isAimingBomb
                ? (_weapon.Config != null ? _weapon.Config.bombMaxThrowDistance : 7.5f)
                : (_weapon.Config != null ? _weapon.Config.maxRange : 12f);

            // 触屏或手柄右摇杆优先
            Vector2 stickAim = TouchAimInput;
            var gp = Gamepad.current;
            if (gp != null && gp.rightStick.ReadValue().sqrMagnitude > stickAim.sqrMagnitude)
            {
                stickAim = gp.rightStick.ReadValue();
            }

            if (stickAim.sqrMagnitude > 0.04f)
            {
                Vector3 dir = new Vector3(stickAim.x, 0f, stickAim.y).normalized;
                _aimDirection = dir;
                _aimGroundPoint = transform.position + dir * (Mathf.Clamp01(stickAim.magnitude) * maxRange);
                return;
            }

            // 桌面端鼠标射线与玩家高度平面求交（移动端没有真实鼠标，忽略虚拟鼠标设备）
            var mouse = Mouse.current;
            Camera cam = TopDownCameraFollow.Instance != null ? TopDownCameraFollow.Instance.CameraComponent : Camera.main;
            if (!Application.isMobilePlatform && mouse != null && cam != null)
            {
                Vector2 mousePos = mouse.position.ReadValue();
                Ray ray = cam.ScreenPointToRay(mousePos);
                Plane groundPlane = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));

                if (groundPlane.Raycast(ray, out float enter))
                {
                    Vector3 hitPt = ray.GetPoint(enter);
                    Vector3 delta = hitPt - transform.position;
                    delta.y = 0f;

                    if (delta.sqrMagnitude > 0.01f)
                    {
                        _aimDirection = delta.normalized;
                    }

                    float clampedDist = Mathf.Min(delta.magnitude, maxRange);
                    _aimGroundPoint = transform.position + _aimDirection * clampedDist;

                    if (Physics.Raycast(_aimGroundPoint + Vector3.up * 5f, Vector3.down, out RaycastHit groundHit, 12f, ~0, QueryTriggerInteraction.Ignore))
                    {
                        _aimGroundPoint.y = groundHit.point.y;
                    }
                    return;
                }
            }

            if (fallbackMoveDir.sqrMagnitude > 0.05f)
            {
                _aimDirection = fallbackMoveDir.normalized;
                _aimGroundPoint = transform.position + _aimDirection * (maxRange * 0.65f);
            }
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

        private void BuildGroundReticle()
        {
            var go = new GameObject("PlayerGroundReticle");
            _groundReticle = go.AddComponent<GroundAimReticle>();

            // 保留原有 MeshFilter 与 MeshRenderer 字段以保持向后兼容性
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = VfxPool.BuildRingMesh(28, 0.80f, 0.65f);
            _reticleRenderer = go.AddComponent<MeshRenderer>();

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _reticleRenderer.sharedMaterial = new Material(sh);
            _reticleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _reticleRenderer.enabled = false; // 由 GroundAimReticle 程序化组件负责渲染多武器形态

            _reticleTransform = go.transform;
        }

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
