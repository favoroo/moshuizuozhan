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
        private Vector3 _initialSpawnPos;
        private Quaternion _initialSpawnRot;
        private CharacterController _cc;
        private Collider[] _colliders;
        private MaterialPropertyBlock _propBlock;
        private CombatantHealth _lastAttacker;
        private string _lastWeaponCause;
        private InkCharacterMotor _motorCache;
        private bool _motorQueried;

        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");

        public Team Team => team;
        public float CurrentHealth => _currentHealth;
        public float MaxHealth => config != null ? config.maxHealth : 100f;
        public float Health01 => Mathf.Clamp01(_currentHealth / MaxHealth);
        public bool IsAlive { get; private set; } = true;
        public bool IsInvulnerable { get; set; }
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
            _hitFlashTimer = 0.09f;
            ApplyHitFlash(true);

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

            HitstopService.Pulse(
                feel != null ? feel.hitstopHoldSeconds : HitstopService.DefaultHoldSeconds,
                feel != null ? feel.hitstopTimeScale : HitstopService.DefaultTimeScale,
                feel != null ? feel.hitstopRecoverSeconds : HitstopService.DefaultRecoverSeconds);
        }

        private void Die(Team killerTeam)
        {
            IsAlive = false;
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
                }
            }

            if (AudioManager.Instance != null)
            {
                if (killerTeam == Team.TeamA) AudioManager.Instance.PlayKill();
                else AudioManager.Instance.PlayDeath();
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

            if (!IsAlive)
            {
                _respawnTimer -= Time.deltaTime;
                if (_respawnTimer <= 0f)
                {
                    ResetImmediately();
                    if (AudioManager.Instance != null && team == Team.TeamA)
                    {
                        AudioManager.Instance.PlayRespawn();
                    }
                }
            }
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
