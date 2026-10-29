using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 武器总控：整合主武器（策略化：连发小枪 / 推地滚筒 / 蓄力狙）、
    /// 副武器（抛物线延时墨水手雷）与大招（超级墨水砸地冲击波 Splashdown）。
    /// 原有 public 签名全部保留；主武器行为由 <see cref="MainWeaponStrategy"/> 子类驱动。
    /// </summary>
    [RequireComponent(typeof(InkCharacterMotor))]
    [RequireComponent(typeof(CombatantHealth))]
    public class WeaponController : MonoBehaviour, IWeaponRuntimeHost
    {
        [SerializeField] private WeaponConfigSO config;
        [SerializeField] private Transform muzzleTransform;
        [SerializeField] private Transform visualBodyRoot;

        [Header("多武器配置（可选）")]
        [Tooltip("留空则始终使用上面的 config；填入后 Awake 会装备 equippedIndex 指向的武器，可运行时 EquipWeaponAt 切换")]
        [SerializeField] private WeaponConfigSO[] loadout;
        [SerializeField] private int equippedIndex;

        private InkCharacterMotor _motor;
        private CombatantHealth _health;
        private PlayerController _playerController;

        private MainWeaponStrategy _strategy;
        private WeaponAim _lastAim = WeaponAim.FromDirection(Vector3.forward);
        private bool _hasAim;
        private bool _triggerHeldThisFrame;
        private bool _triggerHeldLastFrame;
        private readonly Dictionary<CombatantHealth, float> _rollHitTimes = new Dictionary<CombatantHealth, float>();

        private float _bombCooldown;
        private float _specialPoints;
        private bool _wasSpecialReady;

        // 大招状态：Tidal Slam 升空/滞空/下砸状态机；Ink Tempest 只有短促的投掷前摇
        private bool _isPerformingSpecial;
        private float _specialActionLock;
        private float _specialInvulnGrace;
        private SpecialKind _activeSpecialKind = SpecialKind.Slam;
        private int _slamPhase;              // 0 = 升空, 1 = 滞空, 2 = 下砸
        private float _slamPhaseElapsed;
        private float _slamHeight;
        private float _specialElapsed;
        private Vector3 _specialGroundCenter;
        private Transform _specialWarningRing;
        private MeshRenderer _specialRingRenderer;
        private MaterialPropertyBlock _propBlock;

        // 蓄力狙表现：地面激光指示线 + 释放瞬间的贯穿光柱
        private ChargerLaserSight _laserSight;
        private Transform _beamVisual;
        private MeshRenderer _beamRenderer;
        private Material _beamMaterial;
        private MaterialPropertyBlock _beamBlock;
        private float _beamLife;
        private float _beamBaseWidth;
        private const float BeamVisualDuration = 0.16f;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProp = Shader.PropertyToID("_Color");

        public WeaponConfigSO Config => config;
        public float SpecialPoints => _specialPoints;
        public float SpecialRequired => config != null ? config.specialPointsRequired : 180f;
        public float SpecialCharge01 => Mathf.Clamp01(_specialPoints / Mathf.Max(1f, SpecialRequired));
        public bool IsSpecialReady => _specialPoints >= SpecialRequired && !IsPerformingSpecial;
        public bool IsPerformingSpecial => _isPerformingSpecial || _specialActionLock > 0f;

        /// <summary>最近一次释放/当前武器配置的大招种类（Slam / Storm），HUD 与测试可读取。</summary>
        public SpecialKind ActiveSpecialKind => _activeSpecialKind;

        /// <summary>Tidal Slam 是否处于空中（升空/滞空/下砸），调试可观测。</summary>
        public bool IsSlamAirborne => _isPerformingSpecial && _activeSpecialKind == SpecialKind.Slam;

        /// <summary>当前装备的主武器分类。</summary>
        public WeaponKind CurrentWeaponKind => _strategy != null
            ? _strategy.Kind
            : (config != null ? config.weaponKind : WeaponKind.Shooter);

        /// <summary>当前准星散布半角（度），供准星/HUD 读取；滚筒与蓄力狙为 0。</summary>
        public float CurrentSpreadDegrees => _strategy != null ? _strategy.SpreadDegrees : 0f;

        /// <summary>蓄力狙蓄力进度 0~1；其他武器恒为 0。</summary>
        public float Charge01 => _strategy != null ? _strategy.Charge01 : 0f;

        public bool IsCharging => _strategy != null && _strategy.IsCharging;
        public bool IsRolling => _strategy != null && _strategy.IsRolling;

        /// <summary>双枪是否处于战术翻滚中。</summary>
        public bool IsRollingDodge => _strategy != null && _strategy.IsRollingDodge;

        /// <summary>双枪是否处于翻滚后的站桩架枪状态（移速锁定、射速提升 20%、散布清零）。</summary>
        public bool IsTurretLocked => _strategy != null && _strategy.IsTurretLocked;

        /// <summary>加特林是否正在释放高密度暴风雨弹幕。</summary>
        public bool IsStreaming => _strategy is SplatlingWeaponStrategy s && s.IsStreaming;

        /// <summary>当前武器对角色移动速度的影响倍率（架枪 0，加特林蓄力 0.4 / 扫射 0.57，常规 1.0）。</summary>
        public float MoveSpeedMultiplier => _strategy != null ? _strategy.MoveSpeedMultiplier : 1f;

        /// <summary>双枪当前剩余可用翻滚次数（最多 2 次）。</summary>
        public int RollsRemaining => _strategy != null ? _strategy.RollsRemaining : 0;

        /// <summary>武器换装事件（供视觉表现、HUD 等监听）。</summary>
        public event System.Action<WeaponConfigSO> OnWeaponEquipped;

        /// <summary>主武器开火事件（供枪口后坐力、震颤等视觉/触觉微表现监听）。</summary>
        public event System.Action OnFired;

        /// <summary>更新枪口挂载点（换装时由视觉系统同步）。</summary>
        public void SetMuzzleTransform(Transform muzzle)
        {
            if (muzzle != null) muzzleTransform = muzzle;
        }

        /// <summary>尝试执行双枪战术翻滚（双击跳跃或专用键触发）。</summary>
        public bool TryDodgeRoll(Vector3 moveDirection)
        {
            if (_health == null || !_health.IsAlive || IsPerformingSpecial)
                return false;

            if (_strategy == null)
                RebuildStrategy();

            return _strategy.TryPerformDodgeRoll(moveDirection, this);
        }

        /// <summary>当前 loadout 中装备的下标（无 loadout 时为 0）。</summary>
        public int EquippedIndex => equippedIndex;
        public int LoadoutCount => loadout != null ? loadout.Length : 0;

        /// <summary>主武器累计出手次数（弹丸波次与光束各计 1 次），调试/测试可观测。</summary>
        public int ShotsFired { get; private set; }

        private void Awake()
        {
            _motor = GetComponent<InkCharacterMotor>();
            _health = GetComponent<CombatantHealth>();
            _playerController = GetComponent<PlayerController>();
            _propBlock = new MaterialPropertyBlock();
            _beamBlock = new MaterialPropertyBlock();

            if (loadout != null && loadout.Length > 0)
            {
                equippedIndex = Mathf.Clamp(equippedIndex, 0, loadout.Length - 1);
                if (loadout[equippedIndex] != null)
                    config = loadout[equippedIndex];
            }

            RebuildStrategy();
            _health.OnKilled += HandleKilled;
            BuildSpecialWarningRing();
        }

        private void OnDestroy()
        {
            if (_health != null)
            {
                _health.OnKilled -= HandleKilled;
            }
            if (_specialWarningRing != null)
            {
                DestroySafe(_specialWarningRing.gameObject);
            }
            if (_laserSight != null)
            {
                _laserSight.Dispose();
                _laserSight = null;
            }
            if (_beamVisual != null)
            {
                DestroySafe(_beamVisual.gameObject);
            }
            if (_beamMaterial != null)
            {
                DestroySafe(_beamMaterial);
            }
        }

        public void Configure(WeaponConfigSO weaponConfig, Transform muzzle, Transform bodyVisual)
        {
            config = weaponConfig;
            muzzleTransform = muzzle;
            visualBodyRoot = bodyVisual;
            RebuildStrategy();
        }

        /// <summary>运行时换装（P3 武器库/复活点切换复用同一入口）。</summary>
        public void EquipWeapon(WeaponConfigSO weaponConfig)
        {
            if (weaponConfig == null)
                return;

            config = weaponConfig;
            RebuildStrategy();
            OnWeaponEquipped?.Invoke(weaponConfig);
        }

        /// <summary>配置多武器 loadout；startIndex 为初始装备下标。</summary>
        public void ConfigureLoadout(WeaponConfigSO[] weapons, int startIndex = 0)
        {
            loadout = weapons;
            if (weapons == null || weapons.Length == 0)
            {
                equippedIndex = 0;
                RebuildStrategy();
                return;
            }

            equippedIndex = Mathf.Clamp(startIndex, 0, weapons.Length - 1);
            EquipWeapon(weapons[equippedIndex]);
        }

        /// <summary>切换到 loadout 中指定下标的武器。</summary>
        public bool EquipWeaponAt(int index)
        {
            if (loadout == null || index < 0 || index >= loadout.Length || loadout[index] == null)
                return false;

            equippedIndex = index;
            EquipWeapon(loadout[index]);
            return true;
        }

        private void RebuildStrategy()
        {
            _strategy = WeaponStrategyFactory.Create(config);
            _triggerHeldThisFrame = false;
            _triggerHeldLastFrame = false;
            _hasAim = false;
            _lastAim = WeaponAim.FromDirection(transform.forward);
            _rollHitTimes.Clear();

            if (_laserSight != null)
                _laserSight.Hide();
            if (_beamVisual != null)
                _beamVisual.gameObject.SetActive(false);
            _beamLife = 0f;

            OnWeaponEquipped?.Invoke(config);
        }

        public void ResetAllWeaponState()
        {
            _bombCooldown = 0f;
            _specialPoints = 0f;
            _wasSpecialReady = false;
            _triggerHeldThisFrame = false;
            _triggerHeldLastFrame = false;
            _hasAim = false;
            _rollHitTimes.Clear();
            _strategy?.Reset();

            if (_laserSight != null)
                _laserSight.Hide();
            if (_beamVisual != null)
                _beamVisual.gameObject.SetActive(false);
            _beamLife = 0f;

            CancelSpecialImmediate();
        }

        private void HandleKilled(CombatantHealth victim, Team killerTeam)
        {
            CancelSpecialImmediate();
            // 被击杀损失一半当前未满的大招能量
            _specialPoints *= 0.5f;
            _wasSpecialReady = false;
        }

        public void AddSpecialPoints(int paintedCells)
        {
            if (paintedCells <= 0 || IsPerformingSpecial || !_health.IsAlive)
                return;

            float req = SpecialRequired;
            _specialPoints = Mathf.Min(req, _specialPoints + paintedCells);

            if (!_wasSpecialReady && _specialPoints >= req)
            {
                _wasSpecialReady = true;
                if (_health.Team == Team.TeamA && AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayUltReady();
                }
            }
        }

        public bool TryFireMainWeapon(Vector3 aimDirection)
        {
            return TryFireMainWeapon(aimDirection, ResolveAimGroundPoint(aimDirection));
        }

        /// <summary>
        /// 显式指定地面瞄准点的开火重载：弹道会反解仰角，使弹丸落点与 <paramref name="aimGroundPoint"/> 重合。
        /// 旧签名保留并转发到此重载，跨系统调用方无需改动。
        /// </summary>
        public bool TryFireMainWeapon(Vector3 aimDirection, Vector3 aimGroundPoint)
        {
            if (_health == null || !_health.IsAlive || IsPerformingSpecial)
                return false;

            if (_strategy == null)
                RebuildStrategy();

            Vector3 flatAim = new Vector3(aimDirection.x, 0f, aimDirection.z);
            if (flatAim.sqrMagnitude < 0.0001f)
                flatAim = transform.forward;
            flatAim.Normalize();

            _triggerHeldThisFrame = true;
            _lastAim = WeaponAim.FromDirection(flatAim, aimGroundPoint);
            _hasAim = true;

            return _strategy.OnTriggerHeld(_lastAim, Time.deltaTime, this);
        }

        /// <summary>
        /// 解析本次开火应命中的地面点：本地玩家直接复用它已经算好的地面准星点；
        /// Bot / 无玩家控制器时按瞄准方向在最大射程处向下贴地。
        /// </summary>
        private Vector3 ResolveAimGroundPoint(Vector3 aimDirection)
        {
            Vector3 flatAim = new Vector3(aimDirection.x, 0f, aimDirection.z);
            if (flatAim.sqrMagnitude < 0.0001f)
                flatAim = transform.forward;
            flatAim.Normalize();

            if (_playerController != null)
            {
                Vector3 playerAim = _playerController.AimGroundPoint;
                Vector3 delta = playerAim - transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > 0.04f && Vector3.Dot(delta.normalized, flatAim) > 0.35f)
                    return playerAim;
            }

            float maxRange = config != null ? config.maxRange : 7.2f;
            Vector3 point = transform.position + flatAim * maxRange;
            if (Physics.Raycast(point + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 12f, ~0, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }
            return point;
        }

        public bool TryThrowSubBomb(Vector3 targetGroundPos)
        {
            if (!_health.IsAlive || IsPerformingSpecial || _bombCooldown > 0f)
                return false;

            float bombCost = config != null ? config.bombInkCost : 70f;
            if (!_motor.TryConsumeInk(bombCost))
            {
                if (_health.Team == Team.TeamA && AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayInkEmpty();
                }
                return false;
            }

            _bombCooldown = config != null ? config.bombCooldown : 0.6f;

            Vector3 startPos = muzzleTransform != null
                ? muzzleTransform.position
                : transform.position + Vector3.up * 0.8f;

            if (InkBombPool.Instance != null)
            {
                InkBombPool.Instance.ThrowBomb(
                    startPos,
                    targetGroundPos,
                    _health.Team,
                    config,
                    AddSpecialPoints,
                    _health);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayShot();
            }

            return true;
        }

        public bool TryActivateSpecial()
        {
            return TryActivateSpecial(ResolveSpecialTargetPoint());
        }

        /// <summary>
        /// 带地面瞄准点的大招释放（Ink Tempest 发生器落点）。旧无参签名保留并转发，
        /// PlayerController / BotController 无需改动即可同时支持两套大招。
        /// </summary>
        public bool TryActivateSpecial(Vector3 targetGroundPoint)
        {
            if (_health == null || !_health.IsAlive || !IsSpecialReady || IsPerformingSpecial)
                return false;

            _activeSpecialKind = config != null ? config.specialKind : SpecialKind.Slam;
            _specialPoints = 0f;
            _wasSpecialReady = false;
            // 释放大招顺便回满墨水罐（经典爽感设计）
            _motor.RefillInkFull();

            if (AudioManager.Instance != null)
            {
                // 只有玩家自己的大招压 BGM；Bot 大招保留音效但不打断玩家音乐
                AudioManager.Instance.PlayUltCast(_playerController != null);
            }

            return _activeSpecialKind == SpecialKind.Storm
                ? StartStormSpecial(targetGroundPoint)
                : StartSlamSpecial();
        }

        private bool StartSlamSpecial()
        {
            _isPerformingSpecial = true;
            _health.IsInvulnerable = true;

            _specialElapsed = 0f;
            _slamPhase = 0;
            _slamPhaseElapsed = 0f;
            _slamHeight = config != null ? Mathf.Max(0.5f, config.specialJumpHeight) : 4.2f;
            _specialGroundCenter = ResolveGroundPoint(transform.position);

            float radius = config != null ? config.specialSplatRadius : 5.2f;
            ShowSpecialWarningRing(radius * 0.35f);

            return true;
        }

        private bool StartStormSpecial(Vector3 targetGroundPoint)
        {
            _specialActionLock = config != null ? Mathf.Max(0f, config.stormThrowLockDuration) : 0.35f;

            InkStormCloudPool pool = InkStormCloudPool.EnsureInstance();
            if (pool != null)
            {
                pool.ThrowStorm(ResolveMuzzlePosition(), targetGroundPoint, TeamId, config, _health);
            }

            if (TopDownCameraFollow.Instance != null)
            {
                TopDownCameraFollow.Instance.AddTrauma(0.12f);
            }

            return true;
        }

        /// <summary>无瞄准输入时（Bot/触控）的默认大招落点：本地玩家取地面准星点，否则取正前方最大投掷距离处。</summary>
        private Vector3 ResolveSpecialTargetPoint()
        {
            float maxDistance = config != null ? Mathf.Max(1f, config.stormThrowDistance) : 9f;

            Vector3 flatForward = transform.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f)
                flatForward = Vector3.forward;
            flatForward.Normalize();

            Vector3 target = transform.position + flatForward * maxDistance;
            if (_playerController != null)
            {
                Vector3 aim = _playerController.AimGroundPoint;
                Vector3 delta = aim - transform.position;
                delta.y = 0f;
                if (delta.sqrMagnitude > 0.25f)
                {
                    target = transform.position + Vector3.ClampMagnitude(delta, maxDistance);
                }
            }

            return ResolveGroundPoint(target);
        }

        private static Vector3 ResolveGroundPoint(Vector3 position)
        {
            Vector3 origin = new Vector3(position.x, position.y + 6f, position.z);
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 20f, ~0, QueryTriggerInteraction.Ignore))
            {
                return new Vector3(position.x, hit.point.y, position.z);
            }
            return position;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_bombCooldown > 0f) _bombCooldown -= dt;

            if (_specialActionLock > 0f)
            {
                _specialActionLock = Mathf.Max(0f, _specialActionLock - dt);
            }

            // 砸地落地后的短暂无敌余韵（Inkwave: invuln 0.3s）
            if (_specialInvulnGrace > 0f)
            {
                _specialInvulnGrace = Mathf.Max(0f, _specialInvulnGrace - dt);
                if (_specialInvulnGrace <= 0f && _health != null)
                {
                    _health.IsInvulnerable = false;
                }
            }

            if (_isPerformingSpecial)
            {
                UpdateSpecialSlam(dt);
            }
        }

        /// <summary>
        /// 主武器状态机在 LateUpdate 推进：此时玩家/Bot 的 Update 已注入本帧扳机状态，
        /// 蓄力狙能可靠地检测“松开”并发射，滚筒也能拿到最终移动位置。
        /// </summary>
        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            if (_strategy != null)
            {
                _strategy.Tick(dt, _triggerHeldThisFrame, _lastAim, this);
            }

            UpdateChargerLaser();
            UpdateBeamVisual(dt);

            _triggerHeldLastFrame = _triggerHeldThisFrame;
            _triggerHeldThisFrame = false;
        }

        /// <summary>
        /// Tidal Slam 状态机：起跳升空 0.55s → 顶点滞空 0.25s → 极速下砸 0.1s。
        /// 全程无敌，落地瞬间结算 5.2m 冲击波与 5m 巨型墨斑。
        /// </summary>
        private void UpdateSpecialSlam(float dt)
        {
            _specialElapsed += dt;
            _slamPhaseElapsed += dt;

            float rise = config != null ? Mathf.Max(0.05f, config.specialWindupDuration) : 0.55f;
            float hang = config != null ? Mathf.Max(0f, config.specialHangDuration) : 0.25f;
            float fall = config != null ? Mathf.Max(0.03f, config.specialFallDuration) : 0.1f;

            float yOffset = _slamHeight;
            if (_slamPhase == 0)
            {
                float t = Mathf.Clamp01(_slamPhaseElapsed / rise);
                yOffset = Mathf.Sin(t * Mathf.PI * 0.5f) * _slamHeight;
                if (t >= 1f)
                {
                    _slamPhase = 1;
                    _slamPhaseElapsed = 0f;
                }
            }
            else if (_slamPhase == 1)
            {
                // 顶点轻微悬浮抖动，读作“蓄势待发”
                yOffset = _slamHeight + Mathf.Sin(_slamPhaseElapsed * 16f) * 0.05f;
                if (_slamPhaseElapsed >= hang)
                {
                    _slamPhase = 2;
                    _slamPhaseElapsed = 0f;
                }
            }
            else
            {
                float t = Mathf.Clamp01(_slamPhaseElapsed / fall);
                yOffset = Mathf.Lerp(_slamHeight, 0f, t * t);
                if (t >= 1f)
                {
                    ExecuteSpecialSlamImpact();
                    CancelSpecialImmediate(0.15f);
                    return;
                }
            }

            if (visualBodyRoot != null)
            {
                visualBodyRoot.localPosition = new Vector3(0f, yOffset, 0f);
            }

            // 预警圈随升空从 35% 扩到 100%，落地前就是完整的杀伤范围提示
            float radius = config != null ? config.specialSplatRadius : 5.2f;
            float progress = Mathf.Clamp01(_specialElapsed / Mathf.Max(0.05f, rise + hang + fall));
            UpdateSpecialWarningRing(Mathf.Lerp(radius * 0.35f, radius, progress));
        }

        private void ExecuteSpecialSlamImpact()
        {
            float damageRadius = config != null ? config.specialSplatRadius : 5.2f;
            float paintRadius = config != null ? config.specialPaintRadius : 5.0f;
            float innerRadius = config != null ? config.specialInnerKillRadius : 3.2f;
            float innerDamage = config != null ? config.specialInnerDamage : 180f;
            float outerDamage = config != null ? config.specialOuterDamage : 55f;
            float falloffWeight = config != null ? config.specialOuterFalloffWeight : SpecialCombat.DefaultOuterFalloffWeight;
            Team myTeam = TeamId;

            Color teamColor = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(myTeam)
                : Color.yellow;

            // 1. 5m 巨大墨斑 + 9 枚边缘卫星泼墨，打散正圆轮廓
            InkSurfaceController surface = InkSurfaceController.Instance;
            if (surface != null)
            {
                surface.PaintSphereWithWalls(_specialGroundCenter, paintRadius, myTeam);
                for (int i = 0; i < 9; i++)
                {
                    float angle = (i / 9f) * Mathf.PI * 2f + Random.Range(-0.18f, 0.18f);
                    float distance = paintRadius * Random.Range(0.55f, 0.85f);
                    Vector3 point = _specialGroundCenter + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                    surface.PaintCircle(ResolveGroundPoint(point), Random.Range(1.1f, 1.7f), myTeam);
                }
            }

            // 2. 冲击波伤害：核心 3.2m 内 180 秒杀，外圈平滑衰减到 55
            SpecialCombat.ApplySlamDamage(
                _specialGroundCenter, myTeam, damageRadius, innerRadius, innerDamage, outerDamage, falloffWeight, _health);

            // 3. 强力视觉与震屏反馈
            if (VfxPool.Instance != null)
            {
                VfxPool.Instance.SpawnInkImpact(_specialGroundCenter, Vector3.up, teamColor, 1.2f, 24);
                VfxPool.Instance.SpawnInkSplat(_specialGroundCenter, teamColor, 1.35f, 46);
                VfxPool.Instance.SpawnShockwaveRing(_specialGroundCenter, teamColor, 0.6f, damageRadius, 0.32f);
                VfxPool.Instance.SpawnDelayedShockwaveRing(
                    0.1f, _specialGroundCenter, teamColor, damageRadius * 0.45f, damageRadius * 1.15f, 0.5f);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayInkHitGround(2f);
            }

            if (TopDownCameraFollow.Instance != null)
            {
                // P6-A：按与本地玩家的距离精准衰减（核心满额、远处仅轻微），而不是无差别全屏猛震
                TopDownCameraFollow.Instance.AddTraumaFromPoint(
                    _specialGroundCenter,
                    config != null ? config.specialImpactTrauma : 1f,
                    config != null ? Mathf.Max(damageRadius * 2.5f, 8f) : 14f);
            }
        }

        private void ShowSpecialWarningRing(float radius)
        {
            if (_specialWarningRing == null)
                return;

            _specialWarningRing.position = new Vector3(
                _specialGroundCenter.x, _specialGroundCenter.y + 0.06f, _specialGroundCenter.z);
            _specialWarningRing.localScale = new Vector3(radius, 1f, radius);
            _specialWarningRing.gameObject.SetActive(true);

            if (_propBlock == null)
                _propBlock = new MaterialPropertyBlock();

            Color c = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(_health.Team)
                : Color.yellow;
            _propBlock.SetColor(BaseColorProp, c);
            _propBlock.SetColor(ColorProp, c);
            _specialRingRenderer.SetPropertyBlock(_propBlock);
        }

        private void UpdateSpecialWarningRing(float radius)
        {
            if (_specialWarningRing == null || !_specialWarningRing.gameObject.activeSelf)
                return;

            _specialWarningRing.localScale = new Vector3(radius, 1f, radius);
        }

        private void CancelSpecialImmediate(float invulnerabilityGrace = 0f)
        {
            _isPerformingSpecial = false;
            _specialActionLock = 0f;
            _slamPhase = 0;
            _slamPhaseElapsed = 0f;

            if (_health != null)
            {
                _specialInvulnGrace = Mathf.Max(0f, invulnerabilityGrace);
                _health.IsInvulnerable = _specialInvulnGrace > 0f;
            }
            if (visualBodyRoot != null)
            {
                visualBodyRoot.localPosition = Vector3.zero;
            }
            if (_specialWarningRing != null)
            {
                _specialWarningRing.gameObject.SetActive(false);
            }
        }

        private void BuildSpecialWarningRing()
        {
            var go = new GameObject($"{name}_SpecialWarningRing");
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = VfxPool.BuildRingMesh(40, 0.92f, 1.0f);
            _specialRingRenderer = go.AddComponent<MeshRenderer>();

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            _specialRingRenderer.sharedMaterial = new Material(sh);
            _specialRingRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.SetActive(false);

            _specialWarningRing = go.transform;
        }

        // ------------------------------------------------------------------ 主武器表现

        private void UpdateChargerLaser()
        {
            if (_strategy is ChargerWeaponStrategy charger && charger.IsCharging && _hasAim &&
                _health != null && _health.IsAlive)
            {
                EnsureLaserSight();

                Vector3 origin = ResolveMuzzlePosition();
                Vector3 target = _lastAim.HasGroundPoint
                    ? _lastAim.GroundPoint + Vector3.up * 0.45f
                    : origin + _lastAim.FlatDirection * (config != null ? config.chargeRangeMax : 27f);

                Vector3 delta = target - origin;
                float distance = delta.magnitude;
                if (distance > 0.01f &&
                    WeaponBeamSystem.TryFindWorldHit(origin, delta / distance, distance, gameObject, out RaycastHit wallHit))
                {
                    target = wallHit.point;
                }

                Color teamColor = InkSurfaceController.Instance != null
                    ? InkSurfaceController.Instance.GetTeamColor(TeamId)
                    : (TeamId == Team.TeamB ? new Color(0.15f, 0.42f, 1f) : new Color(1f, 0.44f, 0.06f));

                _laserSight.Show(origin, target, charger.Charge01, teamColor);
            }
            else if (_laserSight != null)
            {
                _laserSight.Hide();
            }
        }

        private void PlayBeamVisual(BeamResult result, float intensity)
        {
            EnsureBeamVisual();

            Vector3 delta = result.End - result.Origin;
            float length = delta.magnitude;
            if (length < 0.05f)
                return;

            intensity = Mathf.Clamp01(intensity);
            _beamVisual.position = result.Origin + delta * 0.5f;
            _beamVisual.rotation = Quaternion.LookRotation(delta / length, Vector3.up);

            _beamBaseWidth = 0.11f + 0.13f * intensity;
            _beamVisual.localScale = new Vector3(_beamBaseWidth, _beamBaseWidth, length);
            _beamVisual.gameObject.SetActive(true);

            Color color = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(TeamId)
                : (TeamId == Team.TeamB ? new Color(0.15f, 0.42f, 1f) : new Color(1f, 0.44f, 0.06f));
            color = Color.Lerp(color, Color.white, 0.55f);

            _beamBlock.SetColor(BaseColorProp, color);
            _beamBlock.SetColor(ColorProp, color);
            _beamRenderer.SetPropertyBlock(_beamBlock);

            _beamLife = BeamVisualDuration;
        }

        private void UpdateBeamVisual(float dt)
        {
            if (_beamLife <= 0f || _beamVisual == null)
                return;

            _beamLife -= dt;
            if (_beamLife <= 0f)
            {
                _beamVisual.gameObject.SetActive(false);
                return;
            }

            // 尾段快速收细，读作“光柱瞬间熄灭”而不是长时间定格。
            float t = Mathf.Clamp01(_beamLife / BeamVisualDuration);
            float width = Mathf.Lerp(_beamBaseWidth * 0.25f, _beamBaseWidth, t);
            Vector3 scale = _beamVisual.localScale;
            scale.x = width;
            scale.y = width;
            _beamVisual.localScale = scale;
        }

        private void EnsureLaserSight()
        {
            if (_laserSight == null)
            {
                _laserSight = new ChargerLaserSight(transform, name);
            }
        }

        private void EnsureBeamVisual()
        {
            if (_beamVisual != null)
                return;

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"{name}_ChargerBeam";
            DestroySafe(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);

            _beamRenderer = go.GetComponent<MeshRenderer>();
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _beamMaterial = new Material(shader) { name = "ChargerBeamMat" };
            _beamRenderer.sharedMaterial = _beamMaterial;
            _beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _beamRenderer.receiveShadows = false;
            _beamBlock = new MaterialPropertyBlock();

            go.SetActive(false);
            _beamVisual = go.transform;
        }

        private Vector3 ResolveMuzzlePosition()
        {
            if (muzzleTransform != null)
                return muzzleTransform.position;

            Vector3 flatAim = _lastAim.FlatDirection;
            return transform.position + Vector3.up * 0.75f + flatAim * 0.55f;
        }

        private Team TeamId => _health != null ? _health.Team : Team.TeamA;

        private static void DestroySafe(Object target)
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        // ------------------------------------------------------------------ IWeaponRuntimeHost

        Team IWeaponRuntimeHost.Team => TeamId;

        bool IWeaponRuntimeHost.IsAlive => _health != null && _health.IsAlive && !IsPerformingSpecial;

        bool IWeaponRuntimeHost.IsGrounded => _motor != null && _motor.IsGrounded;

        float IWeaponRuntimeHost.CurrentInk => _motor != null ? _motor.CurrentInk : 0f;

        Vector3 IWeaponRuntimeHost.Position => transform.position;

        Vector3 IWeaponRuntimeHost.Forward => transform.forward;

        Vector3 IWeaponRuntimeHost.MuzzlePosition => ResolveMuzzlePosition();

        WeaponConfigSO IWeaponRuntimeHost.Config => config;

        bool IWeaponRuntimeHost.TriggerHeldLastFrame => _triggerHeldLastFrame;

        bool IWeaponRuntimeHost.ConsumeInk(float amount)
        {
            return _motor != null && _motor.TryConsumeInk(amount);
        }

        void IWeaponRuntimeHost.SpawnShot(WeaponShot shot)
        {
            ShotsFired++;
            if (InkProjectilePool.Instance != null)
            {
                InkProjectilePool.Instance.SpawnProjectile(shot, TeamId, config, gameObject, AddSpecialPoints);
            }
            OnFired?.Invoke();
        }

        void IWeaponRuntimeHost.FireBeam(WeaponBeam beam)
        {
            ShotsFired++;
            BeamResult result = WeaponBeamSystem.Fire(beam, TeamId, gameObject, AddSpecialPoints);
            PlayBeamVisual(result, beam.Intensity01);
            OnFired?.Invoke();
        }

        void IWeaponRuntimeHost.ApplyRoll(RollSweep sweep)
        {
            int painted = WeaponRollSystem.Apply(sweep, TeamId, _rollHitTimes, Time.time, _health);
            if (painted > 0)
            {
                AddSpecialPoints(painted);
            }
            OnFired?.Invoke();
        }

        void IWeaponRuntimeHost.PaintGround(Vector3 worldPos, float radius)
        {
            if (InkSurfaceController.Instance == null)
                return;

            int changed = InkSurfaceController.Instance.PaintCircle(worldPos, radius, TeamId);
            if (changed > 0)
            {
                AddSpecialPoints(changed);
            }
        }

        void IWeaponRuntimeHost.ReportShotFeedback()
        {
            if (TeamId == Team.TeamA && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayShot();
            }
        }

        void IWeaponRuntimeHost.ReportEmptyFeedback()
        {
            if (TeamId == Team.TeamA && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayInkEmpty();
            }
        }
    }
}
