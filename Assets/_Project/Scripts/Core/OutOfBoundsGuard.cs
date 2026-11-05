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
    /// 注意：滑板场的下沉碗池（池底 -2.1m）是合法玩法区，y 阈值必须低于池底，
    /// 不能沿用所有地图都在 y=0 时的 -1m 平地默认值。
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
            // XZ 边界越界只在比赛 Playing 阶段回收（Ready/Countdown/Finished/Result 禁止拉人）；
            // 但「y 低于下限」的坠落回收在任何阶段都要做：哨响瞬间角色可能已经越过边界在往下掉，
            // 旧实现直接 return 会让它无限坠落（结算摄像机也跟着追出场地）。
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
        /// XZ 边界回收按比赛阶段门禁（见 <see cref="MatchManager.CombatAllowed"/>），
        /// y 坠落回收不受阶段限制。
        /// </summary>
        /// <returns>本轮被回收的单位数量</returns>
        public int CheckAndRecoverAll()
        {
            return CheckAndRecoverAll(MatchManager.CombatAllowed);
        }

        /// <summary>
        /// 带显式开关的扫描（false = 只回收 y 低于下限的坠落单位，不拉 XZ 越界者）。
        /// </summary>
        public int CheckAndRecoverAll(bool allowHorizontalRecovery)
        {
            GameConfigSO cfg = Config;
            float marginX = cfg != null ? cfg.outOfBoundsMarginX : 0.3f;
            float marginZ = cfg != null ? cfg.outOfBoundsMarginZ : 0.3f;
            float minY = cfg != null ? cfg.outOfBoundsMinY : GameConfigSO.DefaultOutOfBoundsMinY;

            var all = CombatantHealthRegistry.All;
            int recoveredCount = 0;

            for (int i = 0; i < all.Count; i++)
            {
                CombatantHealth combatant = all[i];
                if (combatant == null || !combatant.IsAlive) continue;

                Vector3 pos = combatant.transform.position;
                if (ShouldRecover(pos, allowHorizontalRecovery, marginX, marginZ, minY))
                {
                    RecoverCombatant(combatant);
                    recoveredCount++;
                }
            }

            return recoveredCount;
        }

        /// <summary>
        /// 将单个越界单位传回出生锚点：优先回收该单位自己的 <c>respawnAnchor</c>（多人越界各自回位，
        /// 不再全部叠在队伍兜底出生线同一点），没有锚点时才回落到队伍出生线。
        /// </summary>
        public void RecoverCombatant(CombatantHealth combatant)
        {
            if (combatant == null) return;

            GetSpawnPose(combatant, out Vector3 targetPos, out Quaternion targetRot);

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
        /// 纯静态越界判定（XZ 越界 + y 坠落，全部按越界处理），保留给外部/测试使用。
        /// 阶段门禁请用 <see cref="ShouldRecover"/>。
        /// </summary>
        public static bool IsOutOfBounds(Vector3 pos, float marginX = 0.3f, float marginZ = 0.3f,
            float minY = GameConfigSO.DefaultOutOfBoundsMinY)
        {
            return ShouldRecover(pos, true, marginX, marginZ, minY);
        }

        /// <summary>
        /// 纯静态回收判定（可供 EditMode 独立测试）：
        /// 1. y 低于 minY（坠落）→ 任何阶段都回收；
        /// 2. XZ 超出边界 + margin → 仅在 <paramref name="allowHorizontalRecovery"/> 为真（比赛 Playing）时回收。
        /// </summary>
        public static bool ShouldRecover(
            Vector3 pos,
            bool allowHorizontalRecovery,
            float marginX = 0.3f,
            float marginZ = 0.3f,
            float minY = GameConfigSO.DefaultOutOfBoundsMinY)
        {
            if (pos.y < minY) return true;
            if (!allowHorizontalRecovery) return false;

            float limitX = ArenaLayoutV2.BoundaryX + marginX;
            float limitZ = ArenaLayoutV2.BoundaryZ + marginZ;

            if (Mathf.Abs(pos.x) > limitX) return true;
            if (Mathf.Abs(pos.z) > limitZ) return true;

            return false;
        }

        /// <summary>
        /// 取单位的回收出生位姿：优先该单位自己的出生锚点，否则回落到队伍出生线（保留旧兜底行为）。
        /// </summary>
        public static void GetSpawnPose(CombatantHealth combatant, out Vector3 position, out Quaternion rotation)
        {
            if (combatant != null && combatant.TryGetSpawnPose(out Vector3 anchorPos, out Quaternion anchorRot))
            {
                position = anchorPos;
                rotation = anchorRot;
                return;
            }

            Team team = combatant != null ? combatant.Team : Team.TeamA;
            float preferredX = combatant != null ? combatant.transform.position.x : 0f;
            position = GetFallbackSpawnPosition(team, preferredX);
            rotation = GetSpawnRotation(team);
        }

        /// <summary>
        /// 获取单位的出生位置。优先读单位自己的出生锚点（<see cref="CombatantHealth.TryGetSpawnPose"/>），
        /// 无锚点（运行时临时单位）时使用队伍符号与出生线 X=±2.4 兜底。
        /// </summary>
        public static Vector3 GetSpawnPosition(CombatantHealth combatant)
        {
            GetSpawnPose(combatant, out Vector3 position, out _);
            return position;
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
