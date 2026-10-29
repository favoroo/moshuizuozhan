using System;
using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 墨水颜色自适应组件（P4-A）：把纯白矢量图标 / 文字（Art/Sprites/Icons 下的单色素材）
    /// 自动染成当前队伍墨色。默认跟随本地玩家队伍；队伍一变色（或开局后玩家才生成）会自动刷新。
    ///
    /// 用法：挂在图标组 / 单个图标的根节点上即可，子节点的 Image、RawImage、TMP_Text 和
    /// UIInkShapeGraphic 都会被收集。嵌套的 InkTeamColorFilter 拥有更高优先级，外层不会抢。
    /// </summary>
    [AddComponentMenu("P1/UI/Ink Team Color Filter")]
    [DisallowMultipleComponent]
    public class InkTeamColorFilter : MonoBehaviour
    {
        [SerializeField] private UIThemeSO theme;

        [Tooltip("自动跟随本地玩家所属队伍")]
        [SerializeField] private bool autoBindLocalPlayer = true;

        [SerializeField] private Team team = Team.TeamA;

        [SerializeField] private bool includeChildren = true;

        [SerializeField] private bool includeInactive;

        [Tooltip("保留每个目标自身的 alpha（图标透明底不会被染没）")]
        [SerializeField] private bool preserveAlpha = true;

        [Tooltip("0 = 原色，1 = 完全队伍墨色")]
        [SerializeField, Range(0f, 1f)] private float tintStrength = 1f;

        [Tooltip("true：直接替换为队伍色；false：与白色/原色相乘（白色图标默认行为）")]
        [SerializeField] private bool replaceColor;

        [SerializeField] private Graphic[] extraGraphics;

        [SerializeField] private TMP_Text[] extraTexts;

        public event Action<Color> OnColorChanged;

        private Graphic[] _graphics = Array.Empty<Graphic>();
        private Color[] _graphicBaseColors = Array.Empty<Color>();
        private TMP_Text[] _texts = Array.Empty<TMP_Text>();
        private Color[] _textBaseColors = Array.Empty<Color>();
        private Color _appliedColor = Color.white;
        private bool _collected;
        private bool _explicitTeam;

        public Team Team => team;

        public Color CurrentColor => _appliedColor;

        private void Awake()
        {
            if (!autoBindLocalPlayer) _explicitTeam = true;
            CollectTargets();
            Refresh();
        }

        private void OnEnable()
        {
            if (_collected) Refresh();
        }

        private void LateUpdate()
        {
            if (_explicitTeam || !autoBindLocalPlayer) return;

            PlayerController player = PlayerController.LocalPlayer;
            if (player == null || player.Health == null) return;

            Team localTeam = player.Health.Team;
            if (localTeam == Team.Neutral || localTeam == team) return;

            team = localTeam;
            Refresh();
        }

        /// <summary>重新扫描子节点并缓存原始颜色（在运行时动态添加目标后调用）。</summary>
        public void CollectTargets()
        {
            List<Graphic> graphics = new List<Graphic>();
            List<TMP_Text> texts = new List<TMP_Text>();

            if (includeChildren)
            {
                foreach (Graphic graphic in GetComponentsInChildren<Graphic>(includeInactive))
                {
                    // TMP_Text 也是 Graphic；文字走下面的 _texts 通道，避免同一次染色乘两遍
                    if (graphic is TMP_Text) continue;
                    if (IsOwnedByThis(graphic)) graphics.Add(graphic);
                }

                foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(includeInactive))
                {
                    // Chunky 文本的底层是黑色描边层，整组染色会把它染成墨色；用 UIChunkyText.SetTeam 单独处理
                    if (text.GetComponentInParent<UIChunkyText>(true) != null) continue;
                    if (IsOwnedByThis(text)) texts.Add(text);
                }
            }
            else
            {
                foreach (Graphic graphic in GetComponents<Graphic>())
                {
                    if (!(graphic is TMP_Text)) graphics.Add(graphic);
                }

                foreach (TMP_Text text in GetComponents<TMP_Text>())
                {
                    if (text.GetComponentInParent<UIChunkyText>(true) == null) texts.Add(text);
                }
            }

            if (extraGraphics != null)
            {
                foreach (Graphic graphic in extraGraphics)
                {
                    if (graphic != null && !graphics.Contains(graphic)) graphics.Add(graphic);
                }
            }

            if (extraTexts != null)
            {
                foreach (TMP_Text text in extraTexts)
                {
                    if (text != null && !texts.Contains(text)) texts.Add(text);
                }
            }

            _graphics = graphics.ToArray();
            _graphicBaseColors = new Color[_graphics.Length];
            for (int i = 0; i < _graphics.Length; i++) _graphicBaseColors[i] = _graphics[i].color;

            _texts = texts.ToArray();
            _textBaseColors = new Color[_texts.Length];
            for (int i = 0; i < _texts.Length; i++) _textBaseColors[i] = _texts[i].color;

            _collected = true;
        }

        private bool IsOwnedByThis(Component target)
        {
            InkTeamColorFilter owner = target.GetComponentInParent<InkTeamColorFilter>(true);
            return owner == this;
        }

        /// <summary>显式指定队伍，并关闭本地玩家自动跟随。</summary>
        public void SetTeam(Team newTeam)
        {
            _explicitTeam = true;
            if (team == newTeam)
            {
                Refresh();
                return;
            }

            team = newTeam;
            Refresh();
        }

        /// <summary>
        /// 配置入口（编辑器工具、P4-B/C 构建程序化 HUD 时使用）。
        /// 编辑模式下只写序列化字段，不刷新颜色——避免把染色结果烘进场景后运行时二次相乘。
        /// </summary>
        public void Configure(Team newTeam, bool bindToLocalPlayer = false,
            UIThemeSO themeAsset = null)
        {
            if (themeAsset != null) theme = themeAsset;

            team = newTeam;
            autoBindLocalPlayer = bindToLocalPlayer;
            _explicitTeam = !bindToLocalPlayer;

            if (Application.isPlaying) Refresh();
        }

        /// <summary>恢复跟随本地玩家队伍。</summary>
        public void SetAutoBindLocalPlayer(bool enabled)
        {
            autoBindLocalPlayer = enabled;
            if (enabled) _explicitTeam = false;
            Refresh();
        }

        public void SetTintStrength(float strength)
        {
            tintStrength = Mathf.Clamp01(strength);
            Refresh();
        }

        /// <summary>重新计算并应用队伍墨色。</summary>
        public void Refresh()
        {
            if (!_collected) CollectTargets();

            Color teamColor = ResolveTeamColor(team, theme);
            Color applied = new Color(teamColor.r, teamColor.g, teamColor.b, preserveAlpha ? 1f : teamColor.a);

            for (int i = 0; i < _graphics.Length; i++)
            {
                if (_graphics[i] == null) continue;
                _graphics[i].color = TintColor(
                    _graphicBaseColors[i], teamColor, tintStrength, preserveAlpha, replaceColor);
            }

            for (int i = 0; i < _texts.Length; i++)
            {
                if (_texts[i] == null) continue;
                _texts[i].color = TintColor(
                    _textBaseColors[i], teamColor, tintStrength, preserveAlpha, replaceColor);
            }

            if (_appliedColor != applied)
            {
                _appliedColor = applied;
                OnColorChanged?.Invoke(_appliedColor);
            }
        }

        /// <summary>
        /// 统一染色公式：白色图标相乘 = 纯队伍墨色；replace 模式则向队伍色插值。
        /// 公开为静态方法，EditMode 测试可直接验证。
        /// </summary>
        public static Color TintColor(Color source, Color teamColor, float strength,
            bool preserveAlpha = true, bool replace = false)
        {
            strength = Mathf.Clamp01(strength);

            Color target = replace
                ? new Color(teamColor.r, teamColor.g, teamColor.b, source.a)
                : Color.white;

            if (!replace)
            {
                target = new Color(
                    source.r * teamColor.r,
                    source.g * teamColor.g,
                    source.b * teamColor.b,
                    source.a);
            }

            if (!preserveAlpha) target.a = teamColor.a;

            return Color.Lerp(source, target, strength);
        }

        /// <summary>优先读比赛中的 InkGridConfig（单一数据源），没有场景时退回主题配色。</summary>
        public static Color ResolveTeamColor(Team team, UIThemeSO themeAsset)
        {
            UIThemeSO resolved = UIThemeSO.Resolve(themeAsset);
            if (team != Team.Neutral && InkSurfaceController.Instance != null)
            {
                return InkSurfaceController.Instance.GetTeamColor(team);
            }

            return resolved.GetTeamColor(team);
        }
    }
}