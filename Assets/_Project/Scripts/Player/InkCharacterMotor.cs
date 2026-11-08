using System;
using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 玩家与 Bot 共用的角色移动、墨水储罐、脚下墨色检测与潜行（乌贼）形态控制器。
    /// 乌贼形态随时可进（对齐 Inkwave）：没涂墨的地面保持「干地乌贼」慢速蹦行（squidDryMoveSpeed），
    /// 但潜墨加成（高速 / 回墨 / 回血 / 隐匿）仍只在脚下己方墨水（或己方墨墙）中生效。
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
        private bool _aimFacingRequested;
        private bool _isLockedBySpecial;

        // P35 朝向解耦（对齐 INKWAVE actor.js _face）：人形在开火/投弹及之后一小段时间内面向瞄准方向，
        // 其余移动时间面向移动方向 —— 相机固定在角色身后，若角色永远盯准星就永远只能看到后脑勺。
        // 站立且未开火时保持当前朝向（与 INKWAVE 的 target=null 分支一致）。
        private float _aimFacingTimer;

        // P29 移动手感：水平速度向量（S 曲线加减速 + 转向限速的积分状态）与跳跃宽容计时
        private Vector3 _planarVelocity;
        private bool _prevWantsJump;
        private float _jumpBufferTimer;
        private float _coyoteTimer;

        // 双枪战术翻滚：Motor 侧自计时（策略计时器在 LateUpdate 递减，这里用上升沿对齐），驱动快出缓停速度曲线
        private bool _wasRollingDodge;
        private float _dodgeElapsed;

        // 敌墨「受击槽」：踩敌墨时锁定 1，离开敌墨后按速率快速恢复（惩罚状态的可读化退出）
        private float _enemyInkGrip01;
        private float _enemyInkRecoveryRatePerSec = 2.0f;

        // 高速潜墨涟漪触发计时
        private float _swimRippleTimer;

        // A6 潜墨游动尾迹：自包含粒子组件（初始化时挂载，仅在潜墨且实际速度达标时驱动）
        private CharacterSwimWake _swimWake;

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
        // 脚下探地（判断「是否真的贴地」，决定能否采样脚下墨水）
        private readonly RaycastHit[] _groundProbeBuffer = new RaycastHit[4];
        private InkWallSurface _wallSurface;
        private Team _wallInkTeam = Team.Neutral;
        private Vector3 _wallNormal = Vector3.zero;
        private Vector3 _wallPlanePoint;
        private float _wallClimbCooldown;
        private float _nextWallProbeTime;
        private P1.Weapons.WeaponController _weapon;

        public PlayerConfigSO Config => config;
        public Team Team => _health != null ? _health.Team : Team.TeamA;
        public float CurrentInk => _currentInk;
        public float MaxInk => config != null ? config.maxInk : 100f;
        public float Ink01 => Mathf.Clamp01(_currentInk / MaxInk);

        public Team FootInkTeam { get; private set; }
        public CharacterVisualController VisualController => visualController;
        public bool IsSwimming { get; private set; }

        /// <summary>
        /// 乌贼形态本体：按住潜行键且未开火 / 未锁大招即成立，与脚下墨色无关（对齐 Inkwave
        /// actor.js：form = 'squid' 不看地面）。无墨地面为「干地乌贼」（squidDryMoveSpeed 慢速蹦行）；
        /// 潜墨加成状态（高速 / 回墨 / 回血 / 隐匿）仍由 <see cref="IsSwimming"/> 单独判定。
        /// </summary>
        public bool IsInSquidForm { get; private set; }
        public bool IsGrounded => _cc != null && _cc.isGrounded;
        public bool IsInEnemyInk => FootInkTeam == Team.GetOpponent();
        public bool IsInOwnInk => FootInkTeam == Team;
        public float CurrentMoveSpeed { get; private set; }

        /// <summary>
        /// 上一帧实际发生的水平位移速度 (m/s)：由 <c>CharacterController.Move</c> 前后位移测得。
        /// 顶墙/被掩体卡住不动时为 0，与 <see cref="PlanarVelocity"/>（积分速度状态）语义不同；
        /// 潜墨隐匿的「移动与否」判定必须用这个值。
        /// </summary>
        public float ActualPlanarSpeed { get; private set; }

        /// <summary>
        /// 当前水平速度向量（P29 惯性状态，m/s）：由 S 曲线加减速/转向限速积分得到，
        /// 供相机速度感、调试与测试读取（与 EffectiveMoveSpeed 的"目标速度上限"语义区分）。
        /// </summary>
        public Vector3 PlanarVelocity => _planarVelocity;

        /// <summary>当前垂直速度 (m/s)：向上为正，供调试/测试读取。</summary>
        public float VerticalVelocity => _verticalVelocity;

        /// <summary>
        /// 实际生效的平面移速 (m/s)：超速惯性滑行（swim-exit glide）期间高于
        /// <see cref="CurrentMoveSpeed"/> 并按 swimExitGlideDecel 衰减回目标值。
        /// 相机 FOV 速度感与测试断言读取此值。
        /// </summary>
        public float EffectiveMoveSpeed { get; private set; }
        public Vector3 AimDirection => _aimDirectionWorld;

        /// <summary>
        /// 当前帧是否要求面向准星（主武器开火姿态 / 外部瞄准请求，如副武器按住瞄准）。
        /// 实际朝向还受潜墨形态与爬墙优先级影响，仅供调试与测试读取。
        /// </summary>
        public bool IsAimFacingRequested =>
            _isFiringOrThrowing || _aimFacingRequested || (_weapon != null && _weapon.WantsAimFacing);

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

        /// <summary>当前绑定的武器控制器（若存在）。</summary>
        public P1.Weapons.WeaponController Weapon => _weapon;

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
            _weapon = GetComponent<P1.Weapons.WeaponController>();
            _currentInk = MaxInk;

            // P29 贴地：运行时收紧 skinWidth（场景里烘焙的 0.08 相对 1.12m 身高明显偏大，
            // 接触解算残留间隙会读作"整体悬浮"）。不修改场景 YAML，运行时写入即可。
            // P30：控制器静止时根节点高度恰好等于 skinWidth（实测 0.02 → 悬空 1.6cm），
            // 因此收紧到 0.006；视觉层再按同量下移（由 CharacterVisualController 处理，
            // 因为 WeaponController 的大招升空会直接写 visualRoot.localPosition，不能放在这一层）。
            if (_cc != null)
            {
                _cc.skinWidth = config != null
                    ? Mathf.Clamp(config.controllerSkinWidth, 0.004f, 0.04f)
                    : 0.006f;
            }

            _enemyInkRecoveryRatePerSec = config != null ? config.enemyInkRecoveryRatePerSec : 2.0f;

            _health.OnRespawned += HandleRespawned;
            _health.OnKilled += HandleKilled;

            EnsureSwimWake();

            _prevYaw = transform.eulerAngles.y;
            EffectiveMoveSpeed = config != null ? config.normalMoveSpeed : 6.0f;
            _glideSpeed = EffectiveMoveSpeed;
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.OnRespawned -= HandleRespawned;
                _health.OnKilled -= HandleKilled;
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
            EnsureSwimWake();
        }

        private void HandleRespawned(CombatantHealth h)
        {
            _currentInk = MaxInk;
            IsSwimming = false;
            IsInSquidForm = false;
            IsWallClimbing = false;
            _wallSurface = null;
            _wallInkTeam = Team.Neutral;
            _wallNormal = Vector3.zero;
            _wallClimbCooldown = 0f;
            _wantsToSwim = false;
            _wantsJump = false;
            _prevWantsJump = false;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            _verticalVelocity = -2.0f;
            _planarVelocity = Vector3.zero;
            ActualPlanarSpeed = 0f;
            _isFiringOrThrowing = false;
            _aimFacingRequested = false;
            _aimFacingTimer = 0f;
            _isLockedBySpecial = false;

            // 复活清除隐匿 / 敌墨惩罚 / 涟漪与视觉残留（CombatantHealth 已重新启用其收集的渲染器，
            // 这里把幽灵材质实例一并复位）
            IsStealthed = false;
            _enemyInkGrip01 = 0f;
            _swimRippleTimer = 0f;
            if (_swimWake != null) _swimWake.ResetWake();
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
        /// 阵亡瞬间清空潜墨尾迹（A6）：死亡后 Update 不再驱动发射，这里把残留粒子一并收掉，
        /// 避免尸体位置挂着一团还在飘的队色墨雾。
        /// </summary>
        private void HandleKilled(CombatantHealth h, Team killerTeam)
        {
            if (_swimWake != null) _swimWake.ResetWake();
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
        /// <param name="aimFacingRequested">
        /// 外部瞄准姿态请求（副武器手雷按住瞄准等「不属于主武器策略状态」的准星锁定）：
        /// 为 true 时本帧身体朝准星，并对齐 INKWAVE weapons.js 的 <c>aimingSub → fireFacing = 0.3</c>
        /// 刷新松手保持计时。默认 false，旧调用方语义不变。
        /// </param>
        public void SetFrameIntent(
            Vector3 worldMoveDir,
            Vector3 worldAimDir,
            bool wantsSwim,
            bool wantsJump,
            bool isFiring,
            bool lockedBySpecial = false,
            bool aimFacingRequested = false)
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
            _aimFacingRequested = aimFacingRequested;
            if (isFiring || aimFacingRequested)
            {
                // 开火瞬间 / 瞄准中重置保持计时：松手后仍面向准星 aimFacingHoldSeconds 秒
                _aimFacingTimer = config != null ? Mathf.Max(0f, config.aimFacingHoldSeconds) : 0.4f;
            }
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

        /// <summary>
        /// 一次性奖励回墨（击杀奖励等）：在当前墨量上叠加并钳制到罐上限。
        /// 与 <see cref="RefillInkFull"/>（大招释放回满）分开：奖励只回一小截，是「继续交火」的补给
        /// 而不是「重整旗鼓」的重置，因此也不重置开枪后的回墨延迟计时。
        /// </summary>
        public void RefillInk(float amount)
        {
            if (amount <= 0f) return;
            _currentInk = Mathf.Min(MaxInk, _currentInk + amount);
        }

        /// <summary>
        /// 瞬移至指定位置（越界回收与防坠落使用）。
        /// 临时禁用 CharacterController 以确保位置立即生效，并可选择重置垂直与水平滑行动量。
        /// </summary>
        public void TeleportTo(Vector3 targetPosition, bool clearVelocity = true)
        {
            TeleportTo(targetPosition, transform.rotation, clearVelocity);
        }

        /// <summary>
        /// 瞬移至指定位置与朝向。
        /// </summary>
        public void TeleportTo(Vector3 targetPosition, Quaternion targetRotation, bool clearVelocity = true)
        {
            if (_cc != null && _cc.enabled)
            {
                _cc.enabled = false;
                transform.SetPositionAndRotation(targetPosition, targetRotation);
                _cc.enabled = true;
            }
            else
            {
                transform.SetPositionAndRotation(targetPosition, targetRotation);
            }

            if (clearVelocity)
            {
                _verticalVelocity = -2.0f;
                _glideSpeed = 0f;
                EffectiveMoveSpeed = 0f;
                _planarVelocity = Vector3.zero;
                ActualPlanarSpeed = 0f;
                _wantsJump = false;
                _prevWantsJump = false;
                _jumpBufferTimer = 0f;
                _coyoteTimer = 0f;
            }

            IsWallClimbing = false;
            _wallSurface = null;
            _wallClimbCooldown = 0f;
            if (_swimWake != null) _swimWake.ResetWake();
        }

        /// <summary>
        /// 施加外部位移推力（P16-A 出生禁入圈径向外推使用）。
        /// 走 CharacterController.Move 碰撞检测路径，仅在水平面生效；不瞬移、不清速度、不致死。
        /// </summary>
        public void ApplyExternalPush(Vector3 delta)
        {
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.000001f) return;

            if (_cc != null && _cc.enabled)
            {
                _cc.Move(delta);
            }
            else
            {
                transform.position += delta;
            }
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

            // 1. 墙面探测：身前是否有可潜行攀爬的立面，以及该处墨水归属
            UpdateWallContact();

            // 2. 形态仲裁：乌贼形态随时可进（对齐 Inkwave，无墨地面为干地乌贼慢速蹦行）；
            //    潜墨（IsSwimming）仍要求脚下是己方墨水，己方墨墙可攀爬潜行
            bool wantsSwimForm = _wantsToSwim && !_isFiringOrThrowing && !_isLockedBySpecial;
            if (wantsSwimForm != IsInSquidForm)
            {
                IsInSquidForm = wantsSwimForm;
            }
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

            // 3. 脚下墨水归属：只有「确认贴地」才采样脚下墨
            //    （旧实现只排除上升段 —— !isGrounded && _verticalVelocity > 0.5 —— 于是下落/跳顶附近
            //     仍在采样，导致跳过敌墨照样掉血减速、乌贼形态空中经过己墨还会回墨/回血/隐匿）。
            //    贴地判定：CharacterController.isGrounded，或向下短探地命中坡面/台阶（下坡与台阶宽容）。
            float groundDistance = ProbeGroundDistance(isGrounded);
            bool sampleGroundInk = GroundInkSampleMath.ShouldSampleGroundInk(
                isGrounded, _verticalVelocity, groundDistance, IsWallClimbing);
            FootInkTeam = sampleGroundInk && InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.InkAt(transform.position)
                : Team.Neutral;

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

            // 4. 计算移速、回墨与敌墨伤害（跑 6.0 / 潜 11.8 / 敌墨 2.4 m/s：Splatoon 约 40% 跑速；
            //    INKWAVE 原值 1.9 在 4v4 实战里体感过重，按反馈上调，加速度曲线仍对齐其 enemyInkAccel/Decel）
            float normalSpd = config != null ? config.normalMoveSpeed : 6.0f;
            float fireSpd = config != null ? config.firingMoveSpeed : 3.8f;
            float enemySpd = config != null ? config.enemyInkMoveSpeed : 2.4f;
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

            bool isRollingDodge = _weapon != null && _weapon.IsRollingDodge;
            float weaponSpeedMult = _weapon != null ? _weapon.MoveSpeedMultiplier : 1f;

            if (isRollingDodge)
            {
                // 双枪战术翻滚（INKWAVE weapons.js dodgeVel）：快出缓停速度曲线 ——
                // 起步 = burst × 距离/时长的爆发，随 (1-u²) 二次方减速到 0，曲线积分恰为一个翻滚距离；
                // 旧实现是匀速滑行，观感是「平移冲刺」而不是翻滚。
                // 策略计时器在 WeaponController.LateUpdate 才递减，这里用上升沿自起计时保持对齐。
                if (!_wasRollingDodge)
                {
                    _dodgeElapsed = 0f;
                }
                _dodgeElapsed += dt;

                float rollSpd = 9.33f;
                if (_weapon.Config != null)
                {
                    float dur = Mathf.Max(0.05f, _weapon.Config.dualiesRollDuration);
                    float dist = Mathf.Max(0.1f, _weapon.Config.dualiesRollDistance);
                    float burst = Mathf.Clamp(_weapon.Config.dualiesRollBurstSpeedMultiplier, 1f, 2.5f);
                    float u = Mathf.Clamp01(_dodgeElapsed / dur);
                    rollSpd = burst * (dist / dur) * (1f - u * u);
                }
                // 同步锁定 _glideSpeed：超速滑行的 42/s 衰减追不上曲线前段的陡降，会把翻滚距离拖长
                _glideSpeed = rollSpd;
                CurrentMoveSpeed = rollSpd;
                Vector3 dodgeDir = _weapon.DodgeDirection;
                if (dodgeDir.sqrMagnitude > 0.001f)
                {
                    _moveInputWorld = dodgeDir.normalized;
                }
                _wantsJump = false;
            }
            else if (_isLockedBySpecial)
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
                // 回墨 34 点/秒（空罐约 2.9s 回满）、回血 60 点/秒（回血对齐 Inkwave regenRateSwim，回墨按 4v4 手感收紧）
                float refillRate = config != null ? config.swimInkRefillRate : 34f;
                float regenRate = config != null ? config.swimHealthRegenRate : 60f;

                if (_currentInk < MaxInk)
                {
                    _currentInk = Mathf.Min(MaxInk, _currentInk + refillRate * dt);
                    // 回墨「咕嘟」只给本地玩家：旧判定 TeamA 让 AI 队友的回墨音
                    // 和自己的潜墨音混成一片，反而听不出自己的墨有没有回上。
                    if (AudioManager.Instance != null && CombatantHealth.IsLocalPlayer(_health))
                    {
                        if (_currentInk >= MaxInk)
                        {
                            // 「未满 → 满」边沿：refill_full 烘出来了但一直没接线
                            AudioManager.Instance.PlayRefillFull();
                        }
                        else
                        {
                            AudioManager.Instance.PlaySwimRefillTick();
                        }
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
            else if (IsInSquidForm)
            {
                // 干地乌贼（INKWAVE squidDrySpeed 2.9，actor.js L389）：无墨/敌墨地面也能保持乌贼形态，
                // 但没有潜墨加成 —— 不加速、不回血、不隐匿；回墨只有人形被动速率的一半（actor.js L317）。
                float squidDrySpd = config != null ? config.squidDryMoveSpeed : 2.9f;
                // 空中保留动量（actor.js L376：air target = max(squidDrySpeed, 当前速度)），
                // 跳跃/滑行弧线不因形态切换被压回干地速度
                float dryTarget = isGrounded ? squidDrySpd : Mathf.Max(squidDrySpd, _planarVelocity.magnitude);
                CurrentMoveSpeed = IsInEnemyInk ? Mathf.Min(dryTarget, enemySpd) : dryTarget;

                if (IsInEnemyInk)
                {
                    // 踩敌墨的减速与 DOT 不分形态（INKWAVE onEnemy 对乌贼同样生效，敌墨中不可能 submerged）
                    ApplyEnemyInkPenalty(dt);
                }
                else
                {
                    float dryRefill = (config != null ? config.passiveInkRefillRate : 4f) * 0.5f;
                    _currentInk = Mathf.Min(MaxInk, _currentInk + dryRefill * dt);
                }
            }
            else
            {
                // 人形状态
                float delayAfterShot = config != null ? config.inkRefillDelayAfterShot : 0.5f;
                if (!_isFiringOrThrowing && _timeSinceLastInkSpend >= delayAfterShot)
                {
                    float passiveRate = config != null ? config.passiveInkRefillRate : 4f;
                    _currentInk = Mathf.Min(MaxInk, _currentInk + passiveRate * dt);
                }

                if (IsInEnemyInk)
                {
                    // 踩敌墨：移速降至 2.4 m/s（约 40% 跑速）+ DOT 20 点/秒；60 HP 保护底线保证单靠踩墨绝不致死
                    CurrentMoveSpeed = enemySpd;
                    ApplyEnemyInkPenalty(dt);
                }
                else if (_isFiringOrThrowing)
                {
                    // INKWAVE 逐武器开火移速（weapons.js moveSpeed）：武器给出绝对覆盖值时直接采用
                    //（小枪 4.6 / 双枪 5.0 / 爆破 4.0 / 泼桶 4.2 / 滚筒鼓速 4.4 / 加特林 4.5→2.4→3.4 / 蓄力狙 4.2→1.8），
                    // 未配置（0）时保留旧全局开火移速 × 倍率，老武器行为不变。
                    float weaponFiringSpd = _weapon != null ? _weapon.FiringMoveSpeedOverride : 0f;
                    CurrentMoveSpeed = (weaponFiringSpd > 0f ? weaponFiringSpd : fireSpd) * weaponSpeedMult;
                }
                else
                {
                    CurrentMoveSpeed = normalSpd * (weaponSpeedMult < 1f ? weaponSpeedMult : 1f);
                }
            }

            _wasRollingDodge = isRollingDodge;

            // 4.5 超速惯性滑行（INKWAVE swim-exit glide，config.js L60：swimDecel=42，注释 "1.6 m glide to a stop"）：
            // 形态切换/离开敌墨瞬间当前速度可能远高于新目标（潜墨 11.8 → 人形 6.0），不瞬间贴回目标，
            // 而是按 swimExitGlideDecel 自然衰减 —— 出水后保留约 1.5m 滑行动量。
            // 踩敌墨走 INKWAVE 的 enemyInkDecel=30（比 42 更缓）：踩入敌墨保留短暂前冲，不做硬钳制。
            // 加速方向瞬时对齐（起步/正常手感不变），只有超速方向走衰减曲线。
            if (_glideSpeed > CurrentMoveSpeed)
            {
                float glideDecel = config != null
                    ? (IsInEnemyInk ? config.enemyInkDecel : config.swimExitGlideDecel)
                    : (IsInEnemyInk ? 30f : 42f);
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
                    // 攀墙位移由 MoveAlongWall 接管，清空水平动量避免脱离墙面时继承旧速度
                    _planarVelocity = Vector3.zero;
                    _jumpBufferTimer = 0f;
                    _prevWantsJump = _wantsJump;
                    MoveAlongWall(dt);
                }
                else
                {
                    float gravity = config != null ? config.gravity : 24.0f;
                    float jumpHeight = config != null ? config.jumpHeight : 1.35f;

                    // ---- P29 跳跃宽容三件套：跳跃缓冲 + 土狼时间 + 下落加重/顶点滞空 ----
                    float jumpBufferTime = config != null ? config.jumpBufferTime : 0.12f;
                    float coyoteTime = config != null ? config.coyoteTime : 0.12f;
                    float groundSnap = config != null ? config.groundSnapSpeed : 6f;
                    float fallGravityMul = config != null ? config.fallGravityMultiplier : 1.2f;
                    float apexGravityMul = config != null ? config.apexGravityMultiplier : 0.85f;
                    float apexBand = config != null ? config.apexVelocityBand : 1.6f;

                    // 跳跃缓冲：只在按下沿记入，0.12s 内落地即触发（落地前一帧按跳不丢输入）
                    if (_wantsJump && !_prevWantsJump)
                    {
                        _jumpBufferTimer = Mathf.Max(0f, jumpBufferTime);
                    }
                    _prevWantsJump = _wantsJump;
                    if (_jumpBufferTimer > 0f)
                    {
                        _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - dt);
                    }

                    if (isGrounded)
                    {
                        _coyoteTimer = coyoteTime;
                        if (_verticalVelocity < 0f)
                        {
                            // P29 贴地吸附加强：旧 -2.5 在下 0.24m 台阶/潜墨下 12.8° 坡时约 40% 时间腾空
                            _verticalVelocity = -Mathf.Max(2.5f, groundSnap);
                        }
                    }
                    else
                    {
                        // 土狼时间：走出边缘后 0.12s 内仍可起跳
                        if (_coyoteTimer > 0f)
                        {
                            _coyoteTimer = Mathf.Max(0f, _coyoteTimer - dt);
                        }

                        // 重力曲线：下落更沉（×1.2）、顶点微滞空（×0.85），参考 Inkwave config.js
                        float g = gravity;
                        if (_verticalVelocity < 0f) g *= Mathf.Max(1f, fallGravityMul);
                        if (Mathf.Abs(_verticalVelocity) < apexBand) g *= Mathf.Clamp(apexGravityMul, 0.5f, 1f);
                        _verticalVelocity -= g * dt;
                        if (_verticalVelocity < -28f)
                        {
                            _verticalVelocity = -28f;
                        }
                    }

                    bool canJump = isGrounded || _coyoteTimer > 0f;
                    if (canJump && _jumpBufferTimer > 0f)
                    {
                        // 触发跳跃：v0 = sqrt(2 * g * h)
                        _verticalVelocity = Mathf.Sqrt(2f * gravity * jumpHeight);
                        _jumpBufferTimer = 0f;
                        _coyoteTimer = 0f;
                        _wantsJump = false;
                        _prevWantsJump = false;

                        if (VfxPool.Instance != null && InkSurfaceController.Instance != null)
                        {
                            Color c = InkSurfaceController.Instance.GetTeamColor(Team);
                            VfxPool.Instance.SpawnInkSplat(transform.position, c, 0.45f, 6);
                        }
                        if (AudioManager.Instance != null)
                        {
                            // 起跳音按位置衰减：旧版对 8 名角色都按全局 2D 满音量播，
                            // Bot 起跳会把自己的起跳声盖掉。
                            AudioManager.Instance.PlayJump(transform.position);
                        }
                    }

                    // ---- P29 水平速度：S 曲线加减速 + 转向限速（不再一帧满速/一帧急停） ----
                    if (isRollingDodge)
                    {
                        // 翻滚期间速度曲线完全接管（INKWAVE dodgeVel 的「weapon runner owns horizontal velocity」）：
                        // 加速限幅会吃掉起步爆发、衰减限幅会拖长减速尾，翻滚距离就不再是配置值
                        _planarVelocity = _moveInputWorld * EffectiveMoveSpeed;
                    }
                    else
                    {
                        float airControl = isGrounded ? 1.0f : (config != null ? config.airControl : 0.95f);
                        // 目标速度用 EffectiveMoveSpeed（含超速滑行惯性），而非直接贴目标值的 CurrentMoveSpeed
                        Vector3 desiredVel = _moveInputWorld * (EffectiveMoveSpeed * airControl);
                        _planarVelocity = ComputePlanarVelocity(_planarVelocity, desiredVel, isGrounded, config, dt, IsInEnemyInk);
                    }

                    Vector3 step = _planarVelocity * dt + Vector3.up * (_verticalVelocity * dt);
                    Vector3 beforeMove = transform.position;
                    _cc.Move(step);
                    MeasureActualPlanarSpeed(beforeMove, dt);
                }
            }
            else
            {
                _wantsJump = false;
                _prevWantsJump = false;
                _jumpBufferTimer = 0f;
                _planarVelocity = Vector3.zero;
                ActualPlanarSpeed = 0f;
            }

            // P35 朝向解耦（对齐 INKWAVE actor.js _face，优先级与引擎一致）：
            //   爬墙 → 面朝墙；乌贼 → 面向移动方向；人形开火/投弹（含松手保持窗口）→ 面向准星；
            //   人形其余移动 → 面向移动方向（横移/后退能看到侧面与正脸，不再永远后脑勺）；
            //   站立且未开火 → 保持当前朝向（upper body 动画继续表达待机）。
            if (_aimFacingTimer > 0f)
                _aimFacingTimer = Mathf.Max(0f, _aimFacingTimer - dt);

            // 武器处于开火姿态（INKWAVE weapons.js firingPose：蓄力/加特林扫射/滚筒推地与挥击前摇/
            // 泼桶前摇/双枪翻滚）：枪口即弹道方向，扳机松开也要保持朝准星。
            // 旧实现只在「按住扳机」的帧刷新保持计时，加特林松手扫射 0.4s 后身体转回移动方向
            // → 枪口朝后、子弹朝前。双枪翻滚后的架枪强化不在其列：它已不锁移速，
            // 再钉住朝向会让「翻滚脱身」变成原地转身；开枪时另有 _isFiringOrThrowing 兜住朝向。
            bool weaponAimFacing = !IsInSquidForm && _weapon != null && _weapon.WantsAimFacing;

            // 外部瞄准姿态请求（副武器手雷按住瞄准：预览弹道按准星解算）。
            bool requestedAimFacing = !IsInSquidForm && _aimFacingRequested;

            if (weaponAimFacing || requestedAimFacing)
                _aimFacingTimer = config != null ? Mathf.Max(0f, config.aimFacingHoldSeconds) : 0.4f;

            bool aimFacing = !IsInSquidForm &&
                (_isFiringOrThrowing || _aimFacingTimer > 0f || weaponAimFacing || requestedAimFacing);

            Vector3 desiredForward;
            if (IsWallClimbing && _wallNormal.sqrMagnitude > 0.001f)
            {
                // 面朝墙面，视觉上像贴在墙上
                desiredForward = -_wallNormal;
            }
            else if (IsInSquidForm && _moveInputWorld.sqrMagnitude > 0.01f)
            {
                // 乌贼形态面向移动方向（INKWAVE squidFaceOmega 语义），干地与潜墨一致
                desiredForward = _moveInputWorld.normalized;
            }
            else if (aimFacing)
            {
                desiredForward = _aimDirectionWorld;
            }
            else if (_moveInputWorld.sqrMagnitude > 0.01f)
            {
                desiredForward = _moveInputWorld.normalized;
            }
            else if (_planarVelocity.sqrMagnitude > 0.36f)
            {
                // 松手滑行：无输入但仍有惯性速度时面向滑行方向（INKWAVE `hs > 0.6 → vel` 分支）
                desiredForward = new Vector3(_planarVelocity.x, 0f, _planarVelocity.z).normalized;
            }
            else
            {
                desiredForward = Vector3.zero;
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

            // 6.5 潜墨游动尾迹（A6）：在位移与朝向都结算完之后驱动，纯表现、不参与移动解算
            UpdateSwimWake(dt);

            // 7. 潜墨隐匿（Stealth）：己方墨水中静止/极慢移动时对敌方隐形，己方半透明
            UpdateStealthState();
        }

        /// <summary>
        /// 踩敌墨惩罚（人形与干地乌贼共用）：DOT 掉血 + 移动时脚下溅墨反馈。
        /// 只含伤害与表现，不含移速（减速由各形态分支自行钳制）。
        /// </summary>
        private void ApplyEnemyInkPenalty(float dt)
        {
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

        /// <summary>
        /// 水平速度积分（P29，参考 Inkwave actor.js 的速度大小/方向分离模型）：
        /// - 目标速度为 0 → 按刹车减速度滑停（低速段保留制动力，避免硬停）；
        /// - 输入反向 → 按 reverseDecel 踩地急刹再反向；
        /// - 速度方向以 turnRate 朝输入方向旋转（转弯画弧不掉速）；
        /// - 加速/减速用统一 MoveTowards 积分，起步与停步都有过渡，不再一帧满速/一帧静止。
        /// 纯函数、零分配；无配置时回退到默认手感参数。
        /// </summary>
        private static Vector3 ComputePlanarVelocity(
            Vector3 current, Vector3 desired, bool grounded, PlayerConfigSO cfg, float dt, bool inEnemyInk = false)
        {
            float runAccel = cfg != null ? cfg.runAccel : 60f;
            float runDecel = cfg != null ? cfg.runDecel : 48f;
            float reverseDecel = cfg != null ? cfg.reverseDecel : 70f;
            float airAccel = cfg != null ? cfg.airAccel : 20f;
            float airDecel = cfg != null ? cfg.airDecel : 6f;
            float turnRateDeg = cfg != null ? cfg.turnRateDegPerSec : 860f;

            // 敌墨独立手感（对齐 INKWAVE actor.js：A = min(A, enemyInkAccel)、D = enemyInkDecel）：
            // 起步更沉、超速衰减更缓，读出「陷入」而不是一帧钉死；反向输入仍保留正常急刹，方便掉头逃出。
            if (inEnemyInk && grounded)
            {
                runAccel = Mathf.Min(runAccel, cfg != null ? cfg.enemyInkAccel : 30f);
                runDecel = cfg != null ? cfg.enemyInkDecel : 30f;
            }

            float curSpeed = current.magnitude;
            float targetSpeed = desired.magnitude;

            float accel;
            if (targetSpeed < 0.05f)
            {
                // 无输入：刹车滑停（空中阻力更小，保留跳跃惯性）
                accel = grounded ? runDecel : airDecel;
            }
            else if (curSpeed > 0.05f && Vector3.Dot(current, desired) < 0f)
            {
                // 反向输入：先踩地制动（穿过零速后再反向加速）
                accel = grounded ? reverseDecel : airAccel;
            }
            else if (targetSpeed < curSpeed)
            {
                // 目标减速（松开部分摇杆 / 出水 / 踩敌墨）：按刹车减速度收
                accel = grounded ? runDecel : airDecel;
            }
            else
            {
                accel = grounded ? runAccel : airAccel;
            }

            Vector3 newVel = Vector3.MoveTowards(current, desired, Mathf.Max(0.1f, accel) * dt);

            // 转向限速：低速起步（<0.3m/s）允许全向，速度起来后方向变化受 turnRate 约束（画弧）
            if (curSpeed > 0.3f && newVel.sqrMagnitude > 0.0001f)
            {
                Vector3 oldDir = current / curSpeed;
                Vector3 newDir = newVel.normalized;
                float maxTurnDeg = Mathf.Max(0.1f, turnRateDeg) * dt;
                if (Vector3.Angle(oldDir, newDir) > maxTurnDeg)
                {
                    Vector3 cappedDir = Vector3.RotateTowards(oldDir, newDir, maxTurnDeg * Mathf.Deg2Rad, 0f);
                    newVel = cappedDir * newVel.magnitude;
                }
            }

            return newVel;
        }

        /// <summary>
        /// 每帧评估隐匿状态：处于潜行形态、非攀墙、脚下是己方墨水，且实际水平位移速度低于阈值
        /// （静止或极慢蠕动）时进入隐匿；任何移动/离墨/出水都会立刻解除。
        /// 2026-11-01 修复：旧实现用 <c>CurrentMoveSpeed × 输入幅度</c>（速度上限 × 摇杆量），
        /// 顶着墙不动（输入满、位移 0）会被算成全速而无法隐匿，改读实际位移速度
        /// <see cref="ActualPlanarSpeed"/>（顶墙被卡住 = 0）。
        /// </summary>
        private void UpdateStealthState()
        {
            float planarSpeed = ActualPlanarSpeed;
            float threshold = config != null ? config.stealthSpeedThreshold : 1.5f;

            bool stealthed = IsSwimming
                             && !IsWallClimbing
                             && !_isLockedBySpecial
                             && FootInkTeam == Team
                             && planarSpeed <= threshold;

            if (stealthed != IsStealthed)
            {
                IsStealthed = stealthed;
                OnStealthChanged?.Invoke(this, stealthed);
            }

            ApplyStealthVisuals();
        }

        // ==================== 潜墨游动尾迹（Swim Wake, A6） ====================

        /// <summary>
        /// 初始化潜墨尾迹组件（只挂载一次；无配置或配置关闭时直接跳过，不产生任何粒子系统）。
        /// 由 Awake / Configure 调用，运行时若配置被重新赋值也会在此重新绑定。
        /// </summary>
        private void EnsureSwimWake()
        {
            if (config == null || !config.wakeEnabled) return;

            if (_swimWake == null)
            {
                _swimWake = GetComponent<CharacterSwimWake>();
                if (_swimWake == null)
                {
                    _swimWake = gameObject.AddComponent<CharacterSwimWake>();
                }
            }

            _swimWake.Bind(config);
        }

        /// <summary>
        /// 潜墨尾迹驱动（纯表现，读取现有状态，不参与移动/潜行/回血语义）：
        /// 只有「己方墨里高速潜行、且未攀墙」时发射；速度阈值与强度全部走 <see cref="PlayerConfigSO"/>。
        /// 速度用 <see cref="ActualPlanarSpeed"/>（顶墙卡住时为 0，不会原地喷水花），
        /// 方向用 <see cref="PlanarVelocity"/>（实际移动方向，含惯性滑行）。
        /// </summary>
        private void UpdateSwimWake(float dt)
        {
            if (_swimWake == null)
            {
                if (config != null && config.wakeEnabled) EnsureSwimWake();
                if (_swimWake == null) return;
            }

            float minSpeed = config != null ? Mathf.Max(0f, config.wakeMinSpeed) : 3.5f;
            bool emitting = IsSwimming && !IsWallClimbing && ActualPlanarSpeed >= minSpeed;

            float refSpeed = config != null ? Mathf.Max(minSpeed + 0.5f, config.swimMoveSpeed) : 11.8f;
            float speed01 = emitting
                ? Mathf.Clamp01((ActualPlanarSpeed - minSpeed) / Mathf.Max(0.5f, refSpeed - minSpeed))
                : 0f;

            Color wakeColor = Color.white;
            if (emitting && InkSurfaceController.Instance != null)
            {
                wakeColor = InkSurfaceController.Instance.GetTeamColor(Team);
            }

            _swimWake.Tick(dt, emitting, _planarVelocity, speed01, wakeColor);
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

            // 非攀爬态：墙面探测按 30Hz 缓存。每帧 SphereCast 会把 79 面墙与全部角色
            // 丢进 broadphase；缓存后墙面接触最多滞后 1 帧，攀爬入口的体感不变。
            float now = Time.unscaledTime;
            if (now < _nextWallProbeTime)
                return;
            _nextWallProbeTime = now + 1f / 30f;

            _wallSurface = null;
            _wallInkTeam = Team.Neutral;
            _wallNormal = Vector3.zero;

            Vector3 dir = _moveInputWorld.sqrMagnitude > 0.04f ? _moveInputWorld.normalized : transform.forward;
            Vector3 origin = transform.position + Vector3.up * 0.85f;
            const float probeRadius = 0.28f;
            const float probeDistance = 0.45f;

            int count = Physics.SphereCastNonAlloc(origin, probeRadius, dir, _wallProbeBuffer,
                probeDistance, GameLayers.WorldMask, QueryTriggerInteraction.Ignore);

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
                Vector3 beforeKick = transform.position;
                _cc.Move(Vector3.up * (_verticalVelocity * dt) - _wallNormal * (2.2f * dt));
                MeasureActualPlanarSpeed(beforeKick, dt);
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
            Vector3 beforeClimb = transform.position;
            _cc.Move(step);
            MeasureActualPlanarSpeed(beforeClimb, dt);
        }

        /// <summary>
        /// 记录本帧实际发生的水平位移速度（米/秒）：由 CharacterController.Move 前后位置差测得，
        /// 顶墙 / 被掩体卡住时自然为 0。隐匿判定必须用它，而不是"速度上限 × 输入幅度"
        /// （后者顶着墙站着不动也算全速移动，轻推摇杆也会被算成满速比例）。
        /// </summary>
        private void MeasureActualPlanarSpeed(Vector3 beforeMovePosition, float dt)
        {
            if (dt <= 0.0001f)
            {
                ActualPlanarSpeed = 0f;
                return;
            }

            Vector3 delta = transform.position - beforeMovePosition;
            ActualPlanarSpeed = new Vector3(delta.x, 0f, delta.z).magnitude / dt;
        }

        /// <summary>
        /// 向下短探地，返回脚底到最近可站立地面的间隙（米）；找不到返回 -1。
        /// 只认水平面/坡面（normal.y ≥ <see cref="GroundInkSampleMath.MinWalkableNormalY"/>），
        /// 忽略自身碰撞体与触发器，用于「走坡 / 下台阶时 isGrounded 抖一帧」的宽容贴地判定。
        /// </summary>
        private float ProbeGroundDistance(bool isGrounded)
        {
            if (isGrounded) return 0f;
            if (_cc == null) return -1f;

            Vector3 origin = transform.position + Vector3.up * GroundInkSampleMath.GroundProbeOriginLift;
            int count = Physics.RaycastNonAlloc(
                origin,
                Vector3.down,
                _groundProbeBuffer,
                GroundInkSampleMath.GroundProbeMaxDistance,
                GameLayers.WorldMask,
                QueryTriggerInteraction.Ignore);

            float best = -1f;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _groundProbeBuffer[i];
                if (hit.collider == null) continue;
                if (hit.collider.transform.IsChildOf(transform)) continue;
                if (hit.normal.y < GroundInkSampleMath.MinWalkableNormalY) continue;
                if (best >= 0f && hit.distance >= best) continue;
                best = hit.distance;
            }

            if (best < 0f) return -1f;
            return Mathf.Max(0f, best - GroundInkSampleMath.GroundProbeOriginLift);
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
                    else if (IsInSquidForm)
                    {
                        // 干地乌贼：趴地但不下沉（脚下没有墨面可以潜入）
                        targetScale = new Vector3(0.85f, 0.45f, 0.95f);
                    }
                    else
                    {
                        targetScale = Vector3.one;
                    }

                    visualRoot.localScale = Vector3.Lerp(visualRoot.localScale, targetScale, Mathf.Clamp01(dt * 18f));
                }
            }

            if (visualController == null && weaponVisual != null && weaponVisual.activeSelf == IsInSquidForm)
            {
                weaponVisual.SetActive(!IsInSquidForm);
            }
        }

        // ==================== 潜墨隐匿（Stealth）视觉系统 ====================

        private void EnsureStealthCaches()
        {
            if (_stealthCachesBuilt) return;

            Transform root = visualRoot != null ? visualRoot : transform;
            Renderer[] all = root.GetComponentsInChildren<Renderer>(true);

            // 角色刚体部件合批后，源 renderer 处于 disabled（由 MergedStatic_* 接管渲染）。
            // 若把它们也收进隐匿缓存，正常态分支会把源部件整套 re-enable → 与合并网格双份渲染。
            // 合并产物存在时只收启用中的 renderer（合并网格 enabled=true 自然被收进来）。
            bool hasMergedArtifacts = false;
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].gameObject.name.StartsWith("MergedStatic_"))
                {
                    hasMergedArtifacts = true;
                    break;
                }
            }

            if (hasMergedArtifacts)
            {
                var enabledOnly = new List<Renderer>(all.Length);
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].enabled)
                        enabledOnly.Add(all[i]);
                }
                _bodyRenderers = enabledOnly.ToArray();
            }
            else
            {
                _bodyRenderers = all;
            }

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

    /// <summary>
    /// 「脚下墨水是否可采样」的纯静态判定（2026-11-01 修复）：
    /// 旧实现只把上升段（<c>!isGrounded &amp;&amp; verticalVelocity &gt; 0.5</c>）排除在采样外，
    /// 下落段与跳顶附近仍会读脚下墨 → 跳过敌墨照样掉血/减速，乌贼形态空中经过己墨
    /// 还会被判定 <c>IsSwimming</c>（空中回墨、回血、隐匿）。
    /// 现在只有确认贴地才采样：CharacterController 报贴地，或向下短探地确实命中坡面/台阶；
    /// 攀墙时脚下不是地面，墨水归属交给墙面逻辑（InkCharacterMotor 会写为 Team）。
    /// 纯函数、零分配，便于 EditMode 独立测试。
    /// </summary>
    public static class GroundInkSampleMath
    {
        /// <summary>贴地宽容距离（米）：向下探地命中该间隙以内即视为贴地（下坡 / 台阶 / 斜坡）。</summary>
        public const float MaxGroundSampleDistance = 0.18f;

        /// <summary>上升速度超过该值即视为起跳/腾空，不采样脚下墨。</summary>
        public const float MaxUpwardVelocityForSample = 0.5f;

        /// <summary>探地射线起点相对脚底的抬高（米）：避免从胶囊底半球内部起射线。</summary>
        public const float GroundProbeOriginLift = 0.05f;

        /// <summary>探地射线总长（米）：抬高 + 宽容距离 + 少许余量。</summary>
        public const float GroundProbeMaxDistance = GroundProbeOriginLift + MaxGroundSampleDistance + 0.02f;

        /// <summary>可站立地面的法线 Y 下限（低于此值视为墙面，不算脚下地面）。</summary>
        public const float MinWalkableNormalY = 0.5f;

        /// <param name="isGrounded">CharacterController.isGrounded。</param>
        /// <param name="verticalVelocity">当前垂直速度（向上为正）。</param>
        /// <param name="groundDistance">探地间隙（米，脚底到地面），未命中传负数。</param>
        /// <param name="isWallClimbing">是否正在己方墨墙攀爬。</param>
        public static bool ShouldSampleGroundInk(
            bool isGrounded,
            float verticalVelocity,
            float groundDistance,
            bool isWallClimbing)
        {
            // 攀墙：脚下不是地面，墨水归属由墙面接管
            if (isWallClimbing) return false;

            // 确认贴地
            if (isGrounded) return true;

            // 起跳 / 跳顶：腾空期间绝不采样脚下墨
            if (verticalVelocity > MaxUpwardVelocityForSample) return false;

            // 其余（下落、贴地前一帧）：只有短探地确实命中坡面/台阶才算贴地
            return groundDistance >= 0f && groundDistance <= MaxGroundSampleDistance;
        }
    }
}
