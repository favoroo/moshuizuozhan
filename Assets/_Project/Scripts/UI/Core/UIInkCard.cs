using P1.Core;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 墨滴卡片容器基类（P4-A）：自动装配「墨滴/圆角背景 + 内容区 + 弹簧弹入」，
    /// 队伍色、Chunky 描边、斜切高光全部来自 <see cref="UIThemeSO"/>。
    ///
    /// P4-B（4v4 战况条）与 P4-C（液体大招球）可直接继承本类：
    /// - 覆写 <see cref="ApplyTheme"/> 追加自己的配色；
    /// - 用 <see cref="Content"/> 挂载任意子元素；
    /// - 用 <see cref="PlayIn"/>/<see cref="Punch"/> 触发统一的果冻反馈。
    /// </summary>
    [AddComponentMenu("P1/UI/Ink Card")]
    [RequireComponent(typeof(RectTransform))]
    public class UIInkCard : MonoBehaviour
    {
        [SerializeField] protected UIThemeSO theme;

        [SerializeField] protected UIInkCardVariant variant = UIInkCardVariant.Team;

        [SerializeField] protected bool useTeamColor = true;

        [SerializeField] protected Team team = Team.TeamA;

        [SerializeField] protected bool playInOnEnable = true;

        [SerializeField] protected float playInDelay;

        [SerializeField, Range(0f, 0.25f)] protected float entryWobble = 0.09f;

        [SerializeField] protected UIInkShapeGraphic background;

        [SerializeField] protected UIChunkyText title;

        [SerializeField] protected bool createContent = true;

        [SerializeField] protected float contentPadding = 24f;

        [Tooltip("背景是否接收点击；卡片通常需要，纯装饰卡片可关掉")]
        [SerializeField] protected bool backgroundReceivesRaycast = true;

        private UISpringMotion _motion;
        private bool _built;

        public UIInkShapeGraphic Background => background;

        public RectTransform Content { get; private set; }

        public UIThemeSO Theme => UIThemeSO.Resolve(theme);

        public UISpringMotion Motion
        {
            get
            {
                EnsureMotion();
                return _motion;
            }
        }

        protected virtual void Awake()
        {
            EnsureBuilt();
        }

        protected virtual void OnEnable()
        {
            if (playInOnEnable && Application.isPlaying) PlayIn(playInDelay);
        }

        /// <summary>编辑模式也能安全调用：缺什么补什么，可重复执行。</summary>
        public virtual void EnsureBuilt()
        {
            if (_built) return;
            _built = true;

            if (background == null)
            {
                GameObject backgroundGo = new GameObject(
                    "Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                backgroundGo.transform.SetParent(transform, false);
                Stretch((RectTransform)backgroundGo.transform, 0f, 0f);
                backgroundGo.transform.SetSiblingIndex(0);

                background = backgroundGo.GetComponent<UIInkShapeGraphic>();
                background.raycastTarget = backgroundReceivesRaycast;
            }

            if (title == null) title = GetComponentInChildren<UIChunkyText>(true);

            if (createContent && Content == null)
            {
                RectTransform existing = transform.Find("Content") as RectTransform;
                if (existing != null)
                {
                    Content = existing;
                }
                else
                {
                    GameObject contentGo = new GameObject("Content", typeof(RectTransform));
                    contentGo.transform.SetParent(transform, false);
                    Content = (RectTransform)contentGo.transform;
                    Stretch(Content, contentPadding, contentPadding);
                }

                Content.SetSiblingIndex(background != null ? 1 : 0);
            }

            EnsureMotion();
            ApplyTheme();
        }

        public void Configure(UIThemeSO themeAsset, UIInkCardVariant cardVariant, Team cardTeam,
            bool tintWithTeamColor = true)
        {
            theme = themeAsset;
            variant = cardVariant;
            team = cardTeam;
            useTeamColor = tintWithTeamColor;

            if (Application.isPlaying)
            {
                EnsureBuilt();
                ApplyTheme();
            }
            else if (_built)
            {
                ApplyTheme();
            }
        }

        public void SetTitle(UIChunkyText titleText)
        {
            title = titleText;
            ApplyTheme();
        }

        public void SetTeam(Team newTeam)
        {
            team = newTeam;
            useTeamColor = true;
            ApplyTheme();
        }

        public void SetVariant(UIInkCardVariant newVariant)
        {
            variant = newVariant;
            ApplyTheme();
        }

        /// <summary>用任意颜色覆盖当前卡片填充（关闭队伍跟随）。</summary>
        public void SetAccent(Color color)
        {
            useTeamColor = false;
            if (background != null)
            {
                background.ApplyStyle(Theme.GetCardStyle(variant), color);
            }
        }

        /// <summary>把主题预设刷到背景与标题上；派生类可覆写追加自己的元素。</summary>
        public virtual void ApplyTheme()
        {
            UIThemeSO resolved = Theme;
            UIInkCardStyle style = resolved.GetCardStyle(variant);

            if (background != null)
            {
                Color fill = useTeamColor
                    ? InkTeamColorFilter.ResolveTeamColor(team, theme)
                    : style.fillColor;
                background.ApplyStyle(style, fill);
            }

            if (title != null)
            {
                title.ApplyTheme(theme);
                title.SetFillColor(style.titleColor);
            }
        }

        /// <summary>弹性弹入；delay 单位秒。</summary>
        public virtual void PlayIn(float delay = 0f)
        {
            EnsureBuilt();
            EnsureMotion().SetTheme(theme);
            _motion.PlayIn(delay);

            if (background != null && entryWobble > 0.001f)
            {
                background.PlayWobble(entryWobble, 0.55f);
            }
        }

        public virtual void PlayOut()
        {
            EnsureBuilt();
            _motion.PlayOut();
        }

        /// <summary>被强调时弹一下（选中、奖励、大招就绪等）。</summary>
        public virtual void Punch()
        {
            EnsureBuilt();
            _motion.Punch(0.07f);
            if (background != null) background.PlayWobble(0.1f, 0.4f);
        }

        private UISpringMotion EnsureMotion()
        {
            if (_motion != null) return _motion;

            _motion = GetComponent<UISpringMotion>();
            if (_motion == null) _motion = gameObject.AddComponent<UISpringMotion>();
            _motion.SetTheme(theme);
            return _motion;
        }

        private static void Stretch(RectTransform rectTransform, float horizontalPadding,
            float verticalPadding)
        {
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = new Vector2(horizontalPadding, verticalPadding);
            rectTransform.offsetMax = new Vector2(-horizontalPadding, -verticalPadding);
        }
    }
}