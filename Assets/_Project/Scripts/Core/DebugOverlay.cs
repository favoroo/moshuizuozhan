using P1.Ink;
using P1.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace P1.Core
{
    /// <summary>
    /// 调试叠层与快捷测试热键（默认关闭，避免与系统/编辑器的 F1~F4 冲突）：
    /// F12: 开关调试面板（仅在面板打开时，F2/F3/F4 才会生效）
    /// F2: 清空重涂全场 | F3: 立即充满大招 | F4: 在脚下生成敌方墨水测试减速
    /// </summary>
    public class DebugOverlay : MonoBehaviour
    {
        [SerializeField] private bool showOverlay = false;

        private float _smoothedDeltaTime = 0.016f;
        private GUIStyle _boxStyle;
        private GUIStyle _labelStyle;

        private void Update()
        {
            _smoothedDeltaTime += (Time.unscaledDeltaTime - _smoothedDeltaTime) * 0.1f;

            var kb = Keyboard.current;
            if (kb == null) return;

            // 用 F12 切换，避开被系统/编辑器占用的 F1
            if (kb.f12Key.wasPressedThisFrame)
            {
                showOverlay = !showOverlay;
            }

            // 调试快捷键仅在面板打开时生效，默认状态下 F2/F3/F4 不触发任何东西
            if (!showOverlay) return;

            if (kb.f2Key.wasPressedThisFrame && InkSurfaceController.Instance != null)
            {
                InkSurfaceController.Instance.ResetSurface();
                Debug.Log("[DebugOverlay] F2: 已重置全场墨水。");
            }

            if (kb.f3Key.wasPressedThisFrame && PlayerController.LocalPlayer != null)
            {
                PlayerController.LocalPlayer.Weapon.AddSpecialPoints(500);
                PlayerController.LocalPlayer.Motor.RefillInkFull();
                Debug.Log("[DebugOverlay] F3: 已充满大招能量与墨水槽。");
            }

            if (kb.f4Key.wasPressedThisFrame && PlayerController.LocalPlayer != null && InkSurfaceController.Instance != null)
            {
                Vector3 pos = PlayerController.LocalPlayer.transform.position;
                InkSurfaceController.Instance.PaintCircle(pos, 3.2f, Team.TeamB);
                Debug.Log("[DebugOverlay] F4: 已在脚下生成敌方蓝墨水（测试减速与灼烧）。");
            }
        }

        private void OnGUI()
        {
            if (!showOverlay) return;

            EnsureStyles();

            float fps = 1f / Mathf.Max(0.0001f, _smoothedDeltaTime);
            var surface = InkSurfaceController.Instance;
            var player = PlayerController.LocalPlayer;

            Rect rect = new Rect(16f, 70f, 340f, 300f);
            GUI.Box(rect, GUIContent.none, _boxStyle);

            GUILayout.BeginArea(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, rect.height - 20f));
            GUILayout.Label($"<b>[P1 调试面板 (F12显隐)]</b>  FPS: {fps:F0}", _labelStyle);

            if (surface != null && surface.Grid != null)
            {
                var g = surface.Grid;
                GUILayout.Label($"网格: {g.Width}x{g.Height} (可涂 {g.PaintableCount} 格)", _labelStyle);
                GUILayout.Label($"橙队覆盖: {surface.GetCoveragePercent(Team.TeamA):F1}% ({g.TeamACount}格) | 蓝队: {surface.GetCoveragePercent(Team.TeamB):F1}% ({g.TeamBCount}格)", _labelStyle);
            }

            if (player != null)
            {
                var m = player.Motor;
                var w = player.Weapon;
                var h = player.Health;
                string mode = m.IsWallClimbing
                    ? "墙面潜行攀爬"
                    : (m.IsSwimming ? "潜行形态 (乌贼)" : (w.IsPerformingSpecial ? "大招跃起砸地" : "人形状态"));
                GUILayout.Label($"状态: {mode} | 脚下墨水: {m.FootInkTeam} | 移速: {m.CurrentMoveSpeed:F1} m/s", _labelStyle);
                GUILayout.Label($"墨水: {m.CurrentInk:F0}/{m.MaxInk:F0} | 生命: {h.CurrentHealth:F0}/{h.MaxHealth:F0} | 大招: {(w.SpecialCharge01 * 100f):F0}%", _labelStyle);
            }

            GUILayout.Space(4f);
            GUILayout.Label("<color=#FFD54F>热键: [F2]清空墨水  [F3]秒满大招  [F4]脚下刷敌墨</color>", _labelStyle);

            // Bot 状态一览（BOt 目标与状态，排查卡死/不交战时最有用）
            GUILayout.Space(4f);
            var all = CombatantHealthRegistry.All;
            for (int i = 0; i < all.Count; i++)
            {
                var c = all[i];
                if (c == null) continue;
                var bot = c.GetComponent<P1.AI.BotController>();
                if (bot == null) continue;
                GUILayout.Label($"{c.name} [{bot.State}] target={bot.DebugTargetName} hp={c.CurrentHealth:F0} team={c.Team}", _labelStyle);
            }
            GUILayout.EndArea();
        }

        private void EnsureStyles()
        {
            if (_boxStyle != null) return;

            _boxStyle = new GUIStyle(GUI.skin.box);
            Texture2D bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.06f, 0.08f, 0.12f, 0.82f));
            bg.Apply();
            _boxStyle.normal.background = bg;

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                fontSize = 13,
                normal = { textColor = new Color(0.92f, 0.95f, 1f, 1f) }
            };
        }
    }
}
