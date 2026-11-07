using System.Collections.Generic;
using P1.Core;
using P1.Environment;
using P1.Ink;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.AI
{
    /// <summary>
    /// Bot 控制器：巡涂 / 交战 / 补给 / 残血撤退 / 死亡 五状态有限状态机。
    /// 复用与玩家完全相同的 InkCharacterMotor + WeaponController + CombatantHealth，
    /// 寻路只负责给出移动方向，位移与潜行规则和玩家一致。
    ///
    /// 拟人化三件套（对齐 Inkwave bots.js）：
    /// 1. 瞄准 = 二阶临界阻尼弹簧 + 转速上限 + 缓慢漂移/拉枪过冲误差，且「枪口真的指哪打哪」；
    /// 2. 走位 = 移动朝向按角速度上限平滑（路点切换/横向拉枪不再抽搐），速度按指数逼近；
    /// 3. 决策 = 墨水加权 A*（己方 0.5 / 中性 1.0 / 敌方 3.0）+ 残血钻己方墨潜行回血。
    /// </summary>
    [RequireComponent(typeof(InkCharacterMotor))]
    [RequireComponent(typeof(WeaponController))]
    [RequireComponent(typeof(CombatantHealth))]
    public class BotController : MonoBehaviour
    {
        public enum BotState { Paint, Engage, Refill, Dead, Retreat }

        [SerializeField] private BotConfigSO config;

        /// <summary>场景里序列化的原始配置（BotConfig.asset）；难度缩放以它为基准。</summary>
        private BotConfigSO _baseConfig;

        /// <summary>按当前难度缩放出来的运行时副本；普通档为 null（直接复用原始资产）。</summary>
        private BotConfigSO _scaledConfig;

        /// <summary>已经应用的难度档位（int.MinValue 表示尚未应用）。</summary>
        private int _appliedDifficulty = int.MinValue;

        /// <summary>
        /// 本局风格个性倍率（由 <see cref="BotLoadoutService"/> 每局抽取）。
        /// 默认恒等：没有抽卡（训练场 / 测试沙盒）时行为与旧版完全一致。
        /// </summary>
        private BotPersonality _personality = BotPersonality.Identity;

        private InkCharacterMotor _motor;
        private WeaponController _weapon;
        private CombatantHealth _health;

        private readonly List<Vector3> _path = new List<Vector3>();
        private int _pathIndex;
        private float _repathTimer;
        private Vector3 _moveTarget;
        private bool _hasMoveTarget;
        private float _paintGoalTimer;

        // ---- 寻路/选点失败退避 ----
        // 导航网格存在但 A* 找不到路时，旧实现会塞一个直线 goal 让 Bot 顶着墙冲锋；
        // 现在改为清空路径 + 按连续失败次数退避（见 BotRecoveryPolicy），并让调用方放弃该目标。
        private int _pathFailures;
        private float _pathRetryTimer;
        private Vector3 _lastPathGoal;
        private bool _hasLastPathGoal;
        private const float PathGoalChangedDistance = 2.5f;
        private int _paintPickFailures;
        private float _paintPickRetryTimer;
        private int _retreatPickFailures;

        // 不可达涂地目标黑名单（固定槽位环形覆盖，避免 List 扩容产生 GC）
        private const int PaintBlacklistSlots = 6;
        private const float PaintBlacklistSeconds = 12f;
        private const float PaintBlacklistRadius = 4f;
        private readonly Vector3[] _paintBlacklistPositions = new Vector3[PaintBlacklistSlots];
        private readonly float[] _paintBlacklistUntil = new float[PaintBlacklistSlots];
        private int _paintBlacklistCursor;

        private float _thinkTimer;
        private float _reactionTimer;
        private CombatantHealth _targetEnemy;
        private Vector3 _targetVelocity;
        private Vector3 _prevTargetPos;
        private const float DefaultPerceptionInterval = 0.15f;

        /// <summary>感知/决策节拍：优先取 BotConfigSO.perceptionInterval，旧资产缺字段时回退 0.15s。</summary>
        private float PerceptionSeconds => config != null && config.perceptionInterval > 0.01f
            ? config.perceptionInterval
            : DefaultPerceptionInterval;

        private readonly HumanAimSpring _aim = new HumanAimSpring();
        private float _aimPhaseA;
        private float _aimPhaseB;
        private float _acquireAge = 1f;
        private float _acquireSign = 1f;
        private bool _firingLatch;

        private float _moveHeadingDegrees;
        private float _moveMagnitude;
        private float _strafeDir = 1f;
        private float _strafeAmp = 1f;
        private float _strafeTimer;
        private float _strafeBlend;

        private float _paintSweepPhase;
        private float _paintAheadTimer;
        private bool _aheadNeedsPaint = true;
        private float _chargerPaintPause;

        private float _bombTimer;
        private float _specialRetryTimer;

        private Vector3 _threatPosition;
        private float _threatMemoryTimer;
        private float _retreatTimer;
        private float _retreatRepathTimer;
        private float _retreatCooldown;

        private float _stuckTimer;
        private Vector3 _stuckAnchor;

        private float _refillSearchTimer;
        private float _elapsed;

        private InkPathWeights _pathWeights = InkPathWeights.Default;
        private Vector3 _aimPoint;

        public BotState State { get; private set; } = BotState.Paint;
        public string DebugTargetName => _targetEnemy != null ? _targetEnemy.name : "-";

        /// <summary>
        /// 本局昵称（INKWAVE 式：每局从名单洗牌发放）。
        /// 未分配时为空，击杀播报回退到 GameObject 名（BotA_xx / BotB_xx）。
        /// </summary>
        public string DisplayName { get; private set; }

        /// <summary>本局生效的风格个性倍率（调试/测试读取）。</summary>
        public BotPersonality DebugPersonality => _personality;

        /// <summary>场景序列化的原始 BotConfigSO（测试/工具用来区分「共享资产 vs 运行时克隆」）。</summary>
        public BotConfigSO DebugBaseConfig => _baseConfig;
        public bool HasPath => _path.Count > 0;
        public float DebugAimYaw => _aim.AngleDegrees;

        /// <summary>当前武器推导出的理想交火距离（调试面板/测试读取）。</summary>
        public float DebugPreferredDistance => BotTactics.PreferredEngageDistance(
            _weapon != null ? _weapon.CurrentWeaponKind : WeaponKind.Shooter,
            _weapon != null ? _weapon.Config : null,
            config);

        /// <summary>
        /// 当前生效的 Bot 配置：普通难度就是 BotConfig.asset 本身；
        /// 轻松/硬核是启动或切换难度时克隆并缩放出来的运行时副本。
        /// </summary>
        public BotConfigSO ActiveConfig => config;

        private void Awake()
        {
            _motor = GetComponent<InkCharacterMotor>();
            _weapon = GetComponent<WeaponController>();
            _health = GetComponent<CombatantHealth>();
            _stuckAnchor = transform.position;
            _paintSweepPhase = Random.Range(0f, 10f);
            _aimPhaseA = Random.Range(0f, 20f);
            _aimPhaseB = Random.Range(0f, 20f);
            _aim.Reset(transform.eulerAngles.y);

            if (_baseConfig == null) _baseConfig = config;
            ApplyDifficulty(GameSettingsService.BotDifficulty);
            RefreshPathWeights();
        }

        private void OnEnable()
        {
            if (_health == null) _health = GetComponent<CombatantHealth>();
            if (_health != null) _health.OnRespawned += HandleRespawned;

            // 难度在选择面板/设置里被改过（本组件当时可能停用或尚未 Awake），重新对齐一次。
            ApplyDifficulty(GameSettingsService.BotDifficulty);
            GameSettingsService.BotDifficultyChanged += HandleBotDifficultyChanged;
        }

        private void OnDisable()
        {
            if (_health != null) _health.OnRespawned -= HandleRespawned;
            GameSettingsService.BotDifficultyChanged -= HandleBotDifficultyChanged;
        }

        private void OnDestroy()
        {
            GameSettingsService.BotDifficultyChanged -= HandleBotDifficultyChanged;
            DestroyScaledConfig();
        }

        private void HandleBotDifficultyChanged(int level)
        {
            ApplyDifficulty(level);
        }

        /// <summary>
        /// 按难度与个性重建生效配置。普通难度 + 恒等个性直接指向原始资产（零分配），
        /// 其余档位克隆一份并按 <see cref="BotDifficultyMath"/> 的倍率缩放，
        /// 再叠加本局 <see cref="BotPersonality"/> 的风格倍率（两层乘算都钳回合法区间）。
        /// 幂等：同一档位重复调用只做一次工作。
        /// </summary>
        private void ApplyDifficulty(int level)
        {
            int normalized = BotDifficultyMath.Normalize(level);
            if (_appliedDifficulty == normalized) return;
            _appliedDifficulty = normalized;

            DestroyScaledConfig();

            BotConfigSO source = _baseConfig != null ? _baseConfig : config;
            _scaledConfig = BotDifficultyMath.CreateRuntimeConfig(source, normalized);
            config = _scaledConfig != null ? _scaledConfig : source;

            if (!_personality.IsIdentity())
            {
                // 陷阱：普通难度下 CreateRuntimeConfig 原样返回共享 BotConfig.asset，
                // 直接在它身上乘个性会永久污染全部 Bot（含后续对局）。非恒等个性必须落在私有克隆上。
                if (_scaledConfig == null || _scaledConfig == _baseConfig)
                {
                    _scaledConfig = Object.Instantiate(source);
                    _scaledConfig.name = source != null ? source.name + " (个性)" : "BotConfig (个性)";
                    _scaledConfig.hideFlags = HideFlags.DontSave;
                    config = _scaledConfig;
                }

                BotPersonalityMath.ApplyTo(config, _personality);
            }
        }

        private void DestroyScaledConfig()
        {
            if (_scaledConfig == null || _scaledConfig == _baseConfig) return;
            if (Application.isPlaying)
                Destroy(_scaledConfig);
            else
                DestroyImmediate(_scaledConfig);
            _scaledConfig = null;
        }

        /// <summary>
        /// 复活 / 重开比赛统一复位 AI 状态。MatchManager.ResetWorld 走的是
        /// <see cref="CombatantHealth.ResetImmediately"/>，它会广播 OnRespawned；
        /// 旧实现只在 State==Dead 转活时复位，第二局 Bot 会直奔上一局的路径 / 涂地点。
        /// 幂等：重复调用只是再清一遍路径与计时，不产生任何对象。
        /// </summary>
        private void HandleRespawned(CombatantHealth health)
        {
            if (health != null && _health != null && health != _health) return;
            RespawnReset();
        }

        public void Configure(BotConfigSO botConfig)
        {
            _baseConfig = botConfig;
            config = botConfig;
            _appliedDifficulty = int.MinValue;

            // 编辑期（ProjectScaffolder 装配场景）绝不能把运行时克隆写进序列化字段：
            // 只在 Play 模式按难度重建，构建场景时保持原始资产引用。
            if (Application.isPlaying)
                ApplyDifficulty(GameSettingsService.BotDifficulty);

            RefreshPathWeights();
        }

        /// <summary>
        /// 本局差异化武装（INKWAVE 式每局重抽）：换主武器、叠风格个性、发昵称。
        /// 武器经 <see cref="WeaponController.EquipWeapon"/> 走 OnWeaponEquipped，
        /// 角色外观（握持模型）与音频订阅方自动跟随；战术由 BotTactics 按 WeaponKind 自适应。
        /// 复活不换枪：只有 <see cref="BotLoadoutService"/> 在每局 Countdown 时调用。
        /// </summary>
        /// <param name="weaponConfig">目标武器；null 时保留现装（例如资源缺失的兜底）。</param>
        /// <param name="personality">风格个性倍率；恒等时行为与旧版一致。</param>
        /// <param name="displayName">本局昵称；空串时保留旧值。</param>
        public void AssignMatchLoadout(WeaponConfigSO weaponConfig, BotPersonality personality, string displayName)
        {
            if (!string.IsNullOrEmpty(displayName))
                DisplayName = displayName;

            if (weaponConfig != null && _weapon != null && _weapon.Config != weaponConfig)
                _weapon.EquipWeapon(weaponConfig);

            _personality = personality;

            if (Application.isPlaying)
            {
                // 强制重建生效配置：难度克隆之外再叠个性层（ApplyDifficulty 内组合）。
                _appliedDifficulty = int.MinValue;
                ApplyDifficulty(GameSettingsService.BotDifficulty);
            }
        }

        private void RefreshPathWeights()
        {
            _pathWeights = new InkPathWeights(
                config != null ? config.ownInkPathCost : 0.5f,
                config != null ? config.neutralPathCost : 1f,
                config != null ? config.enemyInkPathCost : 3f);
        }

        private void Update()
        {
            if (!_health.IsAlive)
            {
                State = BotState.Dead;
                _firingLatch = false;
                _moveMagnitude = 0f;
                _hasMoveTarget = false;
                _path.Clear();
                _motor.SetFrameIntent(Vector3.zero, AimDirection(), false, false);
                return;
            }

            if (State == BotState.Dead)
            {
                RespawnReset();
            }

            if (!MatchManager.CombatAllowed)
            {
                _motor.SetFrameIntent(Vector3.zero, AimDirection(), false, false);
                return;
            }

            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);
            _elapsed += dt;
            if (_bombTimer > 0f) _bombTimer -= dt;
            if (_specialRetryTimer > 0f) _specialRetryTimer -= dt;
            if (_retreatCooldown > 0f) _retreatCooldown -= dt;
            if (_threatMemoryTimer > 0f) _threatMemoryTimer -= dt;
            if (_reactionTimer > 0f) _reactionTimer -= dt;
            if (_chargerPaintPause > 0f) _chargerPaintPause -= dt;
            if (_pathRetryTimer > 0f) _pathRetryTimer -= dt;
            if (_paintPickRetryTimer > 0f) _paintPickRetryTimer -= dt;
            _acquireAge += dt;
            _paintSweepPhase += dt;

            UpdateStuckDetection(dt);

            _thinkTimer -= dt;
            if (_thinkTimer <= 0f)
            {
                _thinkTimer = PerceptionSeconds;
                UpdateState();
                if (!_health.IsAlive) return;
            }

            switch (State)
            {
                case BotState.Engage: TickEngage(dt); break;
                case BotState.Retreat: TickRetreat(dt); break;
                case BotState.Refill: TickRefill(dt); break;
                default: TickPaint(dt); break;
            }
        }

        private void RespawnReset()
        {
            State = BotState.Paint;
            ResetMovement();
            RefreshPathWeights();
            _aim.Reset(transform.eulerAngles.y);
            _aimPhaseA = Random.Range(0f, 20f);
            _aimPhaseB = Random.Range(0f, 20f);
            _acquireAge = 1f;
            _reactionTimer = 0f;
            _targetEnemy = null;
            _targetVelocity = Vector3.zero;
            _threatMemoryTimer = 0f;
            _retreatTimer = 0f;
            _retreatCooldown = 0f;
            _firingLatch = false;
            _moveMagnitude = 0f;
            _strafeBlend = 0f;
            _aheadNeedsPaint = true;

            // 重开比赛（MatchManager.ResetWorld → CombatantHealth.ResetImmediately）也必须回到干净状态：
            // 目标、计时、退避计数、弹药/大招重试与黑名单全部清零，避免第二局 Bot 直奔上一局的涂地点。
            _paintGoalTimer = 0f;
            _paintPickFailures = 0;
            _paintPickRetryTimer = 0f;
            _pathFailures = 0;
            _pathRetryTimer = 0f;
            _retreatPickFailures = 0;
            _retreatRepathTimer = 0f;
            _paintAheadTimer = 0f;
            _paintSweepPhase = Random.Range(0f, 10f);
            _chargerPaintPause = 0f;
            _bombTimer = 0f;
            _specialRetryTimer = 0f;
            _strafeTimer = 0f;
            _strafeDir = 1f;
            _strafeAmp = 1f;
            _refillSearchTimer = 0f;
            _thinkTimer = 0f;
            System.Array.Clear(_paintBlacklistUntil, 0, _paintBlacklistUntil.Length);
            _paintBlacklistCursor = 0;
        }

        // ---------- 状态决策 ----------

        private void UpdateState()
        {
            CombatantHealth enemy = FindNearestEnemy(out Vector3 enemyPos);
            TrackTarget(enemy, enemyPos);

            float hp = _health.CurrentHealth;
            float threatDistance = _threatMemoryTimer > 0f ? FlatDistance(_threatPosition) : float.MaxValue;

            if (State != BotState.Retreat && BotTactics.ShouldRetreat(hp, threatDistance, config, _retreatCooldown))
            {
                EnterRetreat();
                return;
            }

            // 撤退何时结束由 TickRetreat 判定（回满血 / 脱离威胁）
            if (State == BotState.Retreat) return;

            bool lowInk = _motor.CurrentInk <= (config != null ? config.refillInkThreshold : 18f);
            if (lowInk && State != BotState.Refill)
            {
                _refillSearchTimer = 0f;
                State = BotState.Refill;
                ResetMovement();
                return;
            }

            if (State == BotState.Refill &&
                _motor.CurrentInk < (config != null ? config.refillExitInk : 75f))
            {
                return;
            }

            State = enemy != null ? BotState.Engage : BotState.Paint;
        }

        private void TrackTarget(CombatantHealth enemy, Vector3 enemyPos)
        {
            if (enemy != _targetEnemy)
            {
                _targetEnemy = enemy;
                _firingLatch = false;

                if (enemy != null)
                {
                    _reactionTimer = (config != null ? config.reactionTime : 0.2f) * Random.Range(0.7f, 1.3f);
                    _acquireAge = 0f;
                    _acquireSign = (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.5f, 1f);
                    _prevTargetPos = enemyPos;
                    _targetVelocity = Vector3.zero;
                    _threatPosition = enemyPos;
                    _threatMemoryTimer = 2.5f;
                }
                return;
            }

            if (enemy == null) return;

            Vector3 velocity = (enemyPos - _prevTargetPos) / PerceptionSeconds;
            velocity.y = 0f;
            _targetVelocity = Vector3.Lerp(_targetVelocity, velocity, 0.5f);
            _prevTargetPos = enemyPos;
            _threatPosition = enemyPos;
            _threatMemoryTimer = 2.5f;
        }

        private CombatantHealth FindNearestEnemy(out Vector3 enemyPosition)
        {
            enemyPosition = default;
            if (_weapon == null || _health == null) return null;

            // SightRange 会被武器射程兜底（maxRange + margin），只缩放 sightRange 对大多数枪无效；
            // 难度通过 sightRangeScale 乘在最终结果上，才能真正缩短索敌距离（新手档更容易被绕后）。
            float sight = BotTactics.SightRange(_weapon.CurrentWeaponKind, _weapon.Config, config)
                          * (config != null ? Mathf.Clamp(config.sightRangeScale, 0.2f, 3f) : 1f);
            float sightSq = sight * sight;
            float stealthDetect = config != null ? Mathf.Max(0.5f, config.stealthDetectRange) : 2.5f;

            CombatantHealth best = null;
            float bestScore = float.MaxValue;

            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var candidate = all[i];
                if (candidate == null || candidate == _health || !candidate.IsAlive) continue;
                if (!candidate.Team.IsOpponentOf(_health.Team)) continue;

                Vector3 targetPos = candidate.transform.position;
                Vector3 to = targetPos - transform.position;
                to.y = 0f;
                float sq = to.sqrMagnitude;
                if (sq > sightSq) continue;

                // 潜行敌人：与 P2-A 的隐身规则一致，除非贴身否则看不见
                if (candidate.TryGetComponent(out InkCharacterMotor enemyMotor) &&
                    enemyMotor.IsStealthed && sq > stealthDetect * stealthDetect)
                {
                    continue;
                }

                if (!HasLineOfSight(candidate)) continue;

                // 粘性目标：当前目标享受 4m 的优先权，避免两名敌人之间反复横跳
                float score = Mathf.Sqrt(sq) - (candidate == _targetEnemy ? 4f : 0f);
                if (score >= bestScore) continue;

                bestScore = score;
                best = candidate;
                enemyPosition = targetPos;
            }

            return best;
        }

        private bool HasLineOfSight(CombatantHealth target)
        {
            Vector3 eye = transform.position + Vector3.up * 0.9f;
            Vector3 targetEye = target.transform.position + Vector3.up * 0.9f;
            if (!Physics.Linecast(eye, targetEye, out RaycastHit hit, GameLayers.QueryMask, QueryTriggerInteraction.Ignore))
                return true;

            return hit.collider.GetComponentInParent<CombatantHealth>() == target;
        }

        // ---------- 交战 ----------

        /// <summary>
        /// 追击目标：A* 成功就沿路径切入；A* 连续失败（目标被墙 / 缺口隔开）时不再沿直线硬冲，
        /// 改为原地横向拉枪保持射程，等退避时间到再重试寻路。
        /// </summary>
        private Vector3 ChaseOrHold(Vector3 goal, Vector3 directDir, float dt)
        {
            if (FollowPath(goal))
            {
                Vector3 steer = CurrentSteer();
                if (steer.sqrMagnitude > 0.001f) return steer;
            }

            // 只要这一轮 A* 失败过就不再沿直线硬冲（直冲正是「顶墙」的来源），
            // 改为横向拉枪保持射程，等退避结束再重试路径。
            return _pathFailures > 0 ? StrafeMove(directDir, dt) : directDir;
        }

        /// <summary>围绕目标方向横向拉枪（带换向节奏），用于保持射程而不是顶着墙推进。</summary>
        private Vector3 StrafeMove(Vector3 dir, float dt)
        {
            _strafeTimer -= dt;
            if (_strafeTimer <= 0f)
            {
                _strafeTimer = Random.Range(0.6f, 1.6f);
                _strafeDir = Random.value > 0.5f ? 1f : -1f;
                _strafeAmp = Random.Range(0.55f, 1f);
            }

            _strafeBlend = Mathf.Lerp(_strafeBlend, _strafeDir * _strafeAmp, 1f - Mathf.Exp(-5f * dt));
            return Vector3.Cross(Vector3.up, dir) * (_strafeBlend * 0.9f);
        }

        private void TickEngage(float dt)
        {
            if (_targetEnemy == null || !_targetEnemy.IsAlive)
            {
                State = BotState.Paint;
                _firingLatch = false;
                ResetMovement();
                return;
            }

            WeaponKind kind = _weapon.CurrentWeaponKind;
            WeaponConfigSO weaponConfig = _weapon.Config;

            Vector3 enemyPos = _targetEnemy.transform.position;
            Vector3 toEnemy = enemyPos - transform.position;
            toEnemy.y = 0f;
            float dist = toEnemy.magnitude;
            Vector3 dir = dist > 0.001f ? toEnemy / dist : transform.forward;

            float preferred = BotTactics.PreferredEngageDistance(kind, weaponConfig, config);
            float fireRange = BotTactics.MaxFireDistance(kind, weaponConfig, config);
            float engageRange = _firingLatch ? fireRange * 1.35f : fireRange;

            // ---- 瞄准：带提前量的理想角 + 漂移/拉枪误差 → 弹簧
            float lead = BotTactics.AimLeadSeconds(kind, weaponConfig, dist);
            Vector3 predicted = enemyPos + _targetVelocity * lead;
            Vector3 toPredicted = predicted - transform.position;
            toPredicted.y = 0f;
            float idealYaw = toPredicted.sqrMagnitude > 0.0001f
                ? HumanAimSpring.YawFromDirection(toPredicted)
                : HumanAimSpring.YawFromDirection(dir);

            float wanderSpeed = config != null ? config.aimWanderSpeed : 1.7f;
            float wander = Mathf.Sin(_elapsed * wanderSpeed + _aimPhaseA) * 0.6f
                           + Mathf.Sin(_elapsed * wanderSpeed * 1.34f + _aimPhaseB) * 0.4f;

            float reaction = config != null ? config.reactionTime : 0.2f;
            float acquireTau = Mathf.Max(0.12f, reaction * 0.9f);
            float acquire = Mathf.Exp(-_acquireAge / acquireTau);
            float aimError = config != null ? config.aimErrorDegrees : 5.5f;
            float wanderDegrees = config != null ? config.aimWanderDegrees : 1.2f;
            float overshoot = config != null ? config.aimAcquireOvershoot : 2.4f;

            // 慢漂移（长期不呆板）+ 发现目标时的大偏角按 reaction 指数收敛（拉枪过冲后修正）
            float wantYaw = idealYaw + wanderDegrees * wander + aimError * overshoot * acquire * _acquireSign;

            _aim.Step(wantYaw,
                config != null ? config.aimSpringOmega : 13f,
                config != null ? config.aimMaxTurnRateDegrees : 573f,
                1f, dt);
            Vector3 aimDir = AimDirection();

            float offAngle = Mathf.Abs(HumanAimSpring.ShortestDelta(_aim.AngleDegrees, idealYaw));
            float tolerance = BotTactics.AimToleranceDegrees(dist, config, _firingLatch);
            bool aimed = offAngle <= tolerance;
            bool reactionReady = _reactionTimer <= 0f;
            bool hasInk = _motor.CurrentInk > 1f;

            // ---- 开火决策（多武器适配）
            bool wantFire = false;
            switch (kind)
            {
                case WeaponKind.Charger:
                    if (_weapon.IsCharging)
                    {
                        // 蓄满即松手：本帧不注入扳机，LateUpdate 会走“松开 → 发射”分支
                        float release = config != null ? Mathf.Clamp01(config.chargerReleaseCharge) : 0.95f;
                        wantFire = _weapon.Charge01 < release;
                    }
                    else
                    {
                        wantFire = reactionReady && aimed && dist <= fireRange && hasInk;
                    }
                    break;

                case WeaponKind.Splatling:
                    if (_weapon.IsCharging)
                    {
                        // 加特林同样是“蓄满松手才扫射”，否则会一直端着不发射
                        float release = config != null ? Mathf.Clamp01(config.splatlingReleaseCharge) : 0.9f;
                        wantFire = _weapon.Charge01 < release;
                    }
                    else
                    {
                        // 扫射/冷却期间保持扳机：策略内部会忽略，冷却结束后自动重新起转
                        wantFire = reactionReady && aimed && dist <= fireRange && hasInk;
                    }
                    break;

                case WeaponKind.Roller:
                {
                    // 滚筒：贴近才推地碾压，转身阶段不浪费扳机
                    float rollerRange = config != null ? Mathf.Max(1.5f, config.rollerFireRange) : 5.5f;
                    wantFire = reactionReady && hasInk && dist <= rollerRange && (aimed || dist <= preferred + 1f);
                    break;
                }

                default:
                    wantFire = reactionReady && aimed && dist <= engageRange && hasInk;
                    break;
            }

            // ---- 移动：远则寻路切入（墨水加权），近则拉开，保持距离时平滑横向拉枪
            Vector3 move;
            if (kind == WeaponKind.Roller)
            {
                if (dist > preferred + 0.4f)
                    move = ChaseOrHold(enemyPos, dir, dt);
                else
                    move = dir;
            }
            else if (dist > preferred + 1.2f)
            {
                move = ChaseOrHold(enemyPos, dir, dt);
            }
            else if (dist < preferred - 1.5f)
            {
                move = -dir;
                _hasMoveTarget = false;
            }
            else
            {
                _hasMoveTarget = false;
                move = StrafeMove(dir, dt);
            }

            if (kind == WeaponKind.Charger && _weapon.IsCharging)
            {
                move *= 0.3f;   // 蓄力时脚步放慢，符合蓄力狙手感
            }
            else if (kind == WeaponKind.Dualies && _weapon.RollsRemaining > 0 && !_weapon.IsRollingDodge && !_weapon.IsTurretLocked)
            {
                // 双枪 Bot：近身受迫或对枪中有概率向移动方向战术翻滚并就地架枪
                if ((dist < preferred - 0.6f || (wantFire && Random.value < 0.04f)) && move.sqrMagnitude > 0.01f)
                {
                    _weapon.TryDodgeRoll(move);
                }
            }

            TryUseBombOrSpecial(dist);

            // 脱离射程但脚下是自家墨：切乌贼形态高速贴近（又快又难被打中）
            bool wantsSwim = !wantFire
                             && kind != WeaponKind.Roller
                             && !_weapon.IsCharging
                             && dist > fireRange * 1.15f
                             && _motor.IsInOwnInk;

            _firingLatch = wantFire;

            Vector3 smoothedMove = SmoothMove(move, dt);
            // 大招（Slam 升空）期间锁移动：与玩家 PlayerController 的 6 参用法一致。
            // 注意第 4 参是 wantsJump、第 6 参才是 lockedBySpecial —— 旧代码把 IsPerformingSpecial
            // 传进 wantsJump，导致 Bot 放大招时真的跳一下且仍能移动，冲击圈留在起跳点与身体错位。
            _motor.SetFrameIntent(smoothedMove, aimDir, wantsSwim, false, wantFire, _weapon.IsPerformingSpecial);

            if (wantFire)
            {
                float aimDistance = Mathf.Clamp(dist, 2f, Mathf.Max(2f, fireRange));
                _aimPoint = BotTactics.AimGroundPoint(transform.position, aimDir, aimDistance, predicted.y);
                _weapon.TryFireMainWeapon(aimDir, _aimPoint);
            }
        }

        private void TryUseBombOrSpecial(float distToEnemy)
        {
            if (_specialRetryTimer <= 0f && _weapon.IsSpecialReady)
            {
                int near = CountEnemiesWithin(config != null ? config.specialSearchRange : 7.5f);
                if (near >= (config != null ? config.specialMinEnemies : 1))
                {
                    if (_weapon.TryActivateSpecial())
                    {
                        _specialRetryTimer = 1f;
                        return;
                    }
                    _specialRetryTimer = 0.8f;
                }
            }

            if (_bombTimer > 0f) return;
            if (_targetEnemy == null) return;

            float minR = config != null ? config.bombRangeMin : 4.5f;
            float maxR = config != null ? config.bombRangeMax : 11f;
            float minInk = config != null ? config.bombMinInk : 60f;

            if (distToEnemy < minR || distToEnemy > maxR) return;
            if (_motor.CurrentInk < minInk) return;
            if (Random.value > (config != null ? config.bombUseChance : 0.55f)) return;

            if (_weapon.TryThrowSubBomb(_targetEnemy.transform.position))
            {
                _bombTimer = config != null ? config.bombCooldown : 5f;
            }
        }

        private int CountEnemiesWithin(float radius)
        {
            var all = CombatantHealthRegistry.All;
            int count = 0;
            float sq = radius * radius;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null || !c.IsAlive || !c.Team.IsOpponentOf(_health.Team)) continue;
                if ((c.transform.position - transform.position).sqrMagnitude <= sq) count++;
            }
            return count;
        }

        // ---------- 残血战术撤退 ----------

        private void EnterRetreat()
        {
            State = BotState.Retreat;
            float min = config != null ? config.retreatDurationMin : 2.2f;
            float max = config != null ? config.retreatDurationMax : 3.6f;
            _retreatTimer = Random.Range(min, Mathf.Max(min, max));
            _retreatRepathTimer = 0f;
            _retreatPickFailures = 0;
            _firingLatch = false;
            _strafeBlend = 0f;
            ResetMovement();
            if (PickRetreatPoint())
            {
                _retreatRepathTimer = config != null ? Mathf.Max(0.2f, config.retreatRepathInterval) : 0.9f;
            }
            else
            {
                // 进撤退就没点可去：先按背离威胁方向撤离，退避结束后再选
                _retreatPickFailures = 1;
                _retreatRepathTimer = BotRecoveryPolicy.RetryDelay(1);
            }
        }

        private void ExitRetreat()
        {
            ExitRetreat(true);
        }

        private void ExitRetreat(bool allowReentryCooldown)
        {
            State = BotState.Paint;
            if (allowReentryCooldown)
            {
                _retreatCooldown = config != null ? config.retreatReentryCooldown : 1.5f;
            }
            else
            {
                // 没回满血就超时脱战：不惩罚再入冷却，否则残血 Bot 下次遇敌无法立即撤退
                _retreatCooldown = 0f;
            }
            _retreatTimer = 0f;
            _firingLatch = false;
            _chargerPaintPause = 0f;
            _retreatPickFailures = 0;
            _retreatRepathTimer = 0f;
            ResetMovement();
        }

        private void TickRetreat(float dt)
        {
            _retreatTimer -= dt;

            float exitHealth = config != null ? config.retreatExitHealth : 75f;
            if (_health.CurrentHealth >= exitHealth)
            {
                ExitRetreat();
                return;
            }

            float triggerRange = config != null ? config.retreatTriggerRange : 14f;
            bool threatNearby = _threatMemoryTimer > 0f && FlatDistance(_threatPosition) <= triggerRange * 1.2f;

            if (_retreatTimer <= 0f)
            {
                if (!threatNearby)
                {
                    ExitRetreat(_health.CurrentHealth >= exitHealth);
                    return;
                }
                _retreatTimer = 1.2f;   // 还没脱离危险：继续边退边打
            }

            _retreatRepathTimer -= dt;
            // 有目标时按「到达 / 间隔」重选；没有目标时只认退避计时（旧条件 !_hasMoveTarget 会让
            // 选点失败后每帧重跑 28 次采样 + 半径 12 的可走性查询）。
            if (BotRecoveryPolicy.NeedsReselect(_hasMoveTarget, ReachedTarget(), _retreatRepathTimer, _retreatRepathTimer))
            {
                if (PickRetreatPoint())
                {
                    _retreatPickFailures = 0;
                    _retreatRepathTimer = config != null ? Mathf.Max(0.2f, config.retreatRepathInterval) : 0.9f;
                }
                else
                {
                    _retreatPickFailures++;
                    _retreatRepathTimer = BotRecoveryPolicy.RetryDelay(_retreatPickFailures);
                }
            }

            Vector3 steer;
            if (_hasMoveTarget && FollowPath(_moveTarget))
            {
                steer = CurrentSteer();
                if (steer.sqrMagnitude < 0.001f) steer = EscapeDirection();
            }
            else
            {
                if (_hasMoveTarget && BotRecoveryPolicy.ShouldAbandonTarget(_pathFailures))
                {
                    // 撤退点 A* 不可达：立刻放弃，改按背离威胁方向撤离，等退避计时结束再选点
                    _hasMoveTarget = false;
                }
                steer = EscapeDirection();
            }

            // 站在己方墨里就潜下去回血（60 HP/s），否则边跑边铺一条逃生墨道
            bool inOwnInk = _motor.IsInOwnInk;
            bool canPaintEscape = !inOwnInk
                                  && _weapon.CurrentWeaponKind != WeaponKind.Charger
                                  && _motor.CurrentInk > 3f;

            float idleOmega = config != null ? config.aimIdleSpringOmega : 8f;
            float idleRate = config != null ? config.aimIdleTurnRateDegrees : 344f;
            _aim.Step(HumanAimSpring.YawFromDirection(steer), idleOmega, idleRate, 1f, dt);
            Vector3 aimDir = AimDirection();

            Vector3 move = SmoothMove(steer, dt);
            // 第 4 参是 wantsJump、第 6 参才是 lockedBySpecial：大招升空期间锁移动，避免圈与身体错位
            _motor.SetFrameIntent(move, aimDir, inOwnInk, false, canPaintEscape, _weapon.IsPerformingSpecial);
            if (canPaintEscape)
            {
                _weapon.TryFireMainWeaponThrottled(aimDir);
            }
        }

        /// <summary>
        /// 没有可用路径时的撤离朝向：优先背离威胁记忆，记忆过期则保持当前朝向。
        /// 绝不沿用 transform.forward 盲目前冲（可能正对威胁）。
        /// </summary>
        private Vector3 EscapeDirection()
        {
            if (_threatMemoryTimer > 0f)
            {
                Vector3 away = transform.position - _threatPosition;
                away.y = 0f;
                if (away.sqrMagnitude > 0.001f) return away.normalized;
            }
            return transform.forward;
        }

        /// <summary>
        /// 选一个撤退点。返回 false 表示本轮没有可用点（调用方负责退避，不要每帧重试）。
        /// 有导航网格时会顺带做一次 A* 可达性校验：成功则直接把路径铺好，失败则该点不可用。
        /// </summary>
        private bool PickRetreatPoint()
        {
            Vector3 self = transform.position;
            InkGrid ink = ResolveInkGrid();
            Team myTeam = _health.Team;
            float radius = config != null ? Mathf.Max(4f, config.retreatSearchRadius) : 9f;
            bool hasThreat = _threatMemoryTimer > 0f;

            bool found = false;
            float bestScore = float.MinValue;
            Vector3 best = self;

            // 第一轮：只在己方墨水点里选（安全屋：潜行 + 高速回血）
            for (int i = 0; i < 16; i++)
            {
                Vector3 p = RandomRingPoint(self, 3f, radius);
                if (ink != null)
                {
                    if (ink.IsBlockedAt(p)) continue;
                    if (ink.InkAt(p) != myTeam) continue;
                }

                // 只接受「背离威胁」的候选点：远征到地图对角虽然离威胁更远，
                // 但路径会先穿过威胁，残血撤退反而送命。
                if (!IsRetreatDirectionAway(p, self)) continue;

                float score = 6f - Vector3.Distance(p, self) * 0.15f;
                if (hasThreat)
                {
                    float away = FlatDistanceTo(_threatPosition, p) - FlatDistanceTo(_threatPosition, self);
                    score += away * 0.8f;
                    if (!HasLineOfSightFrom(p)) score += 4f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                    found = true;
                }
            }

            // 第二轮：附近没有己方墨，就退向离威胁最远的点，边退边铺墨
            if (!found)
            {
                float bestAway = float.MinValue;
                for (int i = 0; i < 12; i++)
                {
                    Vector3 p = RandomRingPoint(self, 3f, radius);
                    if (ink != null && ink.IsBlockedAt(p)) continue;
                    if (!IsRetreatDirectionAway(p, self)) continue;

                    float away = hasThreat ? FlatDistanceTo(_threatPosition, p) : Random.value;
                    if (away <= bestAway) continue;
                    bestAway = away;
                    best = p;
                    found = true;
                }
            }

            var arena = ArenaNavigator.Instance;
            var nav = arena != null ? arena.Grid : null;
            if (found && nav != null && !nav.TryGetNearestWalkable(best, out best))
            {
                found = false;
            }

            // 吸附到可行走格后再次确认方向，避免越界点被夹到侧后方/对向。
            if (found && !IsRetreatDirectionAway(best, self))
            {
                found = false;
            }

            if (found)
            {
                Vector3 goal = new Vector3(best.x, self.y, best.z);

                // A* 可达性：撤退点落在墙后 / 缺口对面时，与其让 Bot 顶着墙冲，不如本轮判定无点，
                // 由调用方退避后重选（期间按背离威胁方向撤离）。
                if (nav != null && arena != null)
                {
                    if (!arena.TryFindPath(self, goal, _path, myTeam, _pathWeights))
                    {
                        _path.Clear();
                        found = false;
                    }
                    else
                    {
                        _pathIndex = 0;
                        _repathTimer = config != null ? config.repathInterval : 0.55f;
                    }
                }
            }

            if (!found)
            {
                _hasMoveTarget = false;
                _path.Clear();
                return false;
            }

            _moveTarget = new Vector3(best.x, self.y, best.z);
            _hasMoveTarget = true;
            _pathFailures = 0;
            _pathRetryTimer = 0f;
            _lastPathGoal = _moveTarget;
            _hasLastPathGoal = true;
            if (nav == null || arena == null)
            {
                // 没有导航服务：保留旧的「下一帧直线推进」行为
                _path.Clear();
                _pathIndex = 0;
                _repathTimer = 0f;
            }
            return true;
        }

        /// <summary>候选撤退点是否位于威胁的反方向（夹角 &lt;= 约 69°）。无威胁记忆时一律接受。</summary>
        private bool IsRetreatDirectionAway(Vector3 point, Vector3 self)
        {
            if (_threatMemoryTimer <= 0f)
                return true;

            Vector3 toPoint = new Vector3(point.x - self.x, 0f, point.z - self.z);
            Vector3 toThreat = new Vector3(_threatPosition.x - self.x, 0f, _threatPosition.z - self.z);
            if (toPoint.sqrMagnitude < 0.0001f || toThreat.sqrMagnitude < 0.0001f)
                return true;

            return Vector3.Dot(toPoint.normalized, toThreat.normalized) <= 0.35f;
        }

        private bool HasLineOfSightFrom(Vector3 from)
        {
            if (_threatMemoryTimer <= 0f) return false;

            Vector3 eye = from + Vector3.up * 0.9f;
            Vector3 threatEye = _threatPosition + Vector3.up * 0.9f;
            return !Physics.Linecast(eye, threatEye, out _, GameLayers.QueryMask, QueryTriggerInteraction.Ignore);
        }

        private static Vector3 RandomRingPoint(Vector3 center, float minRadius, float maxRadius)
        {
            return center + RandomRingOffset(minRadius, maxRadius);
        }

        private static Vector3 RandomRingOffset(float minRadius, float maxRadius)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(minRadius, maxRadius);
            return new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
        }

        // ---------- 巡涂 ----------

        private void TickPaint(float dt)
        {
            _paintGoalTimer -= dt;

            // 有目标时按「到达 / 目标过期」重选；没有目标时只认退避冷却。
            // 旧条件 `!_hasMoveTarget || ...` 会在选点失败（_hasMoveTarget=false）后每帧
            // 重跑 20 个候选点采样 + 可走性查询，把 1.5s 冷却彻底架空。
            if (BotRecoveryPolicy.NeedsReselect(_hasMoveTarget, ReachedTarget(), _paintGoalTimer, _paintPickRetryTimer))
            {
                if (PickPaintTarget())
                {
                    _paintPickFailures = 0;
                    _paintPickRetryTimer = 0f;
                }
                else
                {
                    _hasMoveTarget = false;
                    _paintGoalTimer = 0f;
                    _paintPickFailures++;
                    _paintPickRetryTimer = BotRecoveryPolicy.RetryDelay(_paintPickFailures);
                }
            }

            Vector3 steer;
            if (_hasMoveTarget)
            {
                if (FollowPath(_moveTarget))
                {
                    steer = CurrentSteer();
                    if (steer.sqrMagnitude < 0.001f) steer = SweepDirection();
                }
                else if (BotRecoveryPolicy.ShouldAbandonTarget(_pathFailures))
                {
                    // 目标被墙 / 缺口隔开：放弃它并进入选点退避，绝不沿直线顶着墙推进
                    _hasMoveTarget = false;
                    _paintGoalTimer = 0f;
                    _paintPickRetryTimer = Mathf.Max(_paintPickRetryTimer, BotRecoveryPolicy.RetryDelay(_paintPickFailures + 1));
                    steer = SweepDirection();
                }
                else
                {
                    // 单次寻路失败：等 A* 重试（路径已清空，期间用扫场方向慢慢转，不冲墙）
                    steer = SweepDirection();
                }
            }
            else
            {
                steer = SweepDirection();
            }

            _paintAheadTimer -= dt;
            if (_paintAheadTimer <= 0f)
            {
                _paintAheadTimer = config != null ? Mathf.Max(0.05f, config.paintAheadCheckInterval) : 0.3f;
                _aheadNeedsPaint = NeedsPaintAhead(steer);
            }

            // 前方已经涂好 + 站在自家墨上：收起武器潜行转场（沿己方墨道高速切入）
            bool travelFast = !_aheadNeedsPaint && PathRemainingDistance() > 4f && _motor.IsInOwnInk;

            WeaponKind kind = _weapon.CurrentWeaponKind;
            float sweep = Mathf.Sin(_paintSweepPhase * (config != null ? config.paintSweepSpeed : 1.6f))
                          * (config != null ? config.paintSweepDegrees : 26f);
            float wantYaw = HumanAimSpring.YawFromDirection(steer) + sweep;

            _aim.Step(wantYaw,
                config != null ? config.aimIdleSpringOmega : 8f,
                config != null ? config.aimIdleTurnRateDegrees : 344f,
                1f, dt);
            Vector3 aimDir = AimDirection();

            bool wantFire;
            bool wantsSwim;

            switch (kind)
            {
                case WeaponKind.Charger:
                {
                    // 蓄力狙巡涂：蓄到 70% 松手画一条墨线，短暂停顿后继续
                    float paintRelease = config != null ? Mathf.Clamp01(config.chargerPaintReleaseCharge) : 0.7f;
                    if (_weapon.IsCharging)
                    {
                        wantFire = _weapon.Charge01 < paintRelease;
                        if (!wantFire)
                        {
                            _chargerPaintPause = config != null ? Mathf.Max(0f, config.chargerPaintPause) : 0.35f;
                        }
                    }
                    else
                    {
                        wantFire = !travelFast && _aheadNeedsPaint && _chargerPaintPause <= 0f
                                   && _motor.CurrentInk > 25f;
                    }
                    wantsSwim = !wantFire && travelFast;
                    break;
                }

                case WeaponKind.Roller:
                    wantFire = !travelFast && _motor.CurrentInk > 6f;
                    wantsSwim = false;
                    break;

                case WeaponKind.Splatling:
                {
                    // 加特林巡涂同样是“蓄到阈值松手才喷墨带”，缺了这支会永远端着蓄力不发一发
                    float paintRelease = config != null ? Mathf.Clamp01(config.splatlingPaintReleaseCharge) : 0.6f;
                    if (_weapon.IsCharging)
                    {
                        wantFire = _weapon.Charge01 < paintRelease;
                        if (!wantFire)
                        {
                            _chargerPaintPause = config != null ? Mathf.Max(0f, config.chargerPaintPause) : 0.35f;
                        }
                    }
                    else
                    {
                        wantFire = !travelFast && _aheadNeedsPaint && _chargerPaintPause <= 0f
                                   && _motor.CurrentInk > 12f;
                    }
                    wantsSwim = !wantFire && travelFast;
                    break;
                }

                default:
                    wantFire = !travelFast && _aheadNeedsPaint && _motor.CurrentInk > 12f;
                    wantsSwim = travelFast;
                    break;
            }

            Vector3 move = SmoothMove(steer, dt);
            // 第 4 参是 wantsJump、第 6 参才是 lockedBySpecial：大招升空期间锁移动，避免圈与身体错位
            _motor.SetFrameIntent(move, aimDir, wantsSwim, false, wantFire, _weapon.IsPerformingSpecial);
            if (wantFire)
            {
                _weapon.TryFireMainWeaponThrottled(aimDir);
            }
        }

        /// <summary>无目标时的扫场朝向：缓慢绕圈继续涂地，避免站着不动。</summary>
        private Vector3 SweepDirection()
        {
            return new Vector3(Mathf.Sin(_elapsed * 0.7f + _paintSweepPhase), 0f,
                               Mathf.Cos(_elapsed * 0.7f + _paintSweepPhase));
        }

        private bool NeedsPaintAhead(Vector3 direction)
        {
            var surface = InkSurfaceController.Instance;
            if (surface == null || surface.Grid == null) return true;

            InkGrid grid = surface.Grid;
            Team myTeam = _health.Team;
            float range = config != null ? Mathf.Max(2f, config.paintAimRange) : 6f;

            int own = 0;
            for (int i = 1; i <= 3; i++)
            {
                Vector3 p = transform.position + direction * (range * i / 3f);
                if (grid.IsBlockedAt(p)) continue;
                if (grid.InkAt(p) == myTeam) own++;
            }

            return own < 2;
        }

        private bool ReachedTarget()
        {
            Vector3 d = _moveTarget - transform.position;
            d.y = 0f;
            float arrive = config != null ? config.arriveDistance : 1.3f;
            return d.sqrMagnitude <= arrive * arrive;
        }

        private bool PickPaintTarget()
        {
            var surface = InkSurfaceController.Instance;
            if (surface == null || surface.Grid == null) return false;

            InkGrid ink = surface.Grid;
            var arena = ArenaNavigator.Instance;
            var nav = arena != null ? arena.Grid : null;

            Vector3 myPos = transform.position;
            Team myTeam = _health.Team;
            float enemySideSign = myTeam == Team.TeamA ? 1f : -1f;
            int samples = config != null ? Mathf.Max(6, config.paintGoalSamples) : 20;

            bool found = false;
            float bestScore = float.MinValue;
            Vector3 best = myPos;

            for (int attempt = 0; attempt < samples; attempt++)
            {
                Vector3 p = myPos + RandomRingOffset(6f, 24f);
                if (ink.IsBlockedAt(p)) continue;

                Team cellTeam = ink.InkAt(p);
                if (cellTeam == myTeam) continue;   // 已经是自家墨，没必要再去

                Vector3 target = p;
                if (nav != null && !nav.TryGetNearestWalkable(target, out target)) continue;

                // 上一轮确认过「在墙后 / 缺口对面、A* 到不了」的区域，短期内不再选
                if (IsPaintBlacklisted(target)) continue;

                // P16-A: 过滤掉敌方出生禁入圈内 + 额外缓冲的候选目标，避免压家贴边抖动
                float avoidBuffer = config != null ? config.spawnZoneAvoidBufferMeters : 2f;
                if (SpawnZone.IsInEnemySpawnZone(target, myTeam, avoidBuffer)) continue;

                float dist = FlatDistance(target);
                if (dist < 4f || dist > 28f) continue;

                // 越靠近敌方半场越值得压上；敌方墨水比中性地面更值得抢回。
                // 半场深度直接取当前涂地网格的 Z 尺寸，地图加长时不需要再改魔法数字。
                float halfDepth = Mathf.Max(1f, ink.MapSize.y * 0.5f);
                float progress = Mathf.Clamp01((target.z * enemySideSign) / halfDepth);
                float value = cellTeam == Team.Neutral ? 1f : 1.6f;
                float score = value * 10f - dist * 0.25f + progress * 3f + Random.Range(0f, 2.5f);

                if (score <= bestScore) continue;
                bestScore = score;
                best = target;
                found = true;
            }

            if (!found)
            {
                _paintGoalTimer = 0f;
                return false;
            }

            Vector3 goal = new Vector3(best.x, myPos.y, best.z);

            // 可达性校验：只对最终候选跑一次 A*（对 20 个候选各跑一次会抖动），成功即把路径铺好。
            // 失败说明目标在墙后 / 缺口对面：拉黑这片区域并让调用方退避，避免 Bot 顶着墙冲。
            if (nav != null && arena != null)
            {
                bool startResolvable = nav.TryGetNearestWalkable(myPos, out _);
                if (startResolvable && !arena.TryFindPath(myPos, goal, _path, myTeam, _pathWeights))
                {
                    _path.Clear();
                    AddPaintBlacklist(goal);
                    _paintGoalTimer = 0f;
                    return false;
                }

                _pathIndex = 0;
                _repathTimer = startResolvable ? (config != null ? config.repathInterval : 0.55f) : 0f;
                if (!startResolvable) _path.Clear();
            }
            else
            {
                // 没有导航服务：保留旧的直线推进兜底
                _path.Clear();
                _pathIndex = 0;
                _repathTimer = 0f;
            }

            _moveTarget = goal;
            _hasMoveTarget = true;
            _paintGoalTimer = config != null ? Mathf.Max(1f, config.paintGoalRefresh) : 7f;
            _pathFailures = 0;
            _pathRetryTimer = 0f;
            _lastPathGoal = goal;
            _hasLastPathGoal = true;
            return true;
        }

        /// <summary>该点是否在「A* 确认不可达」的短期黑名单内（半径 4m 内视为同一片区域）。</summary>
        private bool IsPaintBlacklisted(Vector3 point)
        {
            for (int i = 0; i < _paintBlacklistUntil.Length; i++)
            {
                if (_paintBlacklistUntil[i] <= _elapsed) continue;
                if (FlatDistanceTo(_paintBlacklistPositions[i], point) <= PaintBlacklistRadius) return true;
            }
            return false;
        }

        private void AddPaintBlacklist(Vector3 point)
        {
            _paintBlacklistPositions[_paintBlacklistCursor] = point;
            _paintBlacklistUntil[_paintBlacklistCursor] = _elapsed + PaintBlacklistSeconds;
            _paintBlacklistCursor = (_paintBlacklistCursor + 1) % PaintBlacklistSlots;
        }

        // ---------- 补给 ----------

        private void TickRefill(float dt)
        {
            if (_motor.IsInOwnInk)
            {
                _hasMoveTarget = false;
                // 潜进己方墨原地补给：回墨 34/s、回血 60/s
                _motor.SetFrameIntent(Vector3.zero, AimDirection(), true, false);
                return;
            }

            _refillSearchTimer -= dt;
            if (_refillSearchTimer <= 0f || !_hasMoveTarget)
            {
                _refillSearchTimer = 0.6f;
                if (TryFindNearbyOwnInk(out Vector3 inkPos))
                {
                    _moveTarget = inkPos;
                    _hasMoveTarget = true;
                    _path.Clear();
                    _pathIndex = 0;
                    _repathTimer = 0f;
                }
                else
                {
                    // 附近没有己方墨水：切回涂地，先铺一条退路
                    State = BotState.Paint;
                    return;
                }
            }

            FollowPath(_moveTarget);
            Vector3 steer = CurrentSteer();
            Vector3 move = SmoothMove(steer, dt);
            _aim.Step(HumanAimSpring.YawFromDirection(steer),
                config != null ? config.aimIdleSpringOmega : 8f,
                config != null ? config.aimIdleTurnRateDegrees : 344f,
                1f, dt);
            _motor.SetFrameIntent(move, AimDirection(), false, false);
        }

        private bool TryFindNearbyOwnInk(out Vector3 result)
        {
            result = transform.position;
            InkGrid ink = ResolveInkGrid();
            if (ink == null) return false;

            Team myTeam = _health.Team;

            for (int attempt = 0; attempt < 80; attempt++)
            {
                Vector3 p = RandomRingPoint(transform.position, 2f, 14f);

                if (ink.InkAt(p) != myTeam) continue;
                if (ink.IsBlockedAt(p)) continue;

                result = p;
                return true;
            }

            return false;
        }

        // ---------- 移动朝向平滑 ----------

        /// <summary>
        /// 把移动指令当作「带惯性的人体」：朝向按角速度上限转向，速度指数逼近。
        /// 这样路点切换、横向拉枪换向时不会出现瞬时 180° 抽搐。
        /// </summary>
        private Vector3 SmoothMove(Vector3 desired, float dt)
        {
            float magnitude = Mathf.Clamp01(desired.magnitude);

            if (magnitude > 0.01f)
            {
                float desiredYaw = HumanAimSpring.YawFromDirection(desired);
                float delta = HumanAimSpring.ShortestDelta(_moveHeadingDegrees, desiredYaw);

                if (_moveMagnitude < 0.05f)
                {
                    _moveHeadingDegrees = desiredYaw;   // 起步直接对齐，避免第一帧原地画圈
                }
                else if (Mathf.Abs(delta) > 120f)
                {
                    _moveHeadingDegrees = desiredYaw;   // 急掉头：先刹住再反向
                    _moveMagnitude *= 0.35f;
                }
                else
                {
                    float rate = config != null ? Mathf.Max(60f, config.moveHeadingTurnRateDegrees) : 630f;
                    _moveHeadingDegrees += Mathf.Clamp(delta, -rate * dt, rate * dt);
                }
            }

            _moveMagnitude = Mathf.Lerp(_moveMagnitude, magnitude, 1f - Mathf.Exp(-14f * dt));
            return HumanAimSpring.DirectionFromYaw(_moveHeadingDegrees) * _moveMagnitude;
        }

        // ---------- 路径跟随 ----------

        /// <summary>
        /// 沿 A* 路径推进。关键约束：导航网格存在但寻路失败时，绝不能再塞入直线 goal
        /// （旧实现会让 Bot 顶着墙冲锋且卡死检测只清路径、下一帧又对同一目标重试）。
        /// 现在改为清空路径 + 按连续失败次数退避；只有完全没有导航服务时才保留直线兜底。
        /// </summary>
        private bool FollowPath(Vector3 goal)
        {
            // 退避期内不再打 A*（A* 全程 8 个 Bot 每帧重试是主要 GC/CPU 抖动源之一）
            if (_pathRetryTimer > 0f)
            {
                // 目标换成别处（新敌人 / 切回涂地）时重置重试预算：
                // 不能让上一个不可达目标的退避拖住新目标。
                if (!_hasLastPathGoal || FlatDistanceTo(_lastPathGoal, goal) > PathGoalChangedDistance)
                {
                    _pathRetryTimer = 0f;
                    _pathFailures = 0;
                }
                else
                {
                    return false;
                }
            }

            _lastPathGoal = goal;
            _hasLastPathGoal = true;

            _repathTimer -= Time.deltaTime;
            if (_repathTimer <= 0f || _path.Count == 0)
            {
                _repathTimer = config != null ? config.repathInterval : 0.55f;
                var arena = ArenaNavigator.Instance;

                if (arena == null || arena.Grid == null)
                {
                    // 没有导航服务（子场景 / 测试）：保留直线兜底，否则 Bot 完全不会移动
                    _path.Clear();
                    _path.Add(goal);
                    _pathIndex = 0;
                    _pathFailures = 0;
                    return true;
                }

                // 墨水加权：己方墨道 0.5 / 中性 1.0 / 敌方 3.0
                if (!arena.TryFindPath(transform.position, goal, _path, _health.Team, _pathWeights))
                {
                    _path.Clear();
                    _pathIndex = 0;
                    _pathFailures++;
                    _pathRetryTimer = BotRecoveryPolicy.RetryDelay(_pathFailures);
                    _repathTimer = _pathRetryTimer;
                    return false;
                }

                _pathFailures = 0;
                _pathIndex = 0;
            }

            if (_path.Count == 0) return false;

            while (_pathIndex < _path.Count)
            {
                Vector3 d = _path[_pathIndex] - transform.position;
                d.y = 0f;
                if (d.sqrMagnitude > 0.36f) break;
                _pathIndex++;
            }

            return _pathIndex < _path.Count;
        }

        private Vector3 CurrentSteer()
        {
            if (_path.Count == 0 || _pathIndex >= _path.Count)
                return Vector3.zero;

            Vector3 d = _path[_pathIndex] - transform.position;
            d.y = 0f;
            return d.sqrMagnitude > 1e-5f ? d.normalized : Vector3.zero;
        }

        private float PathRemainingDistance()
        {
            if (_path.Count == 0 || _pathIndex >= _path.Count) return 0f;

            Vector3 d = _path[_path.Count - 1] - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        private void ResetMovement()
        {
            _path.Clear();
            _pathIndex = 0;
            _hasMoveTarget = false;
            _repathTimer = 0f;
            _pathFailures = 0;
            _pathRetryTimer = 0f;
            _hasLastPathGoal = false;
            _stuckAnchor = transform.position;
            _stuckTimer = 0f;
        }

        private void UpdateStuckDetection(float dt)
        {
            // 补给时是「故意站着不动」，不是卡死；误判会污染寻路失败计数
            if (State == BotState.Refill)
            {
                _stuckTimer = 0f;
                _stuckAnchor = transform.position;
                return;
            }

            _stuckTimer += dt;
            float interval = config != null ? config.stuckCheckInterval : 1f;
            if (_stuckTimer < interval) return;

            float threshold = config != null ? config.stuckMoveThreshold : 0.35f;
            if (Vector3.Distance(transform.position, _stuckAnchor) < threshold)
            {
                _path.Clear();
                _pathIndex = 0;
                _hasMoveTarget = false;
                _repathTimer = 0f;

                // 卡死也算一次寻路失败：按同样的退避曲线拉长「再试同一处」的间隔，
                // 并让涂地/撤退重选目标前先等一小会，避免每帧对同一面墙冲锋。
                _pathFailures++;
                _pathRetryTimer = BotRecoveryPolicy.RetryDelay(_pathFailures);
                _paintPickRetryTimer = Mathf.Max(_paintPickRetryTimer, _pathRetryTimer);
                _retreatRepathTimer = 0f;
            }

            _stuckTimer = 0f;
            _stuckAnchor = transform.position;
        }

        // ---------- 工具 ----------

        private Vector3 AimDirection()
        {
            return HumanAimSpring.DirectionFromYaw(_aim.AngleDegrees);
        }

        private InkGrid ResolveInkGrid()
        {
            var surface = InkSurfaceController.Instance;
            return surface != null ? surface.Grid : null;
        }

        private float FlatDistance(Vector3 world)
        {
            return FlatDistanceTo(world, transform.position);
        }

        private static float FlatDistanceTo(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private void OnDrawGizmosSelected()
        {
            if (_path != null && _path.Count > 0)
            {
                Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.8f);
                for (int i = 0; i < _path.Count - 1; i++)
                {
                    Gizmos.DrawLine(_path[i] + Vector3.up * 0.1f, _path[i + 1] + Vector3.up * 0.1f);
                }
            }

            Gizmos.color = new Color(0.2f, 1f, 1f, 0.9f);
            Gizmos.DrawLine(transform.position + Vector3.up * 1.1f,
                transform.position + Vector3.up * 1.1f + AimDirection() * 3f);

            if (_aimPoint.sqrMagnitude > 0.0001f)
            {
                Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.9f);
                Gizmos.DrawWireSphere(_aimPoint, 0.25f);
            }
        }
    }

    /// <summary>
    /// Bot 寻路 / 选点失败恢复策略。全部为纯函数，供 <see cref="BotController"/> 调用，
    /// 并可在 EditMode 直接验算（不依赖场景对象）。
    /// 退避曲线：0.75 → 1.5 → 3 → 6（封顶），连续失败越多次越久才重新发起同类查询。
    /// </summary>
    public static class BotRecoveryPolicy
    {
        /// <summary>失败后的最短重试间隔（秒）。</summary>
        public const float MinRetryDelay = 0.75f;

        /// <summary>失败退避上限（秒）。</summary>
        public const float MaxRetryDelay = 6f;

        /// <summary>连续寻路失败多少次后放弃当前目标（换目标 / 退回涂地 / 改为拉扯）。</summary>
        public const int AbandonAfterFailures = 2;

        /// <summary>
        /// 连续失败 <paramref name="consecutiveFailures"/> 次后的重试间隔。
        /// 返回 0 表示尚未失败（不启用退避，按正常重规划间隔工作）。
        /// </summary>
        public static float RetryDelay(int consecutiveFailures)
        {
            if (consecutiveFailures <= 0) return 0f;

            int shift = Mathf.Clamp(consecutiveFailures - 1, 0, 4);
            return Mathf.Min(MaxRetryDelay, MinRetryDelay * (1 << shift));
        }

        /// <summary>
        /// 是否需要重新挑选目标。
        /// 有目标：到达或计时过期即重选；没有目标：只认重试冷却。
        /// 「无目标」绕过冷却正是旧实现每帧重采样 20 个候选点的根因。
        /// </summary>
        public static bool NeedsReselect(bool hasTarget, bool reached, float intervalTimer, float retryTimer)
        {
            if (hasTarget) return reached || intervalTimer <= 0f;
            return retryTimer <= 0f;
        }

        /// <summary>连续寻路失败是否已达到「放弃当前目标」的阈值。</summary>
        public static bool ShouldAbandonTarget(int consecutivePathFailures)
        {
            return consecutivePathFailures >= AbandonAfterFailures;
        }
    }
}
