using System;
using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 玩家与 Bot 共用的角色移动、墨水储罐、脚下墨色检测与潜行（乌贼）形态控制器。
    /// 含俯视角潜墨隐匿（Stealth）：己方墨水中静止/极慢潜行时对敌方完全隐形（模型隐藏，
    /// 己方半透明可见），高速潜墨在水面产生细微涟漪。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(CombatantHealth))]
    public class InkCharacterMotor : MonoBehaviour
    {
        [SerializeField] private PlayerConfigSO config;
        [SerializeField] private Transform visualRoot;
        [SerializeField] private GameObject weaponVisual;
        [SerializeField] private CharacterVisualController visualController;

        private CharacterController _cc;
        private CombatantHealth _health;

        private float _currentInk = 100f;
        private float _timeSinceLastInkSpend = 10f;
        private float _enemyInkSplatCooldown;
        private float _verticalVelocity = -2.0f;
        private Vector3 _moveInputWorld;
        private Vector3 _aimDirectionWorld = Vector3.forward;
        private bool _wantsToSwim;
        private bool _wantsJump;
        private bool _isFiringOrThrowing;
        private bool _isLockedBySpecial;

        // 敌墨「受击槽」：踩敌墨时锁定 1，离开敌墨后按速率快速恢复（惩罚状态的可读化退出）
        private float _enemyInkGrip01;
        private float _enemyInkRecoveryRatePerSec = 2.0f;

        // 高速潜墨涟漪触发计时
        private float _swimRippleTimer;

        // 超速惯性滑行（INKWAVE swim-exit glide）与高速急转弯甩墨状态
        private float _glideSpeed;
        private float _prevYaw;
        private float _swimCarveCooldown;

        // 隐匿（Stealth）视觉状态：模型渲染器缓存 + 半透明材质实例
        private Renderer[] _bodyRenderers;
        private Material[] _bodyOriginalMats;
        private readonly System.Collections.Generic.Dictionary<Renderer, Material> _ghostMatCache
            = new System.Collections.Generic.Dictionary<Renderer, Material>();
        private bool _stealthCachesBuilt;
        // 0 = 正常显示，1 = 己方半透明（幽灵），2 = 对敌方完全隐形
        private int _appliedVisualKind = -1;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        // 墙面潜行攀爬状态
        private readonly RaycastHit[] _wallProbeBuffer = new RaycastHit[8];
        private InkWallSurface _wallSurface;
        private Team _wallInkTeam = Team.Neutral;
        private Vector3 _wallNormal = Vector3.zero;
        private Vector3 _wallPlanePoint;
        private float _wallClimbCooldown;

        public PlayerConfigSO Config => config;
        public Team Team => _health != null ? _health.Team : Team.TeamA;
        public float CurrentInk => _currentInk;
        public float MaxInk => config != null ? config.maxInk : 100f;
        public float Ink01 => Mathf.Clamp01(_currentInk / MaxInk);

        public Team FootInkTeam { get; private set; }
        public CharacterVisualController VisualController => visualController;
        public bool IsSwimming { get; private set; }
        public bool IsGrounded => _cc != null && _cc.isGrounded;
        public bool IsInEnemyInk => FootInkTeam == Team.GetOpponent();
        public bool IsInOwnInk => FootInkTeam == Team;
        public float CurrentMoveSpeed { get; private set; }

        /// <summary>
        /// 实际生效的平面移速 (m/s)：超速惯性滑行（swim-exit glide）期间高于
        /// <see cref="CurrentMoveSpeed"/> 并按 swimExitGlideDecel 衰减回目标值。
        /// 相机 FOV 速度感与测试断言读取此值。
        /// </summary>
        public float EffectiveMoveSpeed { get; private set; }
        public Vector3 AimDirection => _aimDirectionWorld;

        /// <summary>
        /// 潜墨隐匿状态：在己方墨水中潜行且几乎静止时成立。
        /// 对敌方视角完全隐形（模型隐藏），己方视角半透明可见。
        /// </summary>
        public bool IsStealthed { get; private set; }

        /// <summary>隐匿状态变化事件（供 UI / Bot AI 等跨系统消费，避免轮询私有状态）。</summary>
        public event Action<InkCharacterMotor, bool> OnStealthChanged;

        /// <summary>
        /// 敌墨「受击槽」强度 0~1：踩敌墨瞬间锁满，离开敌墨后按
        /// <see cref="PlayerConfigSO.enemyInkRecoveryRatePerSec"/> 快速恢复（受击反馈的可读化退出）。
        /// </summary>
        public float EnemyInkGrip01 => _enemyInkGrip01;

        /// <summary>是否允许贴着己方墨墙潜行上爬（Bot 关闭，避免脱离寻路预期）。</summary>
        public bool AllowWallClimb { get; set; } = true;

        /// <summary>当前是否正在己方墨墙面上潜行攀爬。</summary>
        public bool IsWallClimbing { get; private set; }

        /// <summary>身前是否有可直接攀爬的己方墨墙（潜行键可用的条件之一）。</summary>
        public bool IsTouchingOwnInkWall => _wallSurface != null && _wallInkTeam == Team;
        public InkWallSurface CurrentWall => _wallSurface;

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _health = GetComponent<CombatantHealth>();
            _currentInk = MaxInk;

            _enemyInkRecoveryRatePerSec = config != null ? config.enemyInkRecoveryRatePerSec : 2.0f;

            _health.OnRespawned += HandleRespawned;

            _prevYaw = transform.eulerAngles.y;
            EffectiveMoveSpeed = config != null ? config.normalMoveSpeed : 6.0f;
            _glideSpeed = EffectiveMoveSpeed;
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.OnRespawned -= HandleRespawned;
            }

            if (_ghostMatCache != null && _ghostMatCache.Count > 0)
            {
                foreach (var kv in _ghostMatCache)
                {
                    if (kv.Value != null) Destroy(kv.Value);
                }
                _ghostMatCache.Clear();
            }
        }

        public void Configure(PlayerConfigSO playerConfig, Transform bodyVisual, GameObject gunVisual)
        {
            config = playerConfig;
            visualRoot = bodyVisual;
            weaponVisual = gunVisual;
            visualController = bodyVisual != null ? bodyVisual.GetComponent<CharacterVisualController>() : null;
            if (visualController == null && bodyVisual != null)
            {
                visualController = bodyVisual.GetComponentInChildren<CharacterVisualController>();
            }
            _currentInk = MaxInk;
            _enemyInkRecoveryRatePerSec = config != null ? config.enemyInkRecoveryRatePerSec : 2.0f;
        }

        private void HandleRespawned(CombatantHealth h)
        {
            _currentInk = MaxInk;
            IsSwimming = false;
            IsWallClimbing = false;
            _wallSurface = null;
            _wallInkTeam = Team.Neutral;
            _wallNormal = Vector3.zero;
            _wallClimbCooldown = 0f;
            _wantsToSwim = false;
            _wantsJump = false;
            _verticalVelocity = -2.0f;
            _isFiringOrThrowing = false;
            _isLockedBySpecial = false;

            // 复活清除隐匿 / 敌墨惩罚 / 涟漪与视觉残留（CombatantHealth 已重新启用其收集的渲染器，
            // 这里把幽灵材质实例一并复位）
            IsStealthed = false;
            _enemyInkGrip01 = 0f;
            _swimRippleTimer = 0f;
            _appliedVisualKind = -1;
            _glideSpeed = 0f;
            EffectiveMoveSpeed = 0f;
            _prevYaw = transform.eulerAngles.y;
            _swimCarveCooldown = 0f;
            if (_stealthCachesBuilt) ApplyStealthVisuals();

            if (visualRoot != null) visualRoot.localScale = Vector3.one;
            if (weaponVisual != null) weaponVisual.SetActive(true);
        }

        /// <summary>
        /// 兼容无跳跃意图的 4 参数调用（供 Bot 与基础移动使用）
        /// </summary>
        public void SetFrameIntent(
            Vector3 worldMoveDir,
            Vector3 worldAimDir,
            bool wantsSwim,
            bool isFiring)
        {
            SetFrameIntent(worldMoveDir, worldAimDir, wantsSwim, false, isFiring, false);
        }

        /// <summary>
        /// 完整移动输入（包含跳跃控制与大招状态锁定）
        /// </summary>
        public void SetFrameIntent(
            Vector3 worldMoveDir,
            Vector3 worldAimDir,
            bool wantsSwim,
            bool wantsJump,
            bool isFiring,
            bool lockedBySpecial = false)
        {
            _moveInputWorld = Vector3.ClampMagnitude(worldMoveDir, 1f);
            _moveInputWorld.y = 0f;

            worldAimDir.y = 0f;
            if (worldAimDir.sqrMagnitude > 0.001f)
            {
                _aimDirectionWorld = worldAimDir.normalized;
            }

            _wantsToSwim = wantsSwim;
            _wantsJump = wantsJump;
            _isFiringOrThrowing = isFiring;
            _isLockedBySpecial = lockedBySpecial;
        }

        public bool TryConsumeInk(float amount)
        {
            if (amount <= 0f) return true;
            if (_currentInk < amount)
                return false;

            _currentInk -= amount;
            _timeSinceLastInkSpend = 0f;
            return true;
        }

        public void RefillInkFull()
        {
            _currentInk = MaxInk;
        }

        private void Update()
        {
            if (_health != null && !_health.IsAlive)
            {
                return;
            }

            // 帧间隔钳制：编辑器卡顿/切后台会造成超大 dt，CharacterController 单步位移过大时会直接穿过掩体墙
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            _timeSinceLastInkSpend += Time.deltaTime;
            if (_wallClimbCooldown > 0f) _wallClimbCooldown -= dt;

            bool isGrounded = _cc != null && _cc.isGrounded;
            // 处于明显跳跃升空阶段（垂直速度向上超过阈值）时判定为腾空，避免受地面敌墨减速；其余地面与贴地时刻均正常采样脚下墨水
            bool isHighInAir = !isGrounded && _verticalVelocity > 0.5f;

            // 1. 查询脚下墨水归属
            FootInkTeam = (!isHighInAir && InkSurfaceController.Instance != null)
                ? InkSurfaceController.Instance.InkAt(transform.position)
                : Team.Neutral;

            // 2. 墙面探测：身前是否有可潜行攀爬的立面，以及该处墨水归属
            UpdateWallContact();

            // 3. 潜行（乌贼）形态仲裁：地面己方墨水 与 己方墨墙 都可以进入潜行
            bool wantsSwimForm = _wantsToSwim && !_isFiringOrThrowing && !_isLockedBySpecial;
            bool wallClimbPossible = wantsSwimForm && AllowWallClimb && _wallClimbCooldown <= 0f &&
                                     _wallSurface != null && _wallInkTeam == Team;

            bool pressingIntoWall = _moveInputWorld.sqrMagnitude > 0.04f &&
                                    Vector3.Dot(_moveInputWorld.normalized, -_wallNormal) > 0.2f;
            bool pressingAway = _moveInputWorld.sqrMagnitude > 0.04f &&
                                Vector3.Dot(_moveInputWorld.normalized, _wallNormal) > 0.35f;

            bool climbing = IsWallClimbing
                ? wallClimbPossible && !pressingAway
                : wallClimbPossible && pressingIntoWall;

            // 爬到墙顶：给一小段上跳越过顶边，交给 CharacterController 的 stepOffset 平稳落上墙顶
            if (climbing && !_wantsJump && _wallSurface != null &&
                _wallSurface.TopY - transform.position.y <= 0.40f)
            {
                climbing = false;
                _wallClimbCooldown = 0.45f;
                float remaining = Mathf.Max(0.05f, _wallSurface.TopY - transform.position.y);
                float g = config != null ? config.gravity : 24f;
                _verticalVelocity = Mathf.Sqrt(2f * g * (remaining + 0.10f));
            }

            IsWallClimbing = climbing;
            if (IsWallClimbing)
            {
                // 贴在己方墨墙上即视为处于己方墨水环境（HUD 与输入仲裁保持一致）
                FootInkTeam = Team;
            }

            bool canSwimNow = wantsSwimForm && (FootInkTeam == Team || IsWallClimbing);
            if (canSwimNow != IsSwimming)
            {
                IsSwimming = canSwimNow;
                if (IsSwimming && VfxPool.Instance != null && InkSurfaceController.Instance != null)
                {
                    Color c = InkSurfaceController.Instance.GetTeamColor(Team);
                    VfxPool.Instance.SpawnInkSplat(transform.position, c, 0.55f, 8);
                }
            }

            // 4. 计算移速、回墨与敌墨伤害（数值对齐 Inkwave PLAYER：跑 6.0 / 潜 11.8 / 敌墨 1.9 m/s）
            float normalSpd = config != null ? config.normalMoveSpeed : 6.0f;
            float fireSpd = config != null ? config.firingMoveSpeed : 3.8f;
            float enemySpd = config != null ? config.enemyInkMoveSpeed : 1.9f;
            float swimSpd = config != null ? config.swimMoveSpeed : 11.8f;

            // 敌墨「受击槽」：踩敌墨瞬间锁满；离开敌墨后按速率快速恢复（约 0.5s 摆脱惩罚状态）。
            // 注意：减速与 DOT 只在真正踩在敌墨上时生效，恢复期绝不拖住玩家。
            if (IsInEnemyInk)
            {
                _enemyInkGrip01 = 1f;
            }
            else
            {
                _enemyInkGrip01 = Mathf.Max(0f, _enemyInkGrip01 - _enemyInkRecoveryRatePerSec * dt);
            }

            if (_isLockedBySpecial)
            {
                CurrentMoveSpeed = 0f;
            }
            else if (IsWallClimbing)
            {
                // 攀墙：取爬墙速度；不回墨、不回血，避免挂在墙上无限补给
                CurrentMoveSpeed = config != null ? config.wallClimbSpeed : 4.5f;
            }
            else if (IsSwimming)
            {
                CurrentMoveSpeed = swimSpd;
                // 回墨 42 点/秒（空罐约 2.4s 回满）、回血 60 点/秒（对齐 Inkwave inkRefillSwim / regenRateSwim）
                float refillRate = config != null ? config.swimInkRefillRate : 42f;
                float regenRate = config != null ? config.swimHealthRegenRate : 60f;

                if (_currentInk < MaxInk)
                {
                    _currentInk = Mathf.Min(MaxInk, _currentInk + refillRate * dt);
                    if (Team == Team.TeamA && AudioManager.Instance != null)
                    {
                        AudioManager.Instance.PlaySwimRefillTick();
                    }
                }
                _health.Heal(regenRate * dt);

                // 高速潜墨：水面产生细微水纹涟漪（ shader 驱动，不隐藏时的可见移动信号）
                if (!IsWallClimbing && CurrentMoveSpeed * _moveInputWorld.magnitude >= (config != null ? config.swimRippleMinSpeed : 6.5f)
                    && InkSurfaceController.Instance != null)
                {
                    _swimRippleTimer -= dt;
                    if (_swimRippleTimer <= 0f)
                    {
                        _swimRippleTimer = config != null ? config.swimRippleInterval : 0.16f;
                        InkSurfaceController.Instance.TriggerRipple(
                            transform.position + Vector3.up * 0.02f, 0.03f, 0.30f, 3.4f, 0.85f);
                    }
                }
                else
                {
                    _swimRippleTimer = 0f;
                }
            }
            else
            {
                // 人形状态
                float delayAfterShot = config != null ? config.inkRefillDelayAfterShot : 0.25f;
                if (!_isFiringOrThrowing && _timeSinceLastInkSpend >= delayAfterShot)
                {
                    float passiveRate = config != null ? config.passiveInkRefillRate : 4f;
                    _currentInk = Mathf.Min(MaxInk, _currentInk + passiveRate * dt);
                }

                if (IsInEnemyInk)
                {
                    // 踩敌墨：移速骤降 1.9 m/s + DOT 20 点/秒；60 HP 保护底线保证单靠踩墨绝不致死
                    CurrentMoveSpeed = enemySpd;
                    float burnRate = config != null ? config.enemyInkDamagePerSec : 20f;
                    float minFloor = config != null ? config.enemyInkMinHealthFloor : 60f;
                    _health.ApplyEnemyInkBurn(burnRate * dt, minFloor);

                    _enemyInkSplatCooldown -= dt;
                    if (_enemyInkSplatCooldown <= 0f && _moveInputWorld.sqrMagnitude > 0.05f && VfxPool.Instance != null && InkSurfaceController.Instance != null)
                    {
                        _enemyInkSplatCooldown = 0.22f;
                        VfxPool.Instance.SpawnInkSplat(transform.position, InkSurfaceController.Instance.GetTeamColor(FootInkTeam), 0.35f, 4);
                    }
                }
                else if (_isFiringOrThrowing)
                {
                    CurrentMoveSpeed = fireSpd;
                }
                else
                {
                    CurrentMoveSpeed = normalSpd;
                }
            }

            // 4.5 超速惯性滑行（INKWAVE swim-exit glide，config.js L60：swimDecel=42，注释 "1.6 m glide to a stop"）：
            // 形态切换/离开敌墨瞬间当前速度可能远高于新目标（潜墨 11.8 → 人形 6.0），不瞬间贴回目标，
            // 而是按 swimExitGlideDecel 自然衰减 —— 出水后保留约 1.5m 滑行动量。
            // 加速方向瞬时对齐（起步/正常手感不变），只有超速方向走衰减曲线。
            if (_glideSpeed > CurrentMoveSpeed)
            {
                float glideDecel = config != null ? config.swimExitGlideDecel : 42f;
                _glideSpeed = Mathf.Max(CurrentMoveSpeed, _glideSpeed - Mathf.Max(1f, glideDecel) * dt);
            }
            else
            {
                _glideSpeed = CurrentMoveSpeed;
            }
            EffectiveMoveSpeed = _glideSpeed;

            // 5. 执行位移与跳跃/重力物理（支持斜坡贴合与空中控制；攀墙时走墙面专用位移）
            if (_cc != null && _cc.enabled && !_isLockedBySpecial)
            {
                if (IsWallClimbing)
                {
                    MoveAlongWall(dt);
                }
                else
                {
                    float gravity = config != null ? config.gravity : 24.0f;
                    float jumpHeight = config != null ? config.jumpHeight : 1.35f;

                    if (isGrounded)
                    {
                        if (_verticalVelocity < 0f)
                        {
                            _verticalVelocity = -2.5f; // 轻微向下吸附，平滑贴合地面与斜坡
                        }

                        if (_wantsJump)
                        {
                            // 触发跳跃：v0 = sqrt(2 * g * h)
                            _verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
                            _wantsJump = false;

                            if (VfxPool.Instance != null && InkSurfaceController.Instance != null)
                            {
                                Color c = InkSurfaceController.Instance.GetTeamColor(Team);
                                VfxPool.Instance.SpawnInkSplat(transform.position, c, 0.45f, 6);
                            }
                        }
                    }
                    else
                    {
                        _verticalVelocity -= gravity * dt;
                        if (_verticalVelocity < -28f)
                        {
                            _verticalVelocity = -28f;
                        }
                    }

                    float airControl = isGrounded ? 1.0f : (config != null ? config.airControl : 0.95f);
                    // 位移用 EffectiveMoveSpeed（含超速滑行惯性），而非直接贴目标值的 CurrentMoveSpeed
                    Vector3 horizontalVel = _moveInputWorld * (EffectiveMoveSpeed * airControl);
                    Vector3 step = horizontalVel * dt + Vector3.up * (_verticalVelocity * dt);
                    _cc.Move(step);
                }
            }
            else
            {
                _wantsJump = false;
            }

            Vector3 desiredForward;
            if (IsWallClimbing && _wallNormal.sqrMagnitude > 0.001f)
            {
                // 面朝墙面，视觉上像贴在墙上
                desiredForward = -_wallNormal;
            }
            else if (IsSwimming && _moveInputWorld.sqrMagnitude > 0.01f)
            {
                desiredForward = _moveInputWorld.normalized;
            }
            else
            {
                desiredForward = _aimDirectionWorld;
            }

            if (desiredForward.sqrMagnitude > 0.001f)
            {
                float rotSpeed = config != null ? config.rotationLerpSpeed : 22f;
                Quaternion targetRot = Quaternion.LookRotation(desiredForward, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Mathf.Clamp01(rotSpeed * dt));
            }

            // 5.5 高速潜墨急转弯甩墨（INKWAVE fxHooks.js L354-360：游速 >4.5 且 |角速度| >3.2 rad/s 触发，
            // 向转弯外侧甩出一蓬墨滴，读得出 "carving 过弯" 的速度感；冷却防连喷）
            float yawNow = transform.eulerAngles.y;
            float yawRateAbs = Mathf.Abs(Mathf.DeltaAngle(_prevYaw, yawNow)) / Mathf.Max(0.0001f, dt);
            _prevYaw = yawNow;
            if (_swimCarveCooldown > 0f) _swimCarveCooldown -= dt;
            float carveMinSpeed = config != null ? config.swimCarveMinSpeed : 4.5f;
            float carveTurnRate = config != null ? config.swimCarveTurnRate : 3.2f;
            if (IsSwimming && !IsWallClimbing && _moveInputWorld.sqrMagnitude > 0.04f &&
                EffectiveMoveSpeed >= carveMinSpeed && yawRateAbs >= carveTurnRate &&
                _swimCarveCooldown <= 0f && VfxPool.Instance != null && InkSurfaceController.Instance != null)
            {
                _swimCarveCooldown = config != null ? config.swimCarveCooldown : 0.22f;
                VfxPool.Instance.SpawnInkSplat(
                    transform.position,
                    InkSurfaceController.Instance.GetTeamColor(Team),
                    0.45f, 6);
            }

            // 6. 潜行与人形视觉过渡（压扁贴地 vs 标准胶囊）
            UpdateSwimVisuals(dt);

            // 7. 潜墨隐匿（Stealth）：己方墨水中静止/极慢移动时对敌方隐形，己方半透明
            UpdateStealthState();
        }

        /// <summary>
        /// 每帧评估隐匿状态：处于潜行形态、非攀墙、脚下是己方墨水，且水平速度低于阈值
        /// （静止或极慢蠕动）时进入隐匿；任何移动/离墨/出水都会立刻解除。
        /// </summary>
        private void UpdateStealthState()
        {
            float horizSpeed = CurrentMoveSpeed * _moveInputWorld.magnitude;
            float threshold = config != null ? config.stealthSpeedThreshold : 1.5f;

            bool stealthed = IsSwimming
                             && !IsWallClimbing
                             && !_isLockedBySpecial
                             && FootInkTeam == Team
                             && horizSpeed <= threshold;

            if (stealthed != IsStealthed)
            {
                IsStealthed = stealthed;
                OnStealthChanged?.Invoke(this, stealthed);
            }

            ApplyStealthVisuals();
        }

        /// <summary>
        /// 每帧维护「身前墙面」接触信息：
        /// - 未攀爬时用胸部水平球投射寻找可爬立面；
        /// - 攀爬中改为对已吸附墙面的平面做连续校验（身体高过墙顶后射线会打空，不能再用射线维持攀爬）。
        /// </summary>
        private void UpdateWallContact()
        {
            if (!AllowWallClimb)
            {
                _wallSurface = null;
                _wallInkTeam = Team.Neutral;
                _wallNormal = Vector3.zero;
                return;
            }

            if (IsWallClimbing && _wallSurface != null && _wallNormal.sqrMagnitude > 0.001f)
            {
                Vector3 lowProbe = transform.position + Vector3.up * 0.30f;
                float planeOffset = Vector3.Dot(lowProbe - _wallPlanePoint, _wallNormal);
                if (Mathf.Abs(planeOffset) > 0.95f)
                {
                    // 被推开或离开墙面
                    _wallSurface = null;
                    _wallInkTeam = Team.Neutral;
                    _wallNormal = Vector3.zero;
                    return;
                }

                Vector3 projected = lowProbe - _wallNormal * planeOffset;
                _wallInkTeam = _wallSurface.InkAtWorld(projected, _wallNormal);
                return;
            }

            _wallSurface = null;
            _wallInkTeam = Team.Neutral;
            _wallNormal = Vector3.zero;

            Vector3 dir = _moveInputWorld.sqrMagnitude > 0.04f ? _moveInputWorld.normalized : transform.forward;
            Vector3 origin = transform.position + Vector3.up * 0.85f;
            const float probeRadius = 0.28f;
            const float probeDistance = 0.45f;

            int count = Physics.SphereCastNonAlloc(origin, probeRadius, dir, _wallProbeBuffer,
                probeDistance, ~0, QueryTriggerInteraction.Ignore);

            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _wallProbeBuffer[i];
                if (h.collider == null) continue;
                if (h.collider.transform.IsChildOf(transform)) continue;
                if (Mathf.Abs(h.normal.y) > 0.5f) continue; // 只认竖直立面
                if (h.distance >= bestDist) continue;

                InkWallSurface wall = InkWallSurface.FindForCollider(h.collider);
                if (wall == null || !wall.CanClimb) continue;

                bestDist = h.distance;
                _wallSurface = wall;
                _wallNormal = h.normal.normalized;
                _wallPlanePoint = h.point;
            }

            if (_wallSurface != null)
            {
                _wallInkTeam = _wallSurface.InkAtWorld(_wallPlanePoint, _wallNormal);
            }
        }

        /// <summary>
        /// 沿墙面潜行位移：推向墙面则上爬，只做横向输入则缓慢下滑，松开则脱离（由上层状态机处理）。
        /// </summary>
        private void MoveAlongWall(float dt)
        {
            float climbSpeed = config != null ? config.wallClimbSpeed : 4.5f;
            float stickSpeed = config != null ? config.wallStickSpeed : 1.3f;

            if (_wantsJump)
            {
                // 攀爬中跳跃 = 蹬墙跃出
                _wantsJump = false;
                IsWallClimbing = false;
                _wallClimbCooldown = 0.25f;
                float g = config != null ? config.gravity : 24f;
                float jumpH = config != null ? config.jumpHeight : 1.35f;
                _verticalVelocity = Mathf.Sqrt(2f * g * jumpH);
                _cc.Move(Vector3.up * (_verticalVelocity * dt) - _wallNormal * (2.2f * dt));
                return;
            }

            Vector3 tangent = Vector3.Cross(Vector3.up, _wallNormal);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                tangent = transform.right;
            }
            tangent.Normalize();

            float into = Mathf.Clamp01(Vector3.Dot(_moveInputWorld, -_wallNormal));
            float lateral = Vector3.Dot(_moveInputWorld, tangent);
            float vertical = into > 0.15f ? into * climbSpeed : -0.8f;

            Vector3 step = -_wallNormal * (stickSpeed * dt)
                         + tangent * (lateral * climbSpeed * 0.8f * dt)
                         + Vector3.up * (vertical * dt);

            _verticalVelocity = 0f;
            _cc.Move(step);
        }

        private void UpdateSwimVisuals(float dt)
        {
            if (visualRoot != null)
            {
                if (visualController != null)
                {
                    // CharacterVisualController 负责程序化 Kid ⇄ Squid 形态弹性变形与切换，保持根节点为单位缩放
                    visualRoot.localScale = Vector3.one;
                }
                else
                {
                    Vector3 targetScale;
                    if (IsWallClimbing)
                    {
                        // 贴在墙面上：根节点朝向墙面，因此压扁局部 Z（前后）而不是 Y
                        targetScale = new Vector3(0.85f, 0.90f, 0.28f);
                    }
                    else if (IsSwimming)
                    {
                        targetScale = new Vector3(0.85f, 0.16f, 0.95f);
                    }
                    else
                    {
                        targetScale = Vector3.one;
                    }

                    visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, targetScale, Mathf.Clamp01(dt * 18f));
                }
            }

            if (visualController == null && weaponVisual != null && weaponVisual.activeSelf == IsSwimming)
            {
                weaponVisual.SetActive(!IsSwimming);
            }
        }

        // ==================== 潜墨隐匿（Stealth）视觉系统 ====================

        private void EnsureStealthCaches()
        {
            if (_stealthCachesBuilt) return;

            Transform root = visualRoot != null ? visualRoot : transform;
            _bodyRenderers = root.GetComponentsInChildren<Renderer>(true);
            _bodyOriginalMats = new Material[_bodyRenderers.Length];
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                _bodyOriginalMats[i] = _bodyRenderers[i] != null ? _bodyRenderers[i].sharedMaterial : null;
            }

            _stealthCachesBuilt = true;
        }

        /// <summary>
        /// 依据隐匿状态与视角归属，把角色切换为 正常 / 己方半透明(幽灵) / 完全隐形 三种视觉之一。
        /// 仅在状态变化帧写入材质与开关渲染器，逐帧调用近乎零开销。
        /// </summary>
        private void ApplyStealthVisuals()
        {
            EnsureStealthCaches();

            int kind = 0;
            if (IsStealthed)
            {
                // 敌方视角完全隐形；己方（含自身）视角半透明。无本地玩家时视为己方视角。
                var local = PlayerController.LocalPlayer != null ? PlayerController.LocalPlayer.Health : null;
                Team viewerTeam = local != null ? local.Team : Team;
                kind = viewerTeam == Team ? 1 : 2;
            }

            if (kind == _appliedVisualKind) return;
            _appliedVisualKind = kind;

            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                Renderer r = _bodyRenderers[i];
                if (r == null) continue;

                switch (kind)
                {
                    case 0: // 正常显示：还原原始共享材质
                        if (_bodyOriginalMats[i] != null && r.sharedMaterial != _bodyOriginalMats[i])
                        {
                            r.sharedMaterial = _bodyOriginalMats[i];
                        }
                        r.enabled = true;
                        break;

                    case 1: // 己方半透明
                        r.sharedMaterial = GetGhostMaterial(i);
                        r.enabled = true;
                        break;

                    case 2: // 对敌方完全隐形
                        r.enabled = false;
                        break;
                }
            }
        }

        /// <summary>
        /// 取（或创建）第 i 个身体渲染器的半透明材质实例：复制原材质、打开 URP 透明表面，
        /// 保留原 RGB、把 alpha 设为己方隐匿透明度。
        /// </summary>
        private Material GetGhostMaterial(int index)
        {
            Renderer r = _bodyRenderers[index];
            if (_ghostMatCache.TryGetValue(r, out var cached) && cached != null)
            {
                return cached;
            }

            Material original = _bodyOriginalMats[index];
            if (original == null) return r.sharedMaterial;

            float alpha = config != null ? config.stealthAllyAlpha : 0.35f;
            var ghost = Instantiate(original);
            ghost.name = original.name + "_StealthGhost";
            MakeTransparent(ghost);

            Color baseCol = Color.white;
            if (ghost.HasProperty(BaseColorProp)) baseCol = ghost.GetColor(BaseColorProp);
            else if (ghost.HasProperty(ColorProp)) baseCol = ghost.GetColor(ColorProp);
            baseCol.a = alpha;
            ghost.SetColor(BaseColorProp, baseCol);
            ghost.SetColor(ColorProp, baseCol);

            _ghostMatCache[r] = ghost;
            return ghost;
        }

        /// <summary>把 URP 材质切换为透明表面混合模式（Lit/Unlit 均适用；缺属性时静默跳过）。</summary>
        private static void MakeTransparent(Material m)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
    }
}
