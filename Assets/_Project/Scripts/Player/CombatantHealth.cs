using System;
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

        // P6-B 命中墨花门控：同一单位 50ms 内多次命中只炸一片（霰弹/连发聚合），
        // 距相机 40m 外的战斗不炸（Inkwave _near(victim.pos, 40) 的同款取舍）
        private const float HitSplashMinInterval = 0.05f;
        private const float HitSplashMaxDistanceSqr = 1600f;
        private const float HitSplashChestHeight = 0.9f;
        private const float HitSplashDamageRefFallback = 60f;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");

        public Team Team => team;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => config != null ? config.maxHealth : 100f;
        public float Health01 => Mathf.Clamp01(_currentHealth / MaxHealth);
        public bool IsAlive { get; private set; } = true;

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
            _propBlock = new MaterialPropertyBlock();
            _initialSpawnPos = transform.position;
            _initialSpawnRot = transform.rotation;
            _currentHealth = MaxHealth;

            if (bodyRenderers == null || bodyRenderers.Length == 0)
            {
                bodyRenderers = GetComponentsInChildren<Renderer>(true);
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
        /// 强度随伤害缩放；0.05s 节流 + 40m 距离门控，避免 8 人混战刷爆粒子池。
        /// hitPoint 被丢弃的旧问题顺带修正：无 attacker 时用「受击点 → 身体中心」的反向近似弹道。
        /// </summary>
        private void SpawnHitSplashFeedback(float damage, Team attackerTeam, CombatantHealth attacker, Vector3 hitPoint)
        {
            if (VfxPool.Instance == null)
                return;

            float now = Time.time;
            if (now - _lastHitSplashTime < HitSplashMinInterval)
                return;
            _lastHitSplashTime = now;

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

            float damageRef = config != null && config.hitSplashDamageRef > 1f
                ? config.hitSplashDamageRef
                : HitSplashDamageRefFallback;
            VfxPool.Instance.SpawnHitSplash(origin, shotDir, color, damage / damageRef);
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
                    VfxPool.Instance.SpawnKillBurst(transform.position, killDir, c, deathSplatRadius);
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
            OnKilled?.Invoke(this, splatTeam);
            KillFeedEventHub.ReportCombatantDeath(this, splatTeam, _lastAttacker, _lastWeaponCause);
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
                if (_spawnProtectionRemaining > 0f)
                {
                    // 6Hz 闪白提示（INKWAVE 用着色器呼吸闪代替实体护盾，这里复用受击闪白通道）
                    _spawnBlinkTimer -= Time.deltaTime;
                    if (_spawnBlinkTimer <= 0f)
                    {
                        _spawnBlinkTimer = 0.1f;
                        _spawnBlinkOn = !_spawnBlinkOn;
                        ApplyHitFlash(_spawnBlinkOn);
                    }
                }
                else
                {
                    _spawnBlinkOn = false;
                    if (_hitFlashTimer <= 0f)
                    {
                        ClearHitFlash();
                    }
                }
            }

            if (!IsAlive)
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

        /// <summary>复活瞬间在脚下放一圈队色扩散环，和身体闪白一起提示"出生保护中"。</summary>
        private void PlaySpawnProtectionVfx()
        {
            if (VfxPool.Instance == null || InkSurfaceController.Instance == null)
                return;

            Color color = InkSurfaceController.Instance.GetTeamColor(team);
            VfxPool.Instance.SpawnShockwaveRing(
                transform.position + Vector3.up * 0.12f, color, 0.45f, 1.8f, 0.42f);
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
                r.GetPropertyBlock(_propBlock);
                if (whiteFlash)
                    _propBlock.SetColor(BaseColorProp, Color.white);
                else
                    _propBlock.Clear();
                r.SetPropertyBlock(_propBlock);
            }
        }

        private void ClearHitFlash()
        {
            ApplyHitFlash(false);
        }
    }
}
