using System;
using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.UI;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 玩家、Bot 与练习木桩靶共用的生命值、受击闪烁、击杀爆墨与复活组件。
    /// </summary>
    public class CombatantHealth : MonoBehaviour
    {
        [SerializeField] private Team team = Team.TeamA;
        [SerializeField] private PlayerConfigSO config;
        [SerializeField] private Renderer[] bodyRenderers;
        [SerializeField] private Transform respawnAnchor;

        private float _currentHealth = 100f;
        private float _maxHealthScale = 1f;
        private float _respawnTimer;
        private float _hitFlashTimer;
        private bool _externalInvulnerable;
        private float _spawnProtectionRemaining;
        private float _spawnBlinkTimer;
        private bool _spawnBlinkOn;
        private Vector3 _initialSpawnPos;
        private Quaternion _initialSpawnRot;
        private CharacterController _cc;
        private Collider[] _colliders;
        private MaterialPropertyBlock _propBlock;
        private CombatantHealth _lastAttacker;
        private string _lastWeaponCause;
        private InkCharacterMotor _motorCache;
        private bool _motorQueried;
        private float _lastHitSplashTime = -999f;

        // P6-B 命中墨花门控：同一单位 50ms 内多次命中只炸一片（霰弹/连发聚合）。
        // P30-C2：距离门控从 40m 放宽到 60m——竞技场对角线约 68m，旧值把中远距离交火的命中反馈全吞掉了；
        // 被节流合并掉的命中改为「最小化墨花」，不再静默丢弃。
        private const float HitSplashMinInterval = 0.05f;
        private const float HitSplashMaxDistanceSqr = 3600f;
        private const float HitSplashChestHeight = 0.9f;
        private const float HitSplashDamageRefFallback = 60f;
        /// <summary>被节流合并时的最小化墨花强度（单颗小粒子，约为完整墨花的 0.8 倍尺寸基准）。</summary>
        private const float HitSplashMinimalScale = 0.8f;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");

        public Team Team => team;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => (config != null ? config.maxHealth : 100f) * _maxHealthScale;
        public float MaxHealthScale => _maxHealthScale;
        public float Health01 => Mathf.Clamp01(_currentHealth / MaxHealth);
        public bool IsAlive { get; private set; } = true;

        /// <summary>
        /// 设置最大生命倍率（低难度给本地玩家的容错补偿）。只应由 PlayerController 调用；
        /// Bot / 队友保持 1.0。已受伤时按当前血量比例缩放，切难度时血条百分比不跳变。
        /// </summary>
        public void SetMaxHealthScale(float scale)
        {
            scale = PlayerDifficultyMath.ClampMaxHealthScale(scale);
            if (Mathf.Approximately(_maxHealthScale, scale))
                return;

            float oldMax = MaxHealth;
            _maxHealthScale = scale;
            float newMax = MaxHealth;

            if (IsAlive && oldMax > 0.01f)
                _currentHealth = Mathf.Clamp(_currentHealth * (newMax / oldMax), 0f, newMax);
        }

        /// <summary>
        /// 是否免疫伤害：外部（大招等）无敌与复活出生保护取并集。
        /// setter 只控制外部无敌；出生保护由 <see cref="GrantSpawnProtection"/> 独立计时，
        /// 因此 <c>IsInvulnerable = false</c> 不会误清除刚获得的复活保护。
        /// </summary>
        public bool IsInvulnerable
        {
            get => _externalInvulnerable || _spawnProtectionRemaining > 0f;
            set => _externalInvulnerable = value;
        }

        /// <summary>复活后是否处于出生保护期。</summary>
        public bool IsSpawnProtected => _spawnProtectionRemaining > 0f;

        /// <summary>出生保护剩余秒数（HUD / 调试可读）。</summary>
        public float SpawnProtectionRemaining => _spawnProtectionRemaining;

        /// <summary>出生保护总时长（来自 PlayerConfigSO，默认 3s）。</summary>
        public float SpawnProtectionDuration => config != null ? Mathf.Max(0f, config.respawnInvulnerabilitySeconds) : 3f;

        public float RespawnRemaining => _respawnTimer;

        /// <summary>挂载在该单位上的机动组件（懒缓存，null 结果也标记已查，避免逐帧重复查询）。</summary>
        public InkCharacterMotor Motor
        {
            get
            {
                if (!_motorQueried)
                {
                    _motorCache = GetComponent<InkCharacterMotor>();
                    _motorQueried = true;
                }
                return _motorCache;
            }
        }

        /// <summary>该战斗单位的配置（打击反馈参数等，HUD 可只读访问）。</summary>
        public PlayerConfigSO Config => config;

        /// <summary>最近一次造成伤害的攻击者（可能为 null）。</summary>
        public CombatantHealth LastAttacker => _lastAttacker;

        /// <summary>最近一次伤害来源的武器/技能描述（击杀卡片使用）。</summary>
        public string LastWeaponCause => _lastWeaponCause;

        /// <summary>该单位配置的出生锚点（未配置时为 null，越界回收等外部系统只读使用）。</summary>
        public Transform RespawnAnchor => respawnAnchor;

        /// <summary>
        /// 读取该单位自己的出生位姿（越界回收优先用它，让多人同时越界各自回到自己的出生点，
        /// 而不是全部叠在队伍兜底出生线上）。
        /// 只认显式配置的 <see cref="respawnAnchor"/>；运行时临时拼装的单位（EditMode / PlayMode 测试、
        /// 练习木桩）没有锚点，返回 false 由调用方走队伍兜底，避免把"创建时所在位置"当成出生点。
        /// </summary>
        public bool TryGetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            if (respawnAnchor != null)
            {
                position = respawnAnchor.position;
                rotation = respawnAnchor.rotation;
                return true;
            }

            position = default;
            rotation = default;
            return false;
        }

        public event Action<CombatantHealth, Team> OnKilled;
        public event Action<CombatantHealth> OnRespawned;

        /// <summary>
        /// 任意战斗单位受到有效伤害后触发：(victim, attacker, damage, killed)。
        /// P6-A 的打击标记、受击方向弧与连击音效统一由此消费；attacker 可能为 null（环境伤害）。
        /// </summary>
        public static event Action<CombatantHealth, CombatantHealth, float, bool> DamageApplied;

        /// <summary>判断某个战斗单位是否是本地玩家（供打击反馈/击杀归属使用）。</summary>
        public static bool IsLocalPlayer(CombatantHealth combatant)
        {
            if (combatant == null)
                return false;

            PlayerController player = PlayerController.LocalPlayer;
            return player != null && player.Health == combatant;
        }

        private void Awake()
        {
            _cc = GetComponent<CharacterController>();
            _colliders = GetComponentsInChildren<Collider>(true);

            // 角色层自标记：即使启动扫描（PhysicsLayerBootstrap）先于本组件执行，也不会漏掉角色胶囊。
            if (GameLayers.CombatantLayer >= 0)
            {
                gameObject.layer = GameLayers.CombatantLayer;
                for (int i = 0; i < _colliders.Length; i++)
                {
                    if (_colliders[i] != null)
                        _colliders[i].gameObject.layer = GameLayers.CombatantLayer;
                }
            }

            _propBlock = new MaterialPropertyBlock();
            _initialSpawnPos = transform.position;
            _initialSpawnRot = transform.rotation;
            _currentHealth = MaxHealth;

            if (bodyRenderers == null || bodyRenderers.Length == 0)
            {
                Renderer[] all = GetComponentsInChildren<Renderer>(true);

                // 场景烘焙的角色可能已做刚体部件合并（源 renderer 被禁用、MergedStatic_* 接管渲染）：
                // 此时只收集仍启用的 renderer，避免死亡/复活的 SetVisualsAndCollidersActive 把源部件重新打开。
                bool hasMergedArtifacts = false;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] != null && all[i].gameObject.name.StartsWith("MergedStatic_"))
                    {
                        hasMergedArtifacts = true;
                        break;
                    }
                }

                if (!hasMergedArtifacts)
                {
                    bodyRenderers = all;
                }
                else
                {
                    var enabledOnly = new List<Renderer>(all.Length);
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] != null && all[i].enabled)
                            enabledOnly.Add(all[i]);
                    }
                    bodyRenderers = enabledOnly.ToArray();
                }
            }
        }

        private void OnEnable()
        {
            CombatantHealthRegistry.Register(this);
        }

        private void OnDisable()
        {
            CombatantHealthRegistry.Unregister(this);
        }

        public void Configure(Team assignedTeam, PlayerConfigSO playerConfig, Transform spawnPoint = null)
        {
            team = assignedTeam;
            config = playerConfig;
            respawnAnchor = spawnPoint;
            if (spawnPoint != null)
            {
                _initialSpawnPos = spawnPoint.position;
                _initialSpawnRot = spawnPoint.rotation;
            }
            else
            {
                _initialSpawnPos = transform.position;
                _initialSpawnRot = transform.rotation;
            }

            ResetImmediately();
        }

        public void ResetImmediately()
        {
            IsAlive = true;
            IsInvulnerable = false;
            // 出生保护只在"死亡后复活"路径授予；开局站位与重开比赛不白送保护（对齐 INKWAVE）。
            _spawnProtectionRemaining = 0f;
            _spawnBlinkTimer = 0f;
            _spawnBlinkOn = false;
            _currentHealth = MaxHealth;
            _respawnTimer = 0f;
            _hitFlashTimer = 0f;

            Vector3 targetPos = respawnAnchor != null ? respawnAnchor.position : _initialSpawnPos;
            Quaternion targetRot = respawnAnchor != null ? respawnAnchor.rotation : _initialSpawnRot;

            if (_cc != null) _cc.enabled = false;
            transform.SetPositionAndRotation(targetPos, targetRot);
            if (_cc != null) _cc.enabled = true;

            SetVisualsAndCollidersActive(true);
            ClearHitFlash();
            _lastAttacker = null;
            _lastWeaponCause = null;
            OnRespawned?.Invoke(this);
        }

        /// <summary>
        /// 授予出生保护（秒）。复活路径调用；已在保护中时取较长的剩余时间。
        /// 保护期间 <see cref="TakeDamage"/> 与敌方墨水灼烧均不结算，身体按 6Hz 闪白提示。
        /// </summary>
        public void GrantSpawnProtection(float seconds)
        {
            if (!IsAlive || seconds <= 0f)
                return;

            _spawnProtectionRemaining = Mathf.Max(_spawnProtectionRemaining, seconds);
            _spawnBlinkTimer = 0f;
            _spawnBlinkOn = false;
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
                return;

            _currentHealth = Mathf.Min(MaxHealth, _currentHealth + amount);
        }

        /// <summary>
        /// 敌方墨水灼烧伤害（带最低血量保底，不直接毒死）。
        /// </summary>
        public void ApplyEnemyInkBurn(float damageAmount, float minFloorHealth)
        {
            if (!IsAlive || IsInvulnerable || damageAmount <= 0f)
                return;

            if (_currentHealth > minFloorHealth)
            {
                _currentHealth = Mathf.Max(minFloorHealth, _currentHealth - damageAmount);
            }
        }

        public bool TakeDamage(float damage, Team attackerTeam, Vector3 hitPoint)
        {
            return TakeDamage(damage, attackerTeam, hitPoint, null, null);
        }

        public bool TakeDamage(float damage, Team attackerTeam, Vector3 hitPoint, CombatantHealth attacker, string weaponCause = null)
        {
            if (!IsAlive || IsInvulnerable || damage <= 0f)
                return false;

            if (attackerTeam != Team.Neutral && !attackerTeam.IsOpponentOf(team))
                return false;

            if (attacker != null) _lastAttacker = attacker;
            if (!string.IsNullOrEmpty(weaponCause)) _lastWeaponCause = weaponCause;

            _currentHealth = Mathf.Max(0f, _currentHealth - damage);
            _hitFlashTimer = config != null && config.hitFlashSeconds > 0.01f ? config.hitFlashSeconds : 0.09f;
            ApplyHitFlash(true);

            // P6-B 命中反馈统一入口：受击点方向性墨花 + 墨拍身体音。
            // 所有武器（弹丸/光束/滚筒/爆破/手雷/大招/风暴）都经 TakeDamage，因此一处接入全体生效。
            if (_currentHealth > 0f)
            {
                SpawnHitSplashFeedback(damage, attackerTeam, attacker, hitPoint);

                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayInkHitBody(transform.position, damage);
                }
            }

            bool killed = _currentHealth <= 0f;
            if (killed)
            {
                Die(attackerTeam);
            }

            ApplyImpactFeedback(damage, attacker, killed);

            // 本地玩家受击播放 hurt 音效（带节流）
            if (IsLocalPlayer(this) && !killed && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayHurt();
            }

            // 无 HUD 订阅者时保底播放命中音；有 HUD 时由 HUD 播放带连击音高变化的版本
            if (attacker != null && IsLocalPlayer(attacker) && DamageApplied == null && AudioManager.Instance != null)
            {
                AudioManager.Instance.PlayHitEnemy();
            }

            DamageApplied?.Invoke(this, attacker, damage, killed);
            return killed;
        }

        /// <summary>
        /// P6-A 打击反馈：
        /// 1. 自身受击按伤害量精准附加 0.3~1.0 Camera Trauma；
        /// 2. 本地玩家击杀敌方瞬间触发 0.05s 时间微缩（Hitstop），随后 0.1s 弹簧恢复。
        /// </summary>
        private void ApplyImpactFeedback(float damage, CombatantHealth attacker, bool killed)
        {
            PlayerConfigSO feel = config;

            if (IsLocalPlayer(this) && TopDownCameraFollow.Instance != null)
            {
                float minTrauma = feel != null ? feel.localHitTraumaMin : 0.3f;
                float maxTrauma = feel != null ? feel.localHitTraumaMax : 1f;
                float damageRatio = Mathf.Clamp01(damage / Mathf.Max(1f, MaxHealth));
                TopDownCameraFollow.Instance.AddTrauma(Mathf.Lerp(minTrauma, maxTrauma, damageRatio));
            }

            if (!killed || attacker == null || !IsLocalPlayer(attacker))
                return;

            // P6-B 击杀确认镜头冲击：trauma 震屏（受无障碍滑杆门控）+ FOV 微踢，与 Hitstop 定格叠加成「入肉感」
            if (TopDownCameraFollow.Instance != null)
            {
                float killTrauma = feel != null ? feel.killTrauma : 0.35f;
                float killFovPunch = feel != null ? feel.killFovPunchDegrees : 2.2f;
                TopDownCameraFollow.Instance.AddTrauma(killTrauma);
                if (killFovPunch > 0.01f)
                {
                    TopDownCameraFollow.Instance.AddShotPunch(0f, 0f, killFovPunch, 0.18f);
                }
            }

            HitstopService.Pulse(
                feel != null ? feel.hitstopHoldSeconds : HitstopService.DefaultHoldSeconds,
                feel != null ? feel.hitstopTimeScale : HitstopService.DefaultTimeScale,
                feel != null ? feel.hitstopRecoverSeconds : HitstopService.DefaultRecoverSeconds);
        }

        /// <summary>
        /// P6-B 命中墨花（对齐 Inkwave hitSplash）：从受击者胸口沿弹道方向炸开一小片墨水，
        /// 强度随伤害缩放；0.05s 节流 + 60m 距离门控，避免 8 人混战刷爆粒子池。
        /// P30-C2：距离门控放宽到 60m；被节流吞掉的命中降级为「最小化墨花」（单颗小粒子），
        /// 连射时每一下仍有落点读数，但不会把完整特效路径（主喷 + 回溅 + 卫星珠 + 高光）刷成峰值。
        /// hitPoint 被丢弃的旧问题顺带修正：无 attacker 时用「受击点 → 身体中心」的反向近似弹道。
        /// </summary>
        private void SpawnHitSplashFeedback(float damage, Team attackerTeam, CombatantHealth attacker, Vector3 hitPoint)
        {
            if (VfxPool.Instance == null)
                return;

            Vector3 origin = transform.position + Vector3.up * HitSplashChestHeight;
            if (TopDownCameraFollow.Instance != null &&
                (origin - TopDownCameraFollow.Instance.transform.position).sqrMagnitude > HitSplashMaxDistanceSqr)
            {
                return;
            }

            Vector3 shotDir = attacker != null
                ? transform.position - attacker.transform.position
                : transform.position - hitPoint;
            shotDir.y = 0f;
            shotDir = shotDir.sqrMagnitude > 0.0001f ? shotDir.normalized : Vector3.forward;

            Color color = InkSurfaceController.Instance != null
                ? InkSurfaceController.Instance.GetTeamColor(attackerTeam)
                : Color.white;

            // P30-C2：先判断是否被 0.05s 节流合并；被合并的命中不再静默丢弃，而是补一颗最小墨花。
            float now = Time.time;
            if (now - _lastHitSplashTime < HitSplashMinInterval)
            {
                VfxPool.Instance.SpawnHitSpeck(origin, shotDir, color, HitSplashMinimalScale);
                return;
            }
            _lastHitSplashTime = now;

            float damageRef = config != null && config.hitSplashDamageRef > 1f
                ? config.hitSplashDamageRef
                : HitSplashDamageRefFallback;

            // 白热高光只保留给「本地玩家打中」或「本地玩家被打中」：
            // SpawnImpactFlash 是加法混合的软圆盘，8 人混战里全员命中都放的话，
            // 每个角色胸口都会烧成一团白光（再被 Bloom 放大成整片白），
            // 与 VfxPool.SpawnImpactFlash 注释里「只给本地玩家的命中用，避免全屏闪光污染」一致。
            // P30-C2 起该高光的队色/alpha 已压暗，保留特效但不再过曝。
            bool highlight = IsLocalPlayer(this) || (attacker != null && IsLocalPlayer(attacker));
            VfxPool.Instance.SpawnHitSplash(origin, shotDir, color, damage / damageRef, highlight, attackerTeam);
        }

        private void Die(Team killerTeam)
        {
            IsAlive = false;
            _externalInvulnerable = false;
            _spawnProtectionRemaining = 0f;
            _spawnBlinkOn = false;
            _respawnTimer = config != null ? config.respawnDelay : 4.0f;
            float deathSplatRadius = config != null ? config.deathExplosionSplatRadius : 1.8f;

            // 击杀爆墨：被击败瞬间在原地爆出击杀方的大墨水斑！
            Team splatTeam = killerTeam != Team.Neutral ? killerTeam : team.GetOpponent();
            if (InkSurfaceController.Instance != null)
            {
                InkSurfaceController.Instance.PaintSphereWithWalls(transform.position, deathSplatRadius, splatTeam);
                Color c = InkSurfaceController.Instance.GetTeamColor(splatTeam);
                if (VfxPool.Instance != null)
                {
                    VfxPool.Instance.SpawnInkSplat(transform.position, c, deathSplatRadius, 22);
                    VfxPool.Instance.SpawnShockwaveRing(transform.position, c, 0.3f, deathSplatRadius, 0.26f);

                    // P6-B 击杀爆散加强：方向性贯穿爆散 + 延迟第二层冲击环 + 墨魂上升
                    Vector3 killDir = _lastAttacker != null
                        ? transform.position - _lastAttacker.transform.position
                        : Vector3.forward;
                    killDir.y = 0f;
                    killDir = killDir.sqrMagnitude > 0.0001f ? killDir.normalized : Vector3.forward;
                    VfxPool.Instance.SpawnKillBurst(transform.position, killDir, c, deathSplatRadius, splatTeam);
                }
            }

            // P6-B 击杀音路由修正：按「谁杀的/谁死了」而不是队伍编号——
            // 只有本地玩家击杀才给确认音（旧逻辑队友 Bot 击杀也会响，稀释了奖励感）；
            // 本地玩家阵亡播死亡音（内部含 BGM duck），队友阵亡给低音量提示，远处混战静默。
            if (AudioManager.Instance != null)
            {
                if (_lastAttacker != null && IsLocalPlayer(_lastAttacker))
                {
                    KillFeedbackState.StreakWindowSeconds = config != null
                        ? config.killStreakWindowSeconds
                        : 4f;
                    AudioManager.Instance.PlayKill(KillFeedbackState.RegisterLocalKill());
                }
                else if (IsLocalPlayer(this))
                {
                    AudioManager.Instance.PlayDeath();
                }
                else
                {
                    PlayerController localPlayer = PlayerController.LocalPlayer;
                    if (localPlayer != null && localPlayer.Health != null && team == localPlayer.Health.Team)
                    {
                        AudioManager.Instance.PlayAllyDeath();
                    }
                }
            }

            ClearHitFlash();
            SetVisualsAndCollidersActive(false);

            // 击杀奖励的转移量必须在 OnKilled 之前捕获：订阅方的 WeaponController.HandleKilled 会把
            // 未满大招能量减半，减半前的值才是「本次阵亡损失、应转给击杀者」的完整口径（KillRewardMath）。
            float victimPreDeathSpecialPoints = 0f;
            P1.Weapons.WeaponController victimWeapon = GetComponent<P1.Weapons.WeaponController>();
            if (victimWeapon != null)
            {
                victimPreDeathSpecialPoints = victimWeapon.SpecialPoints;
            }

            OnKilled?.Invoke(this, splatTeam);
            KillFeedEventHub.ReportCombatantDeath(this, splatTeam, _lastAttacker, _lastWeaponCause);

            // 击杀奖励（固定能量 + 损失转移 + 一次性回墨）：环境死亡等无攻击者情形由服务内部跳过。
            KillRewardService.AwardKill(_lastAttacker, this, victimPreDeathSpecialPoints);

            _lastAttacker = null;
            _lastWeaponCause = null;
        }

        private void Update()
        {
            if (_hitFlashTimer > 0f)
            {
                _hitFlashTimer -= Time.deltaTime;
                if (_hitFlashTimer <= 0f)
                {
                    ClearHitFlash();
                }
            }

            if (_spawnProtectionRemaining > 0f)
            {
                _spawnProtectionRemaining = Mathf.Max(0f, _spawnProtectionRemaining - Time.deltaTime);
                if (_spawnProtectionRemaining <= 0f)
                {
                    // 6Hz 复活闪白已移除（用户反馈：满场角色乱闪白光，纯白身体再被 bloom
                    // 放大成大团白晕，且发生在无人受击时，看起来像 bug）。
                    // 出生保护的无敌时间与脚下的队色扩散环保留，身体不再闪白。
                    _spawnBlinkOn = false;
                    if (_hitFlashTimer <= 0f)
                    {
                        ClearHitFlash();
                    }
                }
            }

            if (!IsAlive)
            {
                // 复活计时只在「比赛允许战斗」时推进（2026-11-01 修复）：
                // 旧实现在终场定格 / 结算界面里照样倒计时，结算后角色还会在出生点复活、
                // 播出生保护 VFX/音效，并让相机因瞬移触发 SnapImmediately 硬切。
                if (IsRespawnClockRunning())
                {
                    _respawnTimer -= Time.deltaTime;
                    if (_respawnTimer <= 0f)
                    {
                        ResetImmediately();
                        GrantSpawnProtection(SpawnProtectionDuration);
                        PlaySpawnProtectionVfx();
                        if (AudioManager.Instance != null && team == Team.TeamA)
                        {
                            AudioManager.Instance.PlayRespawn();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 复活倒计时是否推进：Playing（比赛进行中）推进；Countdown（3-2-1）、Finished（终场定格）、
        /// Result（结算）冻结。
        /// Ready（主菜单 / 只加载场景尚未开赛的回归用例）与「场景内没有 MatchManager」
        /// （EditMode 与独立 PlayMode 测试）沿用旧行为：这些场景没有战斗，复活没有副作用。
        /// </summary>
        private static bool IsRespawnClockRunning()
        {
            MatchManager match = MatchManager.Instance;
            if (match == null)
                return true;

            MatchManager.Phase phase = match.CurrentPhase;
            return phase == MatchManager.Phase.Playing || phase == MatchManager.Phase.Ready;
        }

        /// <summary>复活瞬间在脚下放一圈队色扩散环，和身体闪白一起提示"出生保护中"。</summary>
        private void PlaySpawnProtectionVfx()
        {
            if (VfxPool.Instance == null || InkSurfaceController.Instance == null)
                return;

            Color color = InkSurfaceController.Instance.GetTeamColor(team);
            VfxPool.Instance.SpawnShockwaveRing(
                transform.position + Vector3.up * 0.12f, color, 0.45f, 1.8f, 0.42f);
        }

        /// <summary>
        /// 角色刚体部件运行时合并后同步 bodyRenderers：移除被合并的源 renderer、加入合并 renderer，
        /// 保证死亡/复活显隐与受击白闪继续作用于实际渲染的网格。
        /// </summary>
        public void ApplyMergedBodyRenderers(IReadOnlyList<Renderer> mergedSources, IReadOnlyList<Renderer> mergedRenderers)
        {
            if (mergedSources == null || mergedRenderers == null || mergedRenderers.Count == 0)
                return;

            var sourceSet = new HashSet<Renderer>();
            for (int i = 0; i < mergedSources.Count; i++)
            {
                if (mergedSources[i] != null)
                    sourceSet.Add(mergedSources[i]);
            }

            Renderer[] current = bodyRenderers;
            var list = new List<Renderer>((current != null ? current.Length : 0) + mergedRenderers.Count);
            if (current != null)
            {
                for (int i = 0; i < current.Length; i++)
                {
                    Renderer r = current[i];
                    if (r != null && !sourceSet.Contains(r))
                        list.Add(r);
                }
            }

            for (int i = 0; i < mergedRenderers.Count; i++)
            {
                if (mergedRenderers[i] != null)
                    list.Add(mergedRenderers[i]);
            }

            bodyRenderers = list.ToArray();
        }

        private void SetVisualsAndCollidersActive(bool active)
        {
            if (_cc != null) _cc.enabled = active;
            if (_colliders != null)
            {
                for (int i = 0; i < _colliders.Length; i++)
                {
                    if (_colliders[i] != null && _colliders[i] != _cc)
                        _colliders[i].enabled = active;
                }
            }

            if (bodyRenderers != null)
            {
                for (int i = 0; i < bodyRenderers.Length; i++)
                {
                    if (bodyRenderers[i] != null)
                        bodyRenderers[i].enabled = active;
                }
            }
        }

        private void ApplyHitFlash(bool whiteFlash)
        {
            if (bodyRenderers == null) return;
            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            for (int i = 0; i < bodyRenderers.Length; i++)
            {
                var r = bodyRenderers[i];
                if (r == null) continue;
                if (whiteFlash)
                {
                    r.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor(BaseColorProp, Color.white);
                    r.SetPropertyBlock(_propBlock);
                }
                else
                {
                    // 必须传 null 而不是空 MPB：空 MPB 仍会让 renderer 退出 GRD / GPU instancing
                    // 的合批路径。ResetImmediately 开局会给每个身体部件清一次闪白，
                    // 传空块会把全部角色永久踢出批处理；闪白只在受击的 0.09s 内保留 MPB。
                    r.SetPropertyBlock(null);
                }
            }
        }

        private void ClearHitFlash()
        {
            ApplyHitFlash(false);
        }
    }
}
