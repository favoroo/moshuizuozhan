using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    public enum MatchBannerType
    {
        None = 0,
        Countdown = 1,
        Go = 2,
        OneMinute = 3,
        Finish = 4
    }

    /// <summary>
    /// 全屏比赛流程横幅视图（Match Banner View，P4-D）：
    /// 借鉴 Inkwave .iw-bn 街头潮流横幅：
    /// 1. 开赛「开战!」巨幅横幅（开赛墨幕撕开后打出）；
    /// 2. 最后 1 分钟决战横幅；
    /// 3. 「结束!」终局定格炸墨横幅。
    /// 文案统一中文短词，避免玩家读不懂长英文。
    /// </summary>
    [AddComponentMenu("P1/UI/Match Banner View")]
    [RequireComponent(typeof(RectTransform))]
    public class MatchBannerView : MonoBehaviour
    {
        [Header("主题与配置")]
        [SerializeField] private UIThemeSO theme;

        [Tooltip("已退役：主场景里可能仍序列化着 TitanOne，代码不再读取（字体统一走主题 MiSans）")]
        [HideInInspector]
        [SerializeField] private TMP_FontAsset titanOneFont;

        [Header("横幅组件")]
        [SerializeField] private RectTransform bannerRoot;
        [SerializeField] private UIInkShapeGraphic bannerSplatBg;
        [SerializeField] private TMP_Text bannerMainText;
        [SerializeField] private TMP_Text bannerSubText;
        [SerializeField] private UIBurstGraphic burstGraphic;
        [SerializeField] private ComicActionLinesGraphic actionLinesGraphic;
        [SerializeField] private UISpringMotion springMotion;

        private float _activeTimer;
        private MatchBannerType _currentType = MatchBannerType.None;
        private int _lastCountdownNumber = -1;

        public MatchBannerType CurrentType => _currentType;
        public bool IsActive => _currentType != MatchBannerType.None;

        public void SetTheme(UIThemeSO themeAsset)
        {
            if (themeAsset != null) theme = themeAsset;
        }

        private void Awake()
        {
            EnsureBuilt();
        }

        public void EnsureBuilt()
        {
            var rt = GetComponent<RectTransform>();
            if (rt == null) rt = gameObject.AddComponent<RectTransform>();
            Stretch(rt);

            UIThemeSO resolved = UIThemeSO.Resolve(theme);
            TMP_FontAsset bannerFont = resolved.GetFont(true);

            if (bannerRoot == null)
            {
                var rootGo = new GameObject("BannerContent", typeof(RectTransform), typeof(UISpringMotion));
                rootGo.transform.SetParent(transform, false);
                bannerRoot = rootGo.GetComponent<RectTransform>();
                bannerRoot.anchorMin = new Vector2(0.5f, 0.5f);
                bannerRoot.anchorMax = new Vector2(0.5f, 0.5f);
                bannerRoot.pivot = new Vector2(0.5f, 0.5f);
                bannerRoot.anchoredPosition = new Vector2(0f, 60f);
                bannerRoot.sizeDelta = new Vector2(800f, 260f);

                springMotion = rootGo.GetComponent<UISpringMotion>();
                springMotion.SetTheme(resolved);

                // 几何风禁用动作线与爆破光环
                var linesGo = new GameObject("ActionLines", typeof(RectTransform), typeof(CanvasRenderer), typeof(ComicActionLinesGraphic));
                linesGo.transform.SetParent(bannerRoot, false);
                Stretch((RectTransform)linesGo.transform, -80f);
                actionLinesGraphic = linesGo.GetComponent<ComicActionLinesGraphic>();
                actionLinesGraphic.RayCount = 24;
                actionLinesGraphic.Alpha = 0f;
                actionLinesGraphic.color = ComicStyle.Yellow;
                actionLinesGraphic.raycastTarget = false;
                linesGo.SetActive(false);

                var burstGo = new GameObject("Burst", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIBurstGraphic));
                burstGo.transform.SetParent(bannerRoot, false);
                Stretch((RectTransform)burstGo.transform, -30f);
                burstGraphic = burstGo.GetComponent<UIBurstGraphic>();
                burstGraphic.Configure(0.7f, 12, ComicStyle.Yellow);
                burstGraphic.raycastTarget = false;
                burstGo.SetActive(false);

                // 几何风直角横向纯色块（直角 SharpRect + 6px 墨边 + 无阴影 + 无网点）
                var splatGo = new GameObject("SplatBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                splatGo.transform.SetParent(bannerRoot, false);
                Stretch((RectTransform)splatGo.transform);
                bannerSplatBg = splatGo.GetComponent<UIInkShapeGraphic>();
                bannerSplatBg.Shape = UIInkShape.SharpRect;
                bannerSplatBg.CornerRadius = ComicStyle.CornerSharp;
                bannerSplatBg.BorderWidth = ComicStyle.BorderHeavy;
                bannerSplatBg.BorderColor = ComicStyle.Ink;
                bannerSplatBg.HardShadow = false;
                bannerSplatBg.ShadowOffset = Vector2.zero;
                bannerSplatBg.ShadowColor = ComicStyle.Ink;
                bannerSplatBg.Halftone = false;
                bannerSplatBg.HalftoneAlpha = 0f;
                bannerSplatBg.GlossAlpha = 0f;
                bannerSplatBg.DrawGloss = false;
                bannerSplatBg.InnerRimAlpha = 0f;
                bannerSplatBg.color = ComicStyle.Yellow;
                bannerSplatBg.raycastTarget = false;

                // 主标题超大文字
                var mainTextGo = new GameObject("MainText", typeof(RectTransform), typeof(TextMeshProUGUI));
                mainTextGo.transform.SetParent(bannerRoot, false);
                var mtRect = mainTextGo.GetComponent<RectTransform>();
                // 主标题占面板上部 68%，副标题在面板内下部 32%——旧版副标题锚在面板外 -28px，
                // 会飘在游戏画面/天空上（OneMinute 的纸白副标题直接看不见）。
                mtRect.anchorMin = new Vector2(0f, 0.32f);
                mtRect.anchorMax = Vector2.one;
                mtRect.offsetMin = new Vector2(28f, 4f);
                mtRect.offsetMax = new Vector2(-28f, -4f);

                bannerMainText = mainTextGo.GetComponent<TextMeshProUGUI>();
                if (bannerFont != null) bannerMainText.font = bannerFont;
                bannerMainText.fontSize = UITypography.FontSnapped(140f);
                bannerMainText.fontStyle = FontStyles.Bold;
                bannerMainText.alignment = TextAlignmentOptions.Center;
                bannerMainText.color = ComicStyle.Ink;
                bannerMainText.raycastTarget = false;

                // 副标题文字
                var subTextGo = new GameObject("SubText", typeof(RectTransform), typeof(TextMeshProUGUI));
                subTextGo.transform.SetParent(bannerRoot, false);
                var stRect = subTextGo.GetComponent<RectTransform>();
                stRect.anchorMin = Vector2.zero;
                stRect.anchorMax = new Vector2(1f, 0.32f);
                stRect.offsetMin = new Vector2(28f, 8f);
                stRect.offsetMax = new Vector2(-28f, 0f);

                bannerSubText = subTextGo.GetComponent<TextMeshProUGUI>();
                if (bannerFont != null) bannerSubText.font = bannerFont;
                bannerSubText.fontSize = UITypography.FontSnapped(24f);
                bannerSubText.fontStyle = FontStyles.Bold;
                bannerSubText.alignment = TextAlignmentOptions.Center;
                bannerSubText.color = ComicStyle.Ink;
                bannerSubText.raycastTarget = false;
            }

            bannerRoot.gameObject.SetActive(false);

            EnsureGraphicsHaveCanvasRenderers();
        }

        /// <summary>
        /// 旧版场景里由编辑器脚本保存的横幅子物体可能缺少 CanvasRenderer
        /// （自定义 Graphic 通过 GameObject 构造函数创建时不会自动补齐），
        /// 会在 GraphicRaycaster 首次射线检测时报 MissingComponentException。
        /// 这里在 Awake 阶段统一补齐，保证运行时零错误。
        /// </summary>
        private void EnsureGraphicsHaveCanvasRenderers()
        {
            Graphic[] graphics = GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic == null || graphic.GetComponent<CanvasRenderer>() != null)
                    continue;

                graphic.gameObject.AddComponent<CanvasRenderer>();
            }
        }

        /// <summary>已退役：旧 3-2-1 数字倒数横幅。正式流程不再调用，保留给旧场景与单测兼容。</summary>
        public void ShowCountdown(int number)
        {
            if (number == _lastCountdownNumber && _currentType == MatchBannerType.Countdown) return;
            _lastCountdownNumber = number;

            EnsureBuilt();
            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            if (number > 0)
            {
                _currentType = MatchBannerType.Countdown;
                _activeTimer = 1.0f;

                bannerRoot.anchoredPosition = new Vector2(0f, 60f);
                bannerRoot.localRotation = Quaternion.identity;
                bannerRoot.sizeDelta = new Vector2(360f, 280f);

                if (bannerSplatBg != null)
                {
                    bannerSplatBg.color = ComicStyle.Paper;
                }

                ApplyBannerText(number.ToString(), 140f, "准备", 24f, ComicStyle.Ink, ComicStyle.Ink);

                if (actionLinesGraphic != null)
                {
                    actionLinesGraphic.Alpha = 0f;
                }

                bannerRoot.gameObject.SetActive(true);
                if (springMotion != null)
                {
                    springMotion.SnapHome();
                    springMotion.PlayIn(0f);
                }
            }
            else
            {
                ShowGo();
            }
        }

        public void ShowGo()
        {
            EnsureBuilt();
            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            _currentType = MatchBannerType.Go;
            _activeTimer = 1.35f;

            bannerRoot.anchoredPosition = new Vector2(0f, 60f);
            bannerRoot.localRotation = Quaternion.identity;
            bannerRoot.sizeDelta = new Vector2(720f, 300f);

            if (bannerSplatBg != null)
            {
                bannerSplatBg.color = ComicStyle.Yellow;
            }

            ApplyBannerText("开战!", 140f, "涂满全场!", 24f, ComicStyle.Ink, ComicStyle.Ink);

            if (actionLinesGraphic != null)
            {
                actionLinesGraphic.Alpha = 0f;
            }

            bannerRoot.gameObject.SetActive(true);
            if (springMotion != null)
            {
                springMotion.SnapHome();
                springMotion.PlayIn(0f);
            }
        }

        /// <summary>
        /// 1:00 决战横幅（已退役的旧实现）。正式流程的 1:00 提示只由
        /// <see cref="CenterTimerView"/> 负责，<see cref="MatchFlowUI"/> 已不再调用本方法；
        /// 保留仅为单测与旧场景兼容。若有人重新接回本方法，同一时刻会出现两条「最后 1 分钟」提示。
        /// </summary>
        public void ShowOneMinute()
        {
            EnsureBuilt();
            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            _currentType = MatchBannerType.OneMinute;
            _activeTimer = 2.5f;

            bannerRoot.anchoredPosition = new Vector2(0f, 120f);
            bannerRoot.localRotation = Quaternion.identity;
            bannerRoot.sizeDelta = new Vector2(620f, 130f);

            if (bannerSplatBg != null)
            {
                bannerSplatBg.color = ComicStyle.Red;
            }

            ApplyBannerText("最后 1 分钟", 52f, "全力涂地!", 22f,
                ComicStyle.Paper, ComicStyle.Paper);

            if (actionLinesGraphic != null)
            {
                actionLinesGraphic.Alpha = 0f;
            }

            bannerRoot.gameObject.SetActive(true);
            if (springMotion != null)
            {
                springMotion.SnapHome();
                springMotion.PlayIn(0f);
            }
        }

        public void ShowFinish()
        {
            EnsureBuilt();
            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            _currentType = MatchBannerType.Finish;
            _activeTimer = 2.4f;

            bannerRoot.anchoredPosition = new Vector2(0f, 60f);
            bannerRoot.localRotation = Quaternion.identity;
            bannerRoot.sizeDelta = new Vector2(760f, 300f);

            if (bannerSplatBg != null)
            {
                bannerSplatBg.color = ComicStyle.Ink;
            }

            ApplyBannerText("结束!", 140f, "时间到!", 24f, ComicStyle.Paper, ComicStyle.Yellow);

            if (actionLinesGraphic != null)
            {
                actionLinesGraphic.Alpha = 0f;
            }

            bannerRoot.gameObject.SetActive(true);
            if (springMotion != null)
            {
                springMotion.SnapHome();
                springMotion.PlayIn(0f);
            }
        }

        /// <summary>
        /// 统一写主/副标题：字号上限取设计值，TMP 自动缩字保证永远落在面板内边距里。
        /// 旧版每处 Show 各写一套 fontSize，长文案（如「FINAL MINUTE · TURF NOW!」）
        /// 在移动端 ×1.28 后会撑出面板，副标题更是直接挂在面板外面。
        /// </summary>
        private void ApplyBannerText(string main, float mainSize, string sub, float subSize,
            Color mainColor, Color subColor)
        {
            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            if (bannerMainText != null)
            {
                float maxFont = UITypography.FontSnapped(mainSize);
                bannerMainText.enableAutoSizing = true;
                bannerMainText.fontSizeMin = UITypography.FontSnapped(24f);
                bannerMainText.fontSizeMax = maxFont;
                bannerMainText.fontSize = maxFont;
                bannerMainText.textWrappingMode = TextWrappingModes.NoWrap;
                bannerMainText.overflowMode = TextOverflowModes.Overflow;
                bannerMainText.font = resolved.ResolveFont(true, main);
                bannerMainText.text = main;
                bannerMainText.color = mainColor;
            }

            if (bannerSubText != null)
            {
                float maxFont = UITypography.FontSnapped(subSize);
                bannerSubText.enableAutoSizing = true;
                bannerSubText.fontSizeMin = UITypography.FontSnapped(14f);
                bannerSubText.fontSizeMax = maxFont;
                bannerSubText.fontSize = maxFont;
                bannerSubText.textWrappingMode = TextWrappingModes.NoWrap;
                bannerSubText.overflowMode = TextOverflowModes.Overflow;
                // 副标题用正文字重：标题字重只预烘了 ASCII，中文副标题会走运行时动态栅格化。
                bannerSubText.font = resolved.ResolveFont(false, sub);
                bannerSubText.text = sub;
                bannerSubText.color = subColor;
            }
        }

        public void HideImmediate()
        {
            _currentType = MatchBannerType.None;
            if (bannerRoot != null) bannerRoot.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (_currentType == MatchBannerType.None) return;

            // 倒计时在数字更新前保持可见；其他类型横幅按定时器淡出
            if (_currentType != MatchBannerType.Countdown)
            {
                _activeTimer -= Time.deltaTime;
                if (_activeTimer <= 0f)
                {
                    Dismiss();
                }
            }
        }

        private void Dismiss()
        {
            if (_currentType == MatchBannerType.None) return;
            _currentType = MatchBannerType.None;

            if (springMotion != null)
            {
                springMotion.PlayOut();
            }
            else if (bannerRoot != null)
            {
                bannerRoot.gameObject.SetActive(false);
            }
        }

        private static void Stretch(RectTransform rect, float inset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
