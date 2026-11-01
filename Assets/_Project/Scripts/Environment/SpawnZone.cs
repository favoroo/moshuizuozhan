using System.Collections.Generic;
using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.Environment
{
    /// <summary>
    /// 出生禁入圈 (P16-A)：
    /// 每队一个圆圈，中心位于出生线中心 (0, y, ±ArenaLayoutV2.SpawnZ)，默认半径 6m。
    /// 仅对敌方角色生效：每帧对圈内的敌方角色沿 XZ 径向向外推击，推出量为 pushSpeed * dt，
    /// 垂直分量保持为 0；不瞬移、不清速度、不判死，平滑阻止敌方堵门压家。
    /// </summary>
    public class SpawnZone : MonoBehaviour
    {
        [SerializeField] private Team owningTeam = Team.TeamA;
        [SerializeField] private Vector3 center;
        [SerializeField] private float radius = 6f;
        [SerializeField] private float pushSpeed = 8f;
        [SerializeField] private GameConfigSO config;

        private static readonly List<SpawnZone> _activeZones = new List<SpawnZone>(2);

        public static IReadOnlyList<SpawnZone> ActiveZones => _activeZones;

        public Team OwningTeam => owningTeam;
        public Vector3 Center => center;
        public float Radius => radius;
        public float PushSpeed => pushSpeed;

        public static SpawnZone FindZone(Team team)
        {
            for (int i = 0; i < _activeZones.Count; i++)
            {
                if (_activeZones[i] != null && _activeZones[i].OwningTeam == team)
                    return _activeZones[i];
            }
            return null;
        }

        public static SpawnZone CreateZone(Team team, Transform parent = null, GameConfigSO config = null)
        {
            var go = new GameObject($"SpawnZone_{team}");
            if (parent != null)
            {
                go.transform.SetParent(parent, false);
            }
            var zone = go.AddComponent<SpawnZone>();
            zone.Configure(team, config);
            return zone;
        }

        private void OnEnable()
        {
            if (!_activeZones.Contains(this))
            {
                _activeZones.Add(this);
            }
        }

        private void OnDisable()
        {
            _activeZones.Remove(this);
        }

        public void Configure(Team team, GameConfigSO gameConfig = null)
        {
            owningTeam = team;
            center = GetDefaultZoneCenter(team);
            transform.position = center;

            if (gameConfig != null)
            {
                config = gameConfig;
                radius = gameConfig.spawnZoneRadius;
                pushSpeed = gameConfig.spawnZonePushSpeed;
            }
            else if (MatchManager.Instance != null && MatchManager.Instance.Config != null)
            {
                config = MatchManager.Instance.Config;
                radius = config.spawnZoneRadius;
                pushSpeed = config.spawnZonePushSpeed;
            }
        }

        public void SetRadiusAndSpeed(float newRadius, float newPushSpeed)
        {
            radius = newRadius;
            pushSpeed = newPushSpeed;
        }

        public void SetCenter(Vector3 newCenter)
        {
            center = newCenter;
            transform.position = newCenter;
        }

        private void Update()
        {
            // 仅在 Playing 阶段对敌方施加推力
            if (MatchManager.Instance != null && MatchManager.Instance.CurrentPhase != MatchManager.Phase.Playing)
            {
                return;
            }

            Tick(Time.deltaTime);
        }

        /// <summary>
        /// 推进一次禁入圈推力（可由测试直接驱动，不依赖比赛阶段门禁）。
        /// </summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;

            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth combatant = all[i];
                if (combatant == null || !combatant.IsAlive) continue;
                TryPush(combatant, dt);
            }
        }

        /// <summary>
        /// 对单个战斗单位施加一次推力；不在圈内或非敌方时返回 false。
        /// </summary>
        public bool TryPush(CombatantHealth combatant, float dt)
        {
            if (combatant == null || !combatant.IsAlive || dt <= 0f) return false;
            if (!ShouldPush(combatant.Team, owningTeam)) return false;

            Vector3 pos = combatant.transform.position;
            if (!IsInside(pos, center, radius)) return false;

            Vector3 fallbackDir = GetDefaultFallbackDirection(owningTeam);
            Vector3 delta = CalculatePushDelta(pos, center, pushSpeed, dt, fallbackDir);
            InkCharacterMotor motor = combatant.Motor;
            if (motor != null)
            {
                motor.ApplyExternalPush(delta);
            }
            else
            {
                combatant.transform.position += delta;
            }
            return true;
        }

        /// <summary>
        /// 判定坐标在 XZ 平面上是否处于圆圈内（忽略 Y 轴高度差异）。
        /// </summary>
        public static bool IsInside(Vector3 position, Vector3 center, float radius)
        {
            float dx = position.x - center.x;
            float dz = position.z - center.z;
            return (dx * dx + dz * dz) < (radius * radius);
        }

        /// <summary>
        /// 计算从圆心指向角色的径向推出方向（归一化，垂直分量始终为 0）。
        /// 若正好重合在圆心，使用回退方向。
        /// </summary>
        public static Vector3 CalculatePushDirection(Vector3 position, Vector3 center, Vector3 fallbackDirection)
        {
            Vector3 diff = new Vector3(position.x - center.x, 0f, position.z - center.z);
            if (diff.sqrMagnitude > 0.0001f)
            {
                return diff.normalized;
            }

            fallbackDirection.y = 0f;
            return fallbackDirection.sqrMagnitude > 0.0001f ? fallbackDirection.normalized : Vector3.forward;
        }

        /// <summary>
        /// 计算单步推移量向量（水平面位移）。
        /// </summary>
        public static Vector3 CalculatePushDelta(Vector3 position, Vector3 center, float pushSpeed, float dt, Vector3 fallbackDirection)
        {
            Vector3 dir = CalculatePushDirection(position, center, fallbackDirection);
            return dir * (pushSpeed * dt);
        }

        /// <summary>
        /// 规则判断：是否应对该战斗单位施加推力（仅对禁入圈所属队伍的敌方生效）。
        /// </summary>
        public static bool ShouldPush(Team combatantTeam, Team zoneTeam)
        {
            return combatantTeam.IsOpponentOf(zoneTeam);
        }

        /// <summary>
        /// 获取队伍出生禁入圈的默认圆心。
        /// </summary>
        public static Vector3 GetDefaultZoneCenter(Team team)
        {
            float z = team == Team.TeamB ? ArenaLayoutV2.SpawnZ : -ArenaLayoutV2.SpawnZ;
            return new Vector3(0f, ArenaLayoutV2.SpawnPointY, z);
        }

        /// <summary>
        /// 获取圆心重合时的回退推力方向（从出生点推向竞技场中央）。
        /// </summary>
        public static Vector3 GetDefaultFallbackDirection(Team team)
        {
            return team == Team.TeamA ? Vector3.forward : Vector3.back;
        }

        /// <summary>
        /// 判定指定位置是否位于我方的敌方出生禁入圈（含额外缓冲）内。
        /// 供 Bot 挑选涂地目标时避开禁入圈。
        /// </summary>
        public static bool IsInEnemySpawnZone(Vector3 position, Team myTeam, float extraBuffer = 0f)
        {
            if (myTeam == Team.Neutral) return false;

            Team enemyTeam = myTeam.GetOpponent();
            SpawnZone zone = FindZone(enemyTeam);
            Vector3 enemyCenter = zone != null ? zone.Center : GetDefaultZoneCenter(enemyTeam);
            float r = (zone != null ? zone.Radius : 6f) + extraBuffer;

            float dx = position.x - enemyCenter.x;
            float dz = position.z - enemyCenter.z;
            return (dx * dx + dz * dz) <= (r * r);
        }
    }
}
