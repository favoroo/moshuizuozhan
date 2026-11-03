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
    [DefaultExecutionOrder(50)]
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
        // 每发子弹/光束都会把它们传给弹丸池与滚涂系统：缓存方法组委托，避免每发两次分配。
        private System.Action<int> _addSpecialPointsDelegate;
        private System.Action<int> _addSpecialPointsFromWallDelegate;
        private System.Action<int> _addSpecialPointsOnlyDelegate;
        private static readonly RaycastHit[] GroundResolveHits = new RaycastHit[16];
        private float _cachedAimGroundY;
        private float _nextAimGroundYSampleTime;
        private PlayerController _playerController;

        private MainWeaponStrategy _strategy;
        private WeaponAim _lastAim = WeaponAim.FromDirection(Vector3.forward);
        private bool _hasAim;
        private bool _triggerHeldThisFrame;
        private bool _triggerHeldLastFrame;

        /// <summary>本帧主武器扳机被主动抑制（暂停 / 终场 / 死亡 / 副武器投掷仲裁占用）。</summary>
        private bool _triggerSuppressedThisFrame;

        /// <summary>抑制期间物理扳机是否仍被按住：恢复后据此判断要不要当成「新按下」。</summary>
        private bool _triggerHeldWhileSuppressed;

        /// <summary>上一帧是否处于抑制段（用于识别抑制段的第一帧）。</summary>
        private bool _triggerSuppressedLastFrame;

        /// <summary>本次抑制段开始时武器是否正被按住（蓄力/扫射/推地/挥击前摇）。</summary>
        private bool _suppressionEngaged;
        private readonly Dictionary<CombatantHealth, float> _rollHitTimes = new Dictionary<CombatantHealth, float>();

        private float _bombCooldown;
        private float _specialPoints;
        private bool _wasSpecialReady;

        /// <summary>InkSurfaceController 未就绪时充能换算的兜底格面积：40×56m 地图、桌面 400 分辨率。</summary>
        private const float FallbackCellArea = 0.014f;

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
        private Mesh _specialRingMesh;
        private Material _specialRingMaterial;
        // P20 预警圈三层：实心危险盘（=伤害半径）+ 内圈秒杀盘 + 外沿亮边
        private MeshRenderer _specialDiscRenderer;
        private MeshRenderer _specialInnerRenderer;
        private Mesh _specialDiscMesh;
        private Mesh _specialInnerMesh;
        private Material _specialDiscMaterial;
        private Material _specialInnerMaterial;
        private MaterialPropertyBlock _propBlock;
        private Collider _selfCollider;

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

        // 枪口防穿墙：小半径重叠检测，命中非本角色碰撞体时从胸口补正射击起点
        private readonly Collider[] _muzzleOverlaps = new Collider[4];

        // 枪口闪光节流：滚筒挥击 / 泼桶一次会生成 8~9 颗弹丸，不能每颗都闪一下
        private const float MuzzleFlashMinInterval = 0.045f;
        private float _lastMuzzleFlashTime = -99f;

        // 镜头冲击独立闸门（P28）：与闪光解耦，同帧齐射只推一次，单发武器每发都推
        private int _lastCameraPunchFrame = -1;

        // 手感参数缓存（P28）：只在换枪/配置变更时 Resolve，避免每颗弹丸重复比较全字段
        private WeaponFeelParams _cachedFeel;
        private bool _feelDirty = true;

        public WeaponConfigSO Config => config;

        /// <summary>当前武器的缓存手感参数（懒刷新）：后坐力/镜头视觉表现只读此值。</summary>
        public WeaponFeelParams CurrentFeel
        {
            get
            {
                if (_feelDirty)
                    RefreshWeaponFeel();
                return _cachedFeel;
            }
        }

        /// <summary>最近一次开火的手感强度 0~1：蓄力狙按蓄力刻度，其余武器为 1。</summary>
        public float LastShotStrength01 { get; private set; } = 1f;
        public float SpecialPoints => _specialPoints;
        public float SpecialRequired => config != null ? config.specialPointsRequired : 180f;
        public float SpecialCharge01 => Mathf.Clamp01(_specialPoints / Mathf.Max(1f, SpecialRequired));
        public bool IsSpecialReady => _specialPoints >= SpecialRequired && !IsPerformingSpecial;
        public bool IsPerformingSpecial => _isPerformingSpecial || _specialActionLock > 0f;

        /// <summary>最近一次释放/当前武器配置的大招种类（Slam / Storm），HUD 与测试可读取。</summary>
        public SpecialKind ActiveSpecialKind => _activeSpecialKind;

        /// <summary>Tidal Slam 是否处于空中（升空/滞空/下砸），调试可观测。</summary>
        public bool IsSlamAirborne => _isPerformingSpecial && _activeSpecialKind == SpecialKind.Slam;

        /// <summary>Slam 起跳时锁定的落点中心（预警圈/伤害/涂地同源），测试与调试可观测。</summary>
        public Vector3 SpecialGroundCenter => _specialGroundCenter;

        /// <summary>当前装备的主武器分类。</summary>
        public WeaponKind CurrentWeaponKind => _strategy != null
            ? _strategy.Kind
            : (config != null ? config.weaponKind : WeaponKind.Shooter);

        /// <summary>当前准星散布半角（度），供准星/HUD 读取；滚筒与蓄力狙为 0。</summary>
        public float CurrentSpreadDegrees => _strategy != null ? _strategy.SpreadDegrees : 0f;

        /// <summary>蓄力狙蓄力进度 0~1；其他武器恒为 0。</summary>
        public float Charge01 => _strategy != null ? _strategy.Charge01 : 0f;

        /// <summary>当前武器有效射程（米）：HUD 准星据此显示超距状态。</summary>
        public float EffectiveRange => _strategy != null
            ? _strategy.EffectiveRange
            : (config != null ? Mathf.Max(0.1f, config.maxRange) : 0f);

        public bool IsCharging => _strategy != null && _strategy.IsCharging;
        public bool IsRolling => _strategy != null && _strategy.IsRolling;

        /// <summary>滚筒挥击前摇中（抡起阶段）：表现层轮询下降沿与墨滴释放同步砸地（P27）。</summary>
        public bool IsFlicking => _strategy != null && _strategy.IsFlicking;

        /// <summary>泼桶是否处于前摇中（表现层据此播放桶身后仰）。</summary>
        public bool IsWindingUp => _strategy != null && _strategy.IsWindingUp;

        /// <summary>泼桶前摇进度 0~1（0 = 刚按下，1 = 即将甩出）。</summary>
        public float Windup01 => _strategy != null ? _strategy.Windup01 : 0f;

        /// <summary>双枪是否处于战术翻滚中。</summary>
        public bool IsRollingDodge => _strategy != null && _strategy.IsRollingDodge;

        /// <summary>双枪是否处于翻滚后的站桩架枪状态（移速锁定、射速提升 20%、散布清零）。</summary>
        public bool IsTurretLocked => _strategy != null && _strategy.IsTurretLocked;

        /// <summary>双枪当前战术翻滚的方向向量。</summary>
        public Vector3 DodgeDirection => _strategy != null ? _strategy.DodgeDirection : transform.forward;

        /// <summary>双枪翻滚方向相对触发瞬间角色右方的分量（-1 左 ~ +1 右）。</summary>
        public float DodgeSide => _strategy != null ? _strategy.DodgeSide : 0f;

        /// <summary>双枪翻滚方向相对触发瞬间角色前方的分量（-1 后 ~ +1 前）。</summary>
        public float DodgeForward => _strategy != null ? _strategy.DodgeForward : 1f;

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

        /// <summary>最近一次蓄力狙光束的结算结果（起点 / 终点 / 命中法线），调试与自动化测试可读。</summary>
        public BeamResult LastBeamResult { get; private set; }

        private void Awake()
        {
            _motor = GetComponent<InkCharacterMotor>();
            _health = GetComponent<CombatantHealth>();
            _playerController = GetComponent<PlayerController>();
            _addSpecialPointsDelegate = AddSpecialPoints;
            _addSpecialPointsFromWallDelegate = AddSpecialPointsFromWall;
            _addSpecialPointsOnlyDelegate = AddSpecialPointsOnly;
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
            _selfCollider = GetComponent<Collider>();
            BuildSpecialWarningRing();
            WeaponAudioDriver.EnsureDriver(this);
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
                _specialWarningRing = null;
            }
            DestroySafe(_specialRingMesh);
            DestroySafe(_specialDiscMesh);
            DestroySafe(_specialInnerMesh);
            _specialRingMesh = null;
            _specialDiscMesh = null;
            _specialInnerMesh = null;
            DestroySafe(_specialRingMaterial);
            DestroySafe(_specialDiscMaterial);
            DestroySafe(_specialInnerMaterial);
            _specialRingMaterial = null;
            _specialDiscMaterial = null;
            _specialInnerMaterial = null;
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
            _lastCameraPunchFrame = -1;
            LastShotStrength01 = 1f;
            // RebuildStrategy 内部已经 invoke 过一次 OnWeaponEquipped，这里不再重复派发
            // （旧实现每次换装触发两次，视觉/音效订阅方会做两遍换装动作）。
            RebuildStrategy();
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
            RefreshWeaponFeel();
            _triggerHeldThisFrame = false;
            _triggerHeldLastFrame = false;
            _triggerSuppressedThisFrame = false;
            _triggerSuppressedLastFrame = false;
            _suppressionEngaged = false;
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

        /// <summary>刷新缓存的手感参数：换枪、配置变更或测试手动调用。</summary>
        public void RefreshWeaponFeel()
        {
            _cachedFeel = WeaponFeelProfile.Resolve(config);
            _feelDirty = false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Inspector 里调手感字段后不必重进 PlayMode，下一次读取 CurrentFeel 时刷新
            _feelDirty = true;
        }
#endif

        public void ResetAllWeaponState()
        {
            _bombCooldown = 0f;
            _specialPoints = 0f;
            _wasSpecialReady = false;
            _triggerHeldThisFrame = false;
            _triggerHeldLastFrame = false;
            _triggerSuppressedThisFrame = false;
            _triggerSuppressedLastFrame = false;
            _suppressionEngaged = false;
            _hasAim = false;
            _rollHitTimes.Clear();
            _lastMuzzleFlashTime = -99f;
            _lastCameraPunchFrame = -1;
            LastShotStrength01 = 1f;
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

        /// <summary>
        /// 涂地充能入口（旧签名保持不变，弹丸/光束/滚筒/手雷的回调无需改动）：
        /// 传入本次新涂/改写的格子数，内部按「格面积 × <see cref="WeaponConfigSO.specialPointsPerSquareMeter"/>」换算成能量点。
        /// 为什么按面积计价：格子总数随网格分辨率变化（桌面 400 / 移动 200），
        /// 旧版「1 格 = 1 点」会让同一把武器跨平台充能速度差 4 倍，且单发墨斑就有数百格导致秒满。
        /// </summary>
        public void AddSpecialPoints(int paintedCells)
        {
            if (paintedCells <= 0)
                return;

            float cellArea = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.CellArea
                : FallbackCellArea;

            if (_health != null)
            {
                MatchStatsService.RegisterTurf(_health, paintedCells * cellArea);
            }

            float pointsPerM2 = config != null ? config.specialPointsPerSquareMeter : 1f;
            AddSpecialPointsRaw(paintedCells * cellArea * pointsPerM2);
        }

        /// <summary>
        /// 仅充能大招点数、不重复登记个人涂地统计（手雷在引爆池中已按地面格独立登记，防双计）。
        /// </summary>
        private void AddSpecialPointsOnly(int paintedCells)
        {
            if (paintedCells <= 0)
                return;

            float cellArea = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.CellArea
                : FallbackCellArea;
            float pointsPerM2 = config != null ? config.specialPointsPerSquareMeter : 1f;
            AddSpecialPointsRaw(paintedCells * cellArea * pointsPerM2);
        }

        /// <summary>
        /// 墙面涂墨充能：按墙面格面积（<see cref="InkGridConfigSO.WallCellArea"/>）换算，
        /// 且<strong>不</strong>登记个人涂地统计 —— 墙面墨迹不计入涂地覆盖率/涂地王/单局 XP 的涂地项，
        /// 只给大招充能。与 <see cref="AddSpecialPoints(int)"/> 分开是因为墙格 0.1m、地面格 0.1x0.14m，
        /// 混用地面格面积会把墙墨多算约 1.4 倍。
        /// </summary>
        public void AddSpecialPointsFromWall(int paintedWallCells)
        {
            if (paintedWallCells <= 0)
                return;

            float wallCellArea = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.WallCellArea
                : 0.01f;
            float pointsPerM2 = config != null ? config.specialPointsPerSquareMeter : 1f;
            AddSpecialPointsRaw(paintedWallCells * wallCellArea * pointsPerM2);
        }

        /// <summary>直接按能量点充能：调试面板（F3）与测试直充用，跳过面积换算，门槛检查与就绪反馈同涂地路径。</summary>
        public void AddSpecialPointsRaw(float points)
        {
            if (points <= 0f || IsPerformingSpecial || !_health.IsAlive)
                return;

            float req = SpecialRequired;
            _specialPoints = Mathf.Min(req, _specialPoints + points);

            if (!_wasSpecialReady && _specialPoints >= req)
            {
                _wasSpecialReady = true;
                if (_health.Team == Team.TeamA && AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayUltReady();
                }
            }
        }

        /// <summary>
        /// 声明本帧主武器输入被抑制（暂停 / 终场 / 死亡 / 副武器投掷仲裁占用）。
        /// 抑制帧会取消蓄力、扫射、推地与挥击前摇，<strong>但不会把抑制当成松手而开火</strong>，
        /// 并把「上一帧按住」按 <paramref name="triggerStillHeld"/> 记账：恢复后仍按住的扳机
        /// 不会被误判成新按下（滚筒不多挥一次），暂停中真的松手也不会被吞掉。
        /// 标志每帧独立、帧末自动清零；Bot 不调用即完全不受影响。
        /// </summary>
        /// <param name="suppressed">是否抑制本帧主武器输入。</param>
        /// <param name="triggerStillHeld">
        /// 抑制期间物理扳机是否仍被按住（默认 true，保守地视为按住）。
        /// 关闭输入的一方（PlayerController）传入真实按键状态可得到最精确的边沿恢复。
        /// </param>
        public void SetTriggerSuppressed(bool suppressed = true, bool triggerStillHeld = true)
        {
            _triggerSuppressedThisFrame = suppressed;
            _triggerHeldWhileSuppressed = triggerStillHeld;
        }

        /// <summary>本帧主武器输入是否已被 <see cref="SetTriggerSuppressed"/> 抑制。</summary>
        public bool TriggerSuppressed => _triggerSuppressedThisFrame;

        public bool TryFireMainWeapon(Vector3 aimDirection)
        {
            return TryFireMainWeapon(aimDirection, ResolveAimGroundPoint(aimDirection));
        }

        /// <summary>
        /// Bot 专用开火入口：横向瞄准点每帧按当前朝向重算（保持弹道方向正确），
        /// 只把「地面高度」射线按 <paramref name="groundResolveIntervalSeconds"/> 节流采样。
        /// 7 个 Bot 每帧各打一次全场景地面射线（~420 次/秒）没必要——地面高度变化远慢于朝向，
        /// 0.1s 的 Y 值滞后对落点的影响小于 1cm。
        /// </summary>
        public bool TryFireMainWeaponThrottled(Vector3 aimDirection, float groundResolveIntervalSeconds = 0.1f)
        {
            // 有玩家控制器时直接走原路径（玩家有真实地面准星点，不需要节流）
            if (_playerController != null)
                return TryFireMainWeapon(aimDirection);

            float interval = Mathf.Clamp(groundResolveIntervalSeconds, 0.02f, 1f);
            float now = Time.time;

            Vector3 flatAim = new Vector3(aimDirection.x, 0f, aimDirection.z);
            if (flatAim.sqrMagnitude < 0.0001f)
                flatAim = transform.forward;
            flatAim.Normalize();

            float maxRange = config != null ? config.maxRange : 12f;
            Vector3 point = transform.position + flatAim * maxRange;

            if (now >= _nextAimGroundYSampleTime)
            {
                _nextAimGroundYSampleTime = now + interval;
                _cachedAimGroundY = Physics.Raycast(point + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 12f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore)
                    ? hit.point.y
                    : transform.position.y;
            }

            point.y = _cachedAimGroundY;
            return TryFireMainWeapon(aimDirection, point);
        }

        /// <summary>
        /// 显式指定地面瞄准点的开火重载：弹道会反解仰角，使弹丸落点与 <paramref name="aimGroundPoint"/> 重合。
        /// 旧签名保留并转发到此重载，跨系统调用方无需改动。
        /// </summary>
        public bool TryFireMainWeapon(Vector3 aimDirection, Vector3 aimGroundPoint)
        {
            if (_health == null || !_health.IsAlive || IsPerformingSpecial)
                return false;

            // 终场哨响 / 本帧输入被抑制时不得再产生新的射击（已发射的弹丸仍按各自规则飞行）。
            if (!MatchManager.CombatSettlementAllowed || _triggerSuppressedThisFrame)
                return false;

            if (_strategy == null)
                RebuildStrategy();

            // 保留完整 3D 方向（不再压平）：抬头时武器必须真的朝天上打，
            // 具体弹道由各策略按「枪口 → 准星命中点」自行解算。
            Vector3 aimDir = aimDirection.sqrMagnitude > 0.0001f
                ? aimDirection.normalized
                : transform.forward;
            if (aimDir.sqrMagnitude < 0.0001f)
                aimDir = Vector3.forward;

            _triggerHeldThisFrame = true;
            _lastAim = WeaponAim.FromDirection(aimDir, aimGroundPoint);
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

            float maxRange = config != null ? config.maxRange : 12f;
            Vector3 point = transform.position + flatAim * maxRange;
            if (Physics.Raycast(point + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 12f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
            {
                point.y = hit.point.y;
            }
            return point;
        }

        /// <summary>副武器手雷出手点：弹道预览与实际投掷统一取自这里，保证落点完全重合。</summary>
        public Vector3 GetSubBombMuzzlePosition()
        {
            return muzzleTransform != null
                ? muzzleTransform.position
                : transform.position + Vector3.up * 0.8f;
        }

        /// <summary>手雷剩余冷却秒数（0 = 可投）：触屏手雷按钮冷却显示的数据源。</summary>
        public float BombCooldownRemaining => _bombCooldown;

        /// <summary>手雷冷却总时长（当前配置值，未配置时回退 0.6s）。</summary>
        public float BombCooldownDuration => config != null ? config.bombCooldown : 0.6f;

        /// <summary>
        /// 当前是否满足投掷条件（存活 / 非大招演出 / 不在冷却 / 终场前 / 墨水足够）：
        /// 弹道预览用它决定虚线是否变灰闪烁，与 <see cref="TryThrowSubBombBallistic"/> 的门禁保持一致。
        /// </summary>
        public bool CanThrowSubBombNow
        {
            get
            {
                if (_health == null || !_health.IsAlive || IsPerformingSpecial)
                    return false;
                if (_bombCooldown > 0f || !MatchManager.CombatSettlementAllowed)
                    return false;
                float bombCost = config != null ? config.bombInkCost : 0f;
                return bombCost <= 0f || (_motor != null && _motor.CurrentInk >= bombCost);
            }
        }

        /// <summary>
        /// 副武器手雷投掷（真实弹道）：以指定初速从出手点抛出，远近与反弹全部由物理决定。
        /// 玩家链路由准星落点反解初速（指哪落哪）；旧俯仰制 API 仍可传入任意初速。
        /// </summary>
        public bool TryThrowSubBombBallistic(Vector3 throwVelocity)
        {
            if (!_health.IsAlive || IsPerformingSpecial || _bombCooldown > 0f)
                return false;

            // 终场后不再接受新的副武器投掷（终场前已投出的手雷仍按各自引信走完）。
            if (!MatchManager.CombatSettlementAllowed)
                return false;

            // 手雷与墨水脱钩（设计变更）：只受冷却限制，不再挂 70 墨。
            // 仅当配置显式要求耗墨（bombInkCost > 0）才扣墨，杜绝「喷几枪就丢不出雷 + 静默失败」。
            float bombCost = config != null ? config.bombInkCost : 0f;
            if (bombCost > 0f && !_motor.TryConsumeInk(bombCost))
            {
                return false;
            }

            _bombCooldown = config != null ? config.bombCooldown : 0.6f;

            Vector3 startPos = GetSubBombMuzzlePosition();

            if (InkBombPool.Instance != null)
            {
                InkBombPool.Instance.ThrowBomb(
                    startPos,
                    throwVelocity,
                    _health.Team,
                    config,
                    _addSpecialPointsOnlyDelegate,
                    _health,
                    _addSpecialPointsFromWallDelegate);
            }

            if (AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayBombThrow();
            }

            return true;
        }

        /// <summary>
        /// 旧落点制签名（Bot 与既有测试在用，保持向后兼容）：
        /// 按固定初速做弹道学反解，炸弹仍落在 targetGroundPos（指哪落哪），
        /// 飞行/反弹/引信走与玩家完全相同的弹道管线。
        /// </summary>
        public bool TryThrowSubBomb(Vector3 targetGroundPos)
        {
            Vector3 startPos = GetSubBombMuzzlePosition();
            Vector3 velocity = InkBombTrajectoryUtility.ComputeThrowVelocityToLandAt(startPos, targetGroundPos, config);
            return TryThrowSubBombBallistic(velocity);
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

            // 终场哨响后不再开新的大招（正在演出中的 Slam/Storm 由 Update 里的门禁收尾）。
            if (!MatchManager.CombatSettlementAllowed)
                return false;

            _activeSpecialKind = config != null ? config.specialKind : SpecialKind.Slam;
            _specialPoints = 0f;
            _wasSpecialReady = false;
            // 释放大招顺便回满墨水罐（经典爽感设计）
            _motor.RefillInkFull();

            if (AudioManager.Instance != null)
            {
                // 只有玩家自己的大招压 BGM；Bot 大招保留音效但不打断玩家音乐
                AudioManager.Instance.PlaySpecialCast(_activeSpecialKind, _playerController != null);
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
            _specialGroundCenter = ResolveGroundPoint(transform.position, _selfCollider);

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

            return ResolveGroundPoint(target, _selfCollider);
        }

        /// <summary>
        /// 解析某 XZ 处的真实地面点（Y 用射线命中高度）。
        /// 旧 1 参签名保留；P20 起大招路径必须传 <paramref name="self"/>：
        /// 射线从头顶 6m 向下、mask 全开，不过滤自己时会先命中施法者的 CharacterController 顶端
        /// （脚底 +1.25m），导致预警圈/冲击波/涂地球心全部悬空——玩家按屏幕读圈与地面判定圆严重不共形。
        /// </summary>
        private static Vector3 ResolveGroundPoint(Vector3 position, Collider self = null)
        {
            Vector3 origin = new Vector3(position.x, position.y + 6f, position.z);
            if (self == null)
            {
                if (Physics.Raycast(origin, Vector3.down, out RaycastHit legacyHit, 20f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore))
                {
                    return new Vector3(position.x, legacyHit.point.y, position.z);
                }
                return position;
            }

            // NonAlloc + 手动取最近命中（原 RaycastAll 每次起跳/落地都会分配一个 RaycastHit[]）。
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, GroundResolveHits, 20f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            float nearestY = 0f;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = GroundResolveHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(self.transform))
                    continue; // 跳过施法者自身（CharacterController/视觉体），落到真实地面
                if (hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                nearestY = hit.point.y;
                found = true;
            }
            return found ? new Vector3(position.x, nearestY, position.z) : position;
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
                // 终场哨响时还在空中的 Slam：直接收尾（落地冲击不再结算伤害/涂地），
                // 避免结算画面里角色仍吊在半空——表现层只是复位，不清场。
                if (!MatchManager.CombatSettlementAllowed)
                {
                    CancelSpecialImmediate();
                }
                else
                {
                    UpdateSpecialSlam(dt);
                }
            }
        }

        /// <summary>
        /// 主武器状态机在 LateUpdate 推进：此时玩家/Bot 的 Update 已注入本帧扳机状态，
        /// 蓄力狙能可靠地检测“松开”并发射，滚筒也能拿到最终移动位置。
        /// </summary>
        private void LateUpdate()
        {
            float dt = Time.deltaTime;

            // 本地玩家的准星每帧都在变，不能只在「按住扳机」的帧更新 _lastAim：
            // 蓄力狙真正发射发生在松手帧，而松手帧 PlayerController 不会再调用 TryFireMainWeapon，
            // 旧实现会让光束永远慢一帧（甩枪/跟踪时 27m 外可差米级）。这里用本帧准星补齐。
            if (_playerController != null &&
                _playerController.isActiveAndEnabled &&
                _playerController.Health != null &&
                _playerController.Health.IsAlive)
            {
                _lastAim = WeaponAim.FromDirection(
                    _playerController.AimDirection, _playerController.AimPoint);
                _hasAim = true;
            }

            if (_strategy != null)
            {
                if (_triggerSuppressedThisFrame)
                {
                    // 抑制段第一帧先记住「原本是否真的按着扳机」：只有蓄力/扫射/推地/前摇中的武器
                    // 才需要在恢复后继续视为按住，否则暂停前的 UI 点击也会吞掉恢复后的第一次按下。
                    if (!_triggerSuppressedLastFrame)
                        _suppressionEngaged = _strategy.IsTriggerEngaged;

                    // 本帧输入被抑制（暂停 / 终场 / 死亡 / 副武器投掷仲裁占用）：
                    // 先取消蓄力、扫射、推地与挥击前摇，再以「未按下」推进 Tick——
                    // 绝不能把「输入被禁用」当成松手，否则蓄力狙会自动开炮、加特林自动扫射。
                    _strategy.OnTriggerSuppressed(this);
                    _strategy.Tick(dt, false, _lastAim, this);

                    // 如实记账「上一帧扳机状态」：抑制期间玩家仍按着扳机 → 恢复后不误判成新按下
                    // （滚筒不会多挥一次、蓄力狙不会补射）；暂停中真的松手 → 恢复后第一次按下照常生效。
                    _triggerHeldLastFrame = _suppressionEngaged && _triggerHeldWhileSuppressed;
                    _triggerSuppressedLastFrame = true;
                }
                else
                {
                    _strategy.Tick(dt, _triggerHeldThisFrame, _lastAim, this);
                    _triggerHeldLastFrame = _triggerHeldThisFrame;
                    _triggerSuppressedLastFrame = false;
                    _suppressionEngaged = false;
                }
            }
            else
            {
                _triggerHeldLastFrame = _triggerHeldThisFrame;
                _triggerSuppressedLastFrame = false;
                _suppressionEngaged = false;
            }

            UpdateChargerLaser();
            UpdateBeamVisual(dt);

            _triggerHeldThisFrame = false;
            _triggerSuppressedThisFrame = false;
        }

        /// <summary>内部与 EditMode 测试驱动：以指定 dt 执行一帧策略推进。</summary>
        public void LateUpdateForTesting(float dt)
        {
            if (_strategy != null)
            {
                _strategy.Tick(dt, _triggerHeldThisFrame, _lastAim, this);
            }
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
                    // 落地帧先让预警圈长满到伤害半径再结算（P20 前直接 return，
                    // 圈从未以完整 5.2m 显示过，60fps 时只到 ~5.12m）
                    UpdateSpecialWarningRing(config != null ? config.specialSplatRadius : 5.2f);
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

            // 1. 伤害半径整圆 + 9 枚边缘卫星泼墨，打散正圆轮廓。
            //    涂地取 max(paint, damage)：让「墨迹边界 ≈ 伤害边界」，主涂地(5.0)不再比判定(5.2)小一圈；
            //    卫星墨斑半径收进伤害半径内，玩家按墨迹读范围不再被 5.95m 的外溢墨斑误导。
            InkSurfaceController surface = InkSurfaceController.Instance;
            float effectivePaintRadius = Mathf.Max(paintRadius, damageRadius);

            // 终场哨响后落地的冲击波只保留表现（粒子/音效/震屏），不再涂地、不再统计、不再造成击杀。
            bool settleable = MatchManager.CombatSettlementAllowed;

            if (settleable && surface != null)
            {
                int groundChanged = surface.PaintCircle(_specialGroundCenter, effectivePaintRadius, myTeam);
                if (InkWallSurface.ActiveCount > 0)
                {
                    InkWallSurface.PaintAllInSphere(_specialGroundCenter, effectivePaintRadius, myTeam);
                }

                for (int i = 0; i < 9; i++)
                {
                    float angle = (i / 9f) * Mathf.PI * 2f + Random.Range(-0.18f, 0.18f);
                    float distance = effectivePaintRadius * Random.Range(0.5f, 0.72f);
                    Vector3 point = _specialGroundCenter + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
                    float satelliteRadius = Mathf.Min(Random.Range(1.1f, 1.7f), damageRadius - distance);
                    groundChanged += surface.PaintCircle(ResolveGroundPoint(point, _selfCollider), satelliteRadius, myTeam);
                }

                if (_health != null && groundChanged > 0)
                {
                    MatchStatsService.RegisterTurf(_health, groundChanged * surface.CellArea);
                }
            }

            // 2. 冲击波伤害：核心 3.2m 内 180 秒杀，外圈平滑衰减到 55
            if (settleable)
            {
                SpecialCombat.ApplySlamDamage(
                    _specialGroundCenter, myTeam, damageRadius, innerRadius, innerDamage, outerDamage, falloffWeight, _health);
            }

            // 3. 强力视觉与震屏反馈
            if (VfxPool.Instance != null)
            {
                VfxPool.Instance.SpawnInkImpact(_specialGroundCenter, Vector3.up, teamColor, 1.2f, 24);
                VfxPool.Instance.SpawnInkSplat(_specialGroundCenter, teamColor, 1.35f, 46);
                VfxPool.Instance.SpawnShockwaveRing(_specialGroundCenter, teamColor, 0.6f, damageRadius, 0.32f);
                // 延迟余波收在伤害半径内（P20 前 1.15R 会铺到 5.98m，超出 5.2m 判定圈误导范围）。
                VfxPool.Instance.SpawnDelayedShockwaveRing(
                    0.1f, _specialGroundCenter, teamColor, damageRadius * 0.45f, damageRadius, 0.5f);
            }

            if (AudioManager.Instance != null)
            {
                if (AudioManager.Instance.specialSlamClip != null)
                {
                    AudioManager.Instance.PlayOne(AudioManager.Instance.specialSlamClip, 0.95f, 0.98f, 1.02f);
                }
                AudioManager.Instance.PlayInkHitGround(
                    _specialGroundCenter, 2f, effectivePaintRadius);
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

            // 内圈秒杀盘半径独立于外圈缩放：inner/outer 比例随当前配置（默认 3.2/5.2）
            float damageRadius = config != null ? config.specialSplatRadius : 5.2f;
            float innerRadius = config != null ? config.specialInnerKillRadius : 3.2f;
            float innerRatio = Mathf.Clamp01(innerRadius / Mathf.Max(0.1f, damageRadius));
            if (_specialInnerRenderer != null)
            {
                _specialInnerRenderer.transform.localScale = new Vector3(innerRatio, 1f, innerRatio);
            }

            if (_propBlock == null)
                _propBlock = new MaterialPropertyBlock();

            Color c = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(_health.Team)
                : Color.yellow;
            SetRingColor(_specialDiscRenderer, c, 0.32f);
            SetRingColor(_specialInnerRenderer, c, 0.55f);
            SetRingColor(_specialRingRenderer, c, 0.85f);
            _specialWarningRing.gameObject.SetActive(true);
        }

        /// <summary>预警圈单层配色：队色 × 层专属透明度（危险盘淡、秒杀区亮、亮边最实）。</summary>
        private void SetRingColor(MeshRenderer renderer, Color teamColor, float alpha)
        {
            if (renderer == null)
                return;
            teamColor.a = alpha;
            _propBlock.SetColor(BaseColorProp, teamColor);
            _propBlock.SetColor(ColorProp, teamColor);
            renderer.SetPropertyBlock(_propBlock);
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

            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");

            // P20 三层预警读法：实心危险盘（=伤害半径 5.2m，盘内会被砸）+ 内圈秒杀盘（3.2m）高亮 + 外沿亮边。
            // 旧版只有一条 0.92~1.0 的细环带，且因 ResolveGroundPoint 自命中悬空 1.25m，范围完全读不出来。
            _specialDiscMesh = VfxPool.BuildRingMesh(48, 0.03f, 1.0f);
            _specialDiscRenderer = CreateRingLayer(go.transform, "Disc", _specialDiscMesh, sh, 0f);

            _specialInnerMesh = VfxPool.BuildRingMesh(48, 0.03f, 1.0f);
            _specialInnerRenderer = CreateRingLayer(go.transform, "Inner", _specialInnerMesh, sh, 0.01f);

            _specialRingMesh = VfxPool.BuildRingMesh(40, 0.92f, 1.0f);
            _specialRingRenderer = CreateRingLayer(go.transform, "Edge", _specialRingMesh, sh, 0.02f);
            _specialRingMaterial = _specialRingRenderer.sharedMaterial;

            go.SetActive(false);
            _specialWarningRing = go.transform;
        }

        /// <summary>预警圈单层：独立 renderer + 透明材质，localY 分层防 z-fight（容器 XZ 缩放统一驱动半径）。</summary>
        private MeshRenderer CreateRingLayer(Transform parent, string layerName, Mesh mesh, Shader shader, float localY)
        {
            var layerGo = new GameObject($"{name}_SpecialRing_{layerName}");
            layerGo.transform.SetParent(parent, false);
            layerGo.transform.localPosition = new Vector3(0f, localY, 0f);

            var mf = layerGo.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var renderer = layerGo.AddComponent<MeshRenderer>();
            var material = new Material(shader);
            VfxPool.MakeUrpUnlitTransparent(material);
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            if (layerName == "Disc") _specialDiscMaterial = material;
            else if (layerName == "Inner") _specialInnerMaterial = material;

            return renderer;
        }

        // ------------------------------------------------------------------ 主武器表现

        private void UpdateChargerLaser()
        {
            if (_strategy is ChargerWeaponStrategy charger && charger.IsCharging && _hasAim &&
                _health != null && _health.IsAlive)
            {
                EnsureLaserSight();

                Vector3 muzzle = ResolveMuzzlePosition();
                Vector3 chest = transform.position + Vector3.up * 0.95f;
                // 长枪管贴墙/打脚下时，从枪口指向准星会出现「瞄准点已在枪口后方」，
                // 此时改用胸口起点，激光与光束才会真正收敛到准星点。
                Vector3 origin = _lastAim.GetShotOrigin(muzzle, chest);
                float rangeMax = config != null ? Mathf.Max(1f, config.chargeRangeMax) : 27f;
                float rangeMin = config != null ? Mathf.Max(0.5f, config.chargeRangeMin) : 11f;
                // 指示线长度跟随蓄力：低蓄力时如实显示这一枪能打多远，也顺带给蓄力一个视觉反馈。
                float range = Mathf.Lerp(rangeMin, rangeMax, Mathf.Clamp01(charger.Charge01));
                Vector3 direction = _lastAim.GetShotDirection(origin, 0.2f);
                Vector3 target = origin + direction * range;

                if (WeaponBeamSystem.TryFindWorldHit(origin, direction, range, gameObject, out RaycastHit wallHit))
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
            Vector3 aimDir = _hasAim ? _lastAim.Direction : transform.forward;
            if (aimDir.sqrMagnitude < 0.0001f)
                aimDir = Vector3.forward;

            if (muzzleTransform != null)
            {
                Vector3 muzzle = muzzleTransform.position;
                if (!IsMuzzleBlocked(muzzle))
                    return muzzle;

                // 枪口被墙体/掩体挤出时从胸口沿瞄准方向补正，避免子弹从墙里飞出去（对齐 INKWAVE 的枪口兜底）
                return transform.position + Vector3.up * 0.95f + aimDir * 0.3f;
            }

            return transform.position + Vector3.up * 0.75f + aimDir * 0.55f;
        }

        /// <summary>枪口是否埋进了非本角色的碰撞体（贴墙、趴地、抵住掩体）。</summary>
        private bool IsMuzzleBlocked(Vector3 muzzle)
        {
            int count = Physics.OverlapSphereNonAlloc(
                muzzle, 0.06f, _muzzleOverlaps, GameLayers.QueryMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = _muzzleOverlaps[i];
                if (col != null && col.transform.root != transform.root)
                    return true;
            }

            return false;
        }

        private Team TeamId => _health != null ? _health.Team : Team.TeamA;

        private static void DestroySafe(UnityEngine.Object target)
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
            // 终场后不再生成新的墨弹（前摇/状态机可能在哨响后才走完）。
            if (!MatchManager.CombatSettlementAllowed)
                return;

            ShotsFired++;
            LastShotStrength01 = 1f;
            if (InkProjectilePool.Instance != null)
            {
                InkProjectilePool.Instance.SpawnProjectile(shot, TeamId, config, gameObject, _addSpecialPointsDelegate, _addSpecialPointsFromWallDelegate);
            }
            EmitShotFeedback(shot.Origin, shot.Direction, shot.IsBlaster ? 1.25f : 1f);
            OnFired?.Invoke();
        }

        void IWeaponRuntimeHost.FireBeam(WeaponBeam beam)
        {
            if (!MatchManager.CombatSettlementAllowed)
                return;

            ShotsFired++;
            // 蓄力刻度参与全部表现层强度：点按（0.12）与满蓄（1）的镜头/模型/闪光/音效必须分层
            LastShotStrength01 = Mathf.Clamp01(beam.Intensity01);
            BeamResult result = WeaponBeamSystem.Fire(beam, TeamId, gameObject, _addSpecialPointsDelegate, _addSpecialPointsFromWallDelegate);
            LastBeamResult = result;
            PlayBeamVisual(result, beam.Intensity01);
            // 枪口闪光跟实际枪口走；光束在近距可能改从胸口起点结算。
            EmitShotFeedback(
                ResolveMuzzlePosition(),
                beam.Direction,
                Mathf.Lerp(0.75f, 1.3f, LastShotStrength01));
            OnFired?.Invoke();
        }

        /// <summary>
        /// 开火瞬间的枪口闪光 + 本地玩家镜头冲击（P16/P28）。
        /// 闪光对所有角色可见（远处敌人开火才有可读性），镜头冲击只作用于本地玩家。
        /// P28 起闪光与镜头冲击各自独立节流：闪光按武器射速合并齐射，镜头冲击按帧合并，
        /// 因此泼桶 8 滴 / 滚筒 9 滴只闪一次、只推一次，而小枪/双枪/加特林每发都能读到反馈。
        /// </summary>
        private void EmitShotFeedback(Vector3 origin, Vector3 direction, float extraScale)
        {
            WeaponFeelParams feel = CurrentFeel;

            float interval = config != null
                ? Mathf.Clamp(config.fireInterval * 0.6f, MuzzleFlashMinInterval, 0.3f)
                : MuzzleFlashMinInterval;
            if (Time.time - _lastMuzzleFlashTime >= interval)
            {
                _lastMuzzleFlashTime = Time.time;

                Color teamColor = InkSurfaceController.Instance != null
                    ? InkSurfaceController.Instance.GetTeamColor(TeamId)
                    : (TeamId == Team.TeamB ? new Color(0.15f, 0.42f, 1f) : new Color(1f, 0.44f, 0.06f));

                if (VfxPool.Instance != null)
                {
                    VfxPool.Instance.SpawnShotFlash(
                        origin, direction, teamColor, feel.MuzzleFlashScale * extraScale);
                }
            }

            // Bot 没有 PlayerController：镜头冲击只属于本地玩家，4v4 里被敌人开火震屏会很晕。
            // 同帧闸门：多颗弹丸齐射只推一次；单发武器跨帧开火时每发都推。
            if (_playerController == null || TopDownCameraFollow.Instance == null)
                return;
            if (Time.frameCount == _lastCameraPunchFrame)
                return;
            _lastCameraPunchFrame = Time.frameCount;

            float strength = WeaponFeelMath.ShotStrengthScale(LastShotStrength01);
            TopDownCameraFollow.Instance.AddShotPunchScaling(
                feel.CameraKickMeters * strength,
                feel.CameraKickRollDegrees * strength,
                feel.CameraKickFovDegrees * strength,
                feel.CameraKickDecayRate,
                feel.SpringOmega);
        }

        void IWeaponRuntimeHost.ApplyRoll(RollSweep sweep)
        {
            if (!MatchManager.CombatSettlementAllowed)
                return;

            int painted = WeaponRollSystem.Apply(sweep, TeamId, _rollHitTimes, Time.time, _health, _addSpecialPointsFromWallDelegate);
            if (painted > 0)
            {
                AddSpecialPoints(painted);
            }
            LastShotStrength01 = 1f;
            OnFired?.Invoke();
        }

        void IWeaponRuntimeHost.PaintGround(Vector3 worldPos, float radius)
        {
            if (InkSurfaceController.Instance == null || !MatchManager.CombatSettlementAllowed)
                return;

            int changed = InkSurfaceController.Instance.PaintCircle(worldPos, radius, TeamId);
            if (changed > 0)
            {
                AddSpecialPoints(changed);
            }
        }

        void IWeaponRuntimeHost.ReportShotFeedback()
        {
            if (GetComponent<WeaponAudioDriver>() == null && TeamId == Team.TeamA && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayWeaponShot(CurrentWeaponKind);
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
