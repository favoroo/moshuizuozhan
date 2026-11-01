using System;
using P1.Environment;
using P1.Player;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 越界回收与防坠落守护器 (P16-B)：
    /// 定期扫描全部存活战斗单位，当角色翻出边界墙或坠落至指定高度以下时，
    /// 立即传回本队出生锚点并清空速度，不判死、不清血量、不触发击杀事件，
    /// 彻底防止玩家翻越北墙软锁或卡在场外死区。
    /// </summary>
    public class OutOfBoundsGuard : MonoBehaviour
    {
        [SerializeField] private GameConfigSO config;

        private float _checkTimer;

        /// <summary>战斗单位越界被回收事件：(combatant, recoveredPosition)</summary>
        public event Action<CombatantHealth, Vector3> OnCombatantRecovered;

        public GameConfigSO Config
        {
            get => config != null ? config : (MatchManager.Instance != null ? MatchManager.Instance.Config : null);
            set => config = value;
        }

        public void Configure(GameConfigSO gameConfig)
        {
            config = gameConfig;
        }

        private void Update()
        {
            // 仅在比赛 Playing 阶段执行回收；Ready/Countdown/Finished/Result 阶段禁止拉人
            if (MatchManager.Instance != null && MatchManager.Instance.CurrentPhase != MatchManager.Phase.Playing)
            {
                return;
            }

            GameConfigSO cfg = Config;
            float interval = cfg != null ? Mathf.Max(0.05f, cfg.outOfBoundsCheckIntervalSeconds) : 0.25f;

            _checkTimer -= Time.deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = interval;
                CheckAndRecoverAll();
            }
        }

        /// <summary>
        /// 扫描所有注册的战斗单位并回收越界者。
        /// </summary>
        /// <returns>本轮被回收的单位数量</returns>
        public int CheckAndRecoverAll()
        {
            GameConfigSO cfg = Config;
            float marginX = cfg != null ? cfg.outOfBoundsMarginX : 0.3f;
            float marginZ = cfg != null ? cfg.outOfBoundsMarginZ : 0.3f;
            float minY = cfg != null ? cfg.outOfBoundsMinY : -1f;

            var all = CombatantHealthRegistry.All;
            int recoveredCount = 0;

            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth combatant = all[i];
                if (combatant == null || !combatant.IsAlive) continue;

                Vector3 pos = combatant.transform.position;
                if (IsOutOfBounds(pos, marginX, marginZ, minY))
                {
                    RecoverCombatant(combatant);
                    recoveredCount++;
                }
            }

            return recoveredCount;
        }

        /// <summary>
        /// 将单个越界单位传回本队出生锚点。
        /// </summary>
        public void RecoverCombatant(CombatantHealth combatant)
        {
            if (combatant == null) return;

            Vector3 targetPos = GetSpawnPosition(combatant);
            Quaternion targetRot = GetSpawnRotation(combatant.Team);

            InkCharacterMotor motor = combatant.Motor;
            if (motor != null)
            {
                motor.TeleportTo(targetPos, targetRot, clearVelocity: true);
            }
            else
            {
                CharacterController cc = combatant.GetComponent<CharacterController>();
                if (cc != null && cc.enabled)
                {
                    cc.enabled = false;
                    combatant.transform.SetPositionAndRotation(targetPos, targetRot);
                    cc.enabled = true;
                }
                else
                {
                    combatant.transform.SetPositionAndRotation(targetPos, targetRot);
                }
            }

            OnCombatantRecovered?.Invoke(combatant, targetPos);
#if UNITY_EDITOR
            Debug.Log($"[OutOfBoundsGuard] 角色 {combatant.name} ({combatant.Team}) 越界，已安全传回出生点 {targetPos}");
#endif
        }

        /// <summary>
        /// 纯静态越界判定方法（可供 EditMode 独立测试）。
        /// 越界条件：|x| > BoundaryX + marginX 或 |z| > BoundaryZ + marginZ 或 y < minY。
        /// </summary>
        public static bool IsOutOfBounds(Vector3 pos, float marginX = 0.3f, float marginZ = 0.3f, float minY = -1f)
        {
            float limitX = ArenaLayoutV2.BoundaryX + marginX;
            float limitZ = ArenaLayoutV2.BoundaryZ + marginZ;

            if (Mathf.Abs(pos.x) > limitX) return true;
            if (Mathf.Abs(pos.z) > limitZ) return true;
            if (pos.y < minY) return true;

            return false;
        }

        /// <summary>
        /// 获取单位的出生位置。优先读公开 API（当前未暴露则使用队伍符号与出生线 X=±2.4 兜底）。
        /// </summary>
        public static Vector3 GetSpawnPosition(CombatantHealth combatant)
        {
            if (combatant == null)
            {
                return GetFallbackSpawnPosition(Team.TeamA, 0f);
            }

            return GetFallbackSpawnPosition(combatant.Team, combatant.transform.position.x);
        }

        /// <summary>
        /// 出生锚点纯函数：根据队伍与偏好 X 坐标生成合法的出生点位置。
        /// TeamA 在 -SpawnZ，TeamB 在 +SpawnZ，Y 在 SpawnPointY，X 被限制在出生线宽度 [-2.4, 2.4] 内。
        /// </summary>
        public static Vector3 GetFallbackSpawnPosition(Team team, float preferredX = 0f)
        {
            float z = team == Team.TeamB ? ArenaLayoutV2.SpawnZ : -ArenaLayoutV2.SpawnZ;
            float x = Mathf.Clamp(preferredX, -2.4f, 2.4f);
            float y = ArenaLayoutV2.SpawnPointY;
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// 获取出生点的默认朝向：TeamA 面向 +Z (0°)，TeamB 面向 -Z (180°)。
        /// </summary>
        public static Quaternion GetSpawnRotation(Team team)
        {
            float facing = team == Team.TeamB ? 180f : 0f;
            return Quaternion.Euler(0f, facing, 0f);
        }
    }
}
