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
    /// 1. 3-2-1 弹性过冲大字 + 环形冲击光圈；
    /// 2. READY / GO 墨滴炸开倾斜巨幅横幅；
    /// 3. 1:00 决战横幅（ONE MINUTE LEFT!）；
    /// 4. FINISH 终局定格炸墨横幅。
    /// </summary>
    [AddComponentMenu("P1/UI/Match Banner View")]
    [RequireComponent(typeof(RectTransform))]
    public class MatchBannerView : MonoBehaviour
    {
        [Header("主题与配置")]
        [SerializeField] private UIThemeSO theme;
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

#if UNITY_EDITOR
            if (titanOneFont == null)
            {
                titanOneFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/TMP/TitanOne-Regular SDF.asset");
            }
#endif

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
                mtRect.anchorMin = Vector2.zero;
                mtRect.anchorMax = Vector2.one;
                mtRect.offsetMin = Vector2.zero;
                mtRect.offsetMax = Vector2.zero;

                bannerMainText = mainTextGo.GetComponent<TextMeshProUGUI>();
                if (titanOneFont != null) bannerMainText.font = titanOneFont;
                bannerMainText.fontSize = UITypography.FontSnapped(140f);
                bannerMainText.fontStyle = FontStyles.Bold;
                bannerMainText.alignment = TextAlignmentOptions.Center;
                bannerMainText.color = ComicStyle.Ink;
                bannerMainText.raycastTarget = false;

                // 副标题文字
                var subTextGo = new GameObject("SubText", typeof(RectTransform), typeof(TextMeshProUGUI));
                subTextGo.transform.SetParent(bannerRoot, false);
                var stRect = subTextGo.GetComponent<RectTransform>();
                stRect.anchorMin = new Vector2(0f, 0f);
                stRect.anchorMax = new Vector2(1f, 0f);
                stRect.pivot = new Vector2(0.5f, 0f);
                stRect.anchoredPosition = new Vector2(0f, -28f);
                stRect.sizeDelta = new Vector2(0f, 36f);

                bannerSubText = subTextGo.GetComponent<TextMeshProUGUI>();
                if (titanOneFont != null) bannerSubText.font = titanOneFont;
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
                bannerRoot.sizeDelta = new Vector2(360f, 260f);

                if (bannerSplatBg != null)
                {
                    bannerSplatBg.color = ComicStyle.Paper;
                }

                if (bannerMainText != null)
                {
                    bannerMainText.fontSize = UITypography.FontSnapped(140f);
                    bannerMainText.text = number.ToString();
                    bannerMainText.color = ComicStyle.Ink;
                }

                if (bannerSubText != null)
                {
                    bannerSubText.fontSize = UITypography.FontSnapped(24f);
                    bannerSubText.text = "READY...";
                    bannerSubText.color = ComicStyle.Ink;
                }

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
            bannerRoot.sizeDelta = new Vector2(720f, 280f);

            if (bannerSplatBg != null)
            {
                bannerSplatBg.color = ComicStyle.Yellow;
            }

            if (bannerMainText != null)
            {
                bannerMainText.fontSize = UITypography.FontSnapped(140f);
                bannerMainText.text = "GO!";
                bannerMainText.color = ComicStyle.Ink;
            }

            if (bannerSubText != null)
            {
                bannerSubText.fontSize = UITypography.FontSnapped(24f);
                bannerSubText.text = "TURF RIOT START!";
                bannerSubText.color = ComicStyle.Ink;
            }

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

        public void ShowOneMinute()
        {
            EnsureBuilt();
            UIThemeSO resolved = UIThemeSO.Resolve(theme);

            _currentType = MatchBannerType.OneMinute;
            _activeTimer = 2.5f;

            bannerRoot.anchoredPosition = new Vector2(0f, 120f);
            bannerRoot.localRotation = Quaternion.identity;
            bannerRoot.sizeDelta = new Vector2(620f, 110f);

            if (bannerSplatBg != null)
            {
                bannerSplatBg.color = ComicStyle.Red;
            }

            if (bannerMainText != null)
            {
                bannerMainText.fontSize = UITypography.FontSnapped(52f);
                bannerMainText.text = "1:00 LEFT!";
                bannerMainText.color = ComicStyle.Paper;
            }

            if (bannerSubText != null)
            {
                bannerSubText.fontSize = UITypography.FontSnapped(22f);
                bannerSubText.text = "FINAL MINUTE · TURF NOW!";
                bannerSubText.color = ComicStyle.Paper;
            }

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
            bannerRoot.sizeDelta = new Vector2(760f, 260f);

            if (bannerSplatBg != null)
            {
                bannerSplatBg.color = ComicStyle.Ink;
            }

            if (bannerMainText != null)
            {
                bannerMainText.fontSize = UITypography.FontSnapped(140f);
                bannerMainText.text = "FINISH!";
                bannerMainText.color = ComicStyle.Paper;
            }

            if (bannerSubText != null)
            {
                bannerSubText.fontSize = UITypography.FontSnapped(24f);
                bannerSubText.text = "TIME'S UP!";
                bannerSubText.color = ComicStyle.Yellow;
            }

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
