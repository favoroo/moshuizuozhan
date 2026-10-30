using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 中央墨滴计时器与顶部比分条视图（P4-B）：
    /// 1. 墨滴造型背景（Timer Blob）：纯网格 UIInkShapeGraphic Blob；
    /// 2. Titan One 粗体字双层分秒倒计时；
    /// 3. 最后 60 秒弹出决战横幅，背景变暗紫红、文字变金黄；
    /// 4. 最后 30 秒进入警戒红剧烈跳动脉冲；
    /// 5. 顶部中央实时比分条：双方占领百分比带有 UISpringFloat 弹簧平滑插值。
    /// </summary>
    [AddComponentMenu("P1/UI/Center Timer View")]
    public class CenterTimerView : MonoBehaviour
    {
        [Header("主题与配置")]
        [SerializeField] private UIThemeSO theme;
        [SerializeField] private TMP_FontAsset titanOneFont;

        [Header("中央墨滴计时球")]
        [SerializeField] private RectTransform timerBlobRoot;
        [SerializeField] private UIInkShapeGraphic timerBlobGraphic;
        [SerializeField] private TMP_Text timerText;
        [SerializeField] private UISpringMotion timerBlobMotion;

        [Header("顶部实时比分条")]
        [SerializeField] private RectTransform scoreBarRoot;
        [SerializeField] private RectTransform fillBarA;
        [SerializeField] private RectTransform fillBarB;
        [SerializeField] private Image fillImageA;
        [SerializeField] private Image fillImageB;
        [SerializeField] private TMP_Text percentTextA;
        [SerializeField] private TMP_Text percentTextB;

        [Header("决战 60 秒横幅 (Final Minute Banner)")]
        [SerializeField] private RectTransform finalMinuteBannerRoot;
        [SerializeField] private UIInkShapeGraphic bannerBgGraphic;
        [SerializeField] private TMP_Text bannerText;
        [SerializeField] private UISpringMotion bannerMotion;

        // 弹簧插值
        private UISpringFloat _springPercentA;
        private UISpringFloat _springPercentB;

        private float _lastRemainingSeconds = -1f;
        private int _lastSecFloor = -1;
        private int _lastMinutes = -1;
        private int _lastSeconds = -1;
        private bool _lastTimerTextReady;
        private float _lastDisplayedValA = -1f;
        private float _lastDisplayedValB = -1f;
        private bool _bannerShown;
        private float _bannerTimer;
        private bool _isMobileLayout;

        private static readonly Color BlobNormalColor = new Color(0.082f, 0.071f, 0.110f, 0.95f);
        private static readonly Color BlobLastMinuteColor = new Color(0.227f, 0.102f, 0.310f, 0.98f); // #3a1a4f
        private static readonly Color TextLastMinuteColor = new Color(1f, 0.886f, 0.478f, 1f);        // #ffe27a
        private static readonly Color BlobFinalAlertColor = new Color(0.784f, 0.090f, 0.227f, 1f);    // #c8173a

        public float SmoothPercentA => _springPercentA.Value;
        public float SmoothPercentB => _springPercentB.Value;
        public float DisplayedSeconds => Mathf.Max(0f, _lastRemainingSeconds);

        public void SetTheme(UIThemeSO themeAsset)
        {
            if (themeAsset != null) theme = themeAsset;
        }

        public void SetMobileLayout(bool isMobile)
        {
            _isMobileLayout = isMobile;
            ApplyLayoutMetrics();
        }

        private void ApplyLayoutMetrics()
        {
            float timerFontSize = UITypography.FontSnapped(60f);
            Vector2 blobSize = _isMobileLayout ? new Vector2(240f, 100f) : new Vector2(250f, 104f);

            if (timerBlobRoot != null)
            {
                timerBlobRoot.sizeDelta = blobSize;
            }
            if (timerText != null)
            {
                timerText.fontSize = _lastRemainingSeconds > 0f ? timerFontSize : UITypography.FontSnapped(48f);
            }
            if (percentTextA != null)
            {
                percentTextA.fontSize = UITypography.FontSnapped(ComicStyle.FontH3);
            }
            if (percentTextB != null)
            {
                percentTextB.fontSize = UITypography.FontSnapped(ComicStyle.FontH3);
            }
        }

        private void Awake()
        {
            EnsureTimerBuilt();
        }

        private void Start()
        {
            _springPercentA.Set(0f);
            _springPercentB.Set(0f);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateBannerTimer(dt);

            // 占领百分比弹簧平滑推演
            if (scoreBarRoot != null && scoreBarRoot.gameObject.activeInHierarchy)
            {
                // 使用 Follow 弹簧预设平滑逼近
                UISpringSettings followSettings = UISpringSettings.Follow;
                _springPercentA.Step(_targetPercentA, followSettings, dt);
                _springPercentB.Step(_targetPercentB, followSettings, dt);
                ApplyScoreBarVisuals();
            }

            // 几何大胆风：倒计时使用纯色硬切，禁止摆动与抖动
            if (timerBlobRoot != null)
            {
                timerBlobRoot.localScale = Vector3.one;
            }
        }

        private float _targetPercentA;
        private float _targetPercentB;

        /// <summary>
        /// 更新双方占领百分比（带平滑弹簧推演）。
        /// </summary>
        public void SetCoverage(float percentA, float percentB)
        {
            _targetPercentA = Mathf.Clamp(percentA, 0f, 100f);
            _targetPercentB = Mathf.Clamp(percentB, 0f, 100f);
        }

        /// <summary>
        /// 更新剩余倒计时（分秒格式展示，触发 60s/30s 特效）。
        /// </summary>
        public void SetTime(float remainingSeconds, bool isPlayingOrFinished = true)
        {
            float timerFontSize = UITypography.FontSnapped(60f);

            if (!isPlayingOrFinished)
            {
                if (timerText != null && !_lastTimerTextReady)
                {
                    _lastTimerTextReady = true;
                    _lastMinutes = -1;
                    _lastSeconds = -1;
                    timerText.text = "READY";
                    timerText.fontSize = UITypography.FontSnapped(48f);
                }
                ResetBlobColorToNormal();
                return;
            }
            _lastTimerTextReady = false;

            float prev = _lastRemainingSeconds;
            _lastRemainingSeconds = Mathf.Max(0f, remainingSeconds);

            var (minutes, seconds) = TopRosterMath.SplitTime(_lastRemainingSeconds);

            if (timerText != null)
            {
                if (minutes != _lastMinutes || seconds != _lastSeconds)
                {
                    _lastMinutes = minutes;
                    _lastSeconds = seconds;
                    timerText.fontSize = timerFontSize;
                    timerText.text = $"{minutes}:{seconds:00}";
                }
            }

            // 1. 最后 60 秒决战触发（60.0s 穿越）
            if (TopRosterMath.DidCrossSixtySeconds(prev, _lastRemainingSeconds) && !_bannerShown)
            {
                TriggerFinalMinuteBanner();
            }

            // 2. 状态颜色切换（几何风：平时墨黑底白字；最后 60 秒纯色硬切 Red、白字，禁止渐变）
            TimerPhase phase = TopRosterMath.GetTimerPhase(_lastRemainingSeconds);
            if (phase == TimerPhase.FinalThirty || phase == TimerPhase.LastMinute)
            {
                if (timerBlobGraphic != null)
                {
                    timerBlobGraphic.color = ComicStyle.Red;
                }
                if (timerText != null)
                {
                    timerText.color = ComicStyle.Paper;
                }
                if (timerBlobRoot != null)
                {
                    timerBlobRoot.localScale = Vector3.one;
                }
            }
            else
            {
                ResetBlobColorToNormal();
                if (timerBlobRoot != null)
                {
                    timerBlobRoot.localScale = Vector3.one;
                }
            }
        }

        private void ResetBlobColorToNormal()
        {
            if (timerBlobGraphic != null)
            {
                timerBlobGraphic.color = ComicStyle.Ink;
            }
            if (timerText != null)
            {
                timerText.color = ComicStyle.Paper;
            }
        }

        /// <summary>
        /// 触发最后 1 分钟决战横幅弹入。
        /// </summary>
        public void TriggerFinalMinuteBanner()
        {
            _bannerShown = true;
            _bannerTimer = 2.6f;

            if (finalMinuteBannerRoot != null)
            {
                finalMinuteBannerRoot.gameObject.SetActive(true);
                if (bannerMotion != null)
                {
                    bannerMotion.PlayIn(0f);
                }
            }
        }

        private void UpdateBannerTimer(float dt)
        {
            if (!_bannerShown || _bannerTimer <= 0f) return;

            _bannerTimer -= dt;
            if (_bannerTimer <= 0f)
            {
                if (bannerMotion != null)
                {
                    bannerMotion.PlayOut();
                }
                else if (finalMinuteBannerRoot != null)
                {
                    finalMinuteBannerRoot.gameObject.SetActive(false);
                }
            }
        }

        private void ApplyScoreBarVisuals()
        {
            float valA = _springPercentA.Value;
            float valB = _springPercentB.Value;

            if (percentTextA != null && Mathf.Abs(valA - _lastDisplayedValA) >= 0.05f)
            {
                _lastDisplayedValA = valA;
                percentTextA.text = $"{valA:F1}%";
            }
            if (percentTextB != null && Mathf.Abs(valB - _lastDisplayedValB) >= 0.05f)
            {
                _lastDisplayedValB = valB;
                percentTextB.text = $"{valB:F1}%";
            }

            // 进度条宽度比例
            float ratioA = TopRosterMath.CalculateScoreSplitRatio(valA, valB);

            if (fillBarA != null && fillBarB != null)
            {
                fillBarA.anchorMax = new Vector2(ratioA, 1f);
                fillBarB.anchorMin = new Vector2(ratioA, 0f);
            }
        }

        /// <summary>
        /// 确保中央墨滴计时球、比分条与决战横幅在 UI 层级完整建立。
        /// </summary>
        public void EnsureTimerBuilt()
        {
            EnsureTimerBuilt(Application.isMobilePlatform || Screen.height <= 800);
        }

        public void EnsureTimerBuilt(bool isMobile)
        {
            _isMobileLayout = isMobile;
            if (timerBlobGraphic != null && timerText != null && scoreBarRoot != null)
            {
                ApplyLayoutMetrics();
                return;
            }

            BuildHierarchy();
        }

        private void BuildHierarchy()
        {
            UIThemeSO resolvedTheme = UIThemeSO.Resolve(theme);

#if UNITY_EDITOR
            if (titanOneFont == null)
            {
                titanOneFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/_Project/Art/Fonts/TMP/TitanOne-Regular SDF.asset");
            }
#endif

            RectTransform rootRect = GetComponent<RectTransform>();
            if (rootRect == null) rootRect = gameObject.AddComponent<RectTransform>();

            float timerFontSize = UITypography.FontSnapped(60f);
            Vector2 blobSize = _isMobileLayout ? new Vector2(240f, 100f) : new Vector2(250f, 104f);

            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -6f);
            rootRect.sizeDelta = new Vector2(300f, 140f);

            // 1. 顶部比分条 ScoreBar（直角纯黑底座 + 队伍色纯色块）
            if (scoreBarRoot == null)
            {
                var scoreBarGo = new GameObject("TopScoreBar", typeof(RectTransform));
                scoreBarGo.transform.SetParent(transform, false);
                scoreBarRoot = scoreBarGo.GetComponent<RectTransform>();
                scoreBarRoot.anchorMin = new Vector2(0.5f, 1f);
                scoreBarRoot.anchorMax = new Vector2(0.5f, 1f);
                scoreBarRoot.pivot = new Vector2(0.5f, 1f);
                // 顶部比分条必须整体落在屏幕内：锚点在父级顶边，中心下移 18px 才不会被裁掉
                // 顶部比分条必须整体落在屏幕内，且数字直接放进队伍色块里（白字，避免飘在天空上看不清）。
                scoreBarRoot.anchoredPosition = new Vector2(0f, -10f);
                scoreBarRoot.sizeDelta = new Vector2(420f, 44f);

                // 黑边底座直角块
                var bgBarGo = new GameObject("BarBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bgBarGo.transform.SetParent(scoreBarRoot, false);
                var bgBarRect = bgBarGo.GetComponent<RectTransform>();
                bgBarRect.anchorMin = Vector2.zero;
                bgBarRect.anchorMax = Vector2.one;
                bgBarRect.sizeDelta = Vector2.zero;

                var bgBarGraphic = bgBarGo.GetComponent<UIInkShapeGraphic>();
                bgBarGraphic.Shape = UIInkShape.SharpRect;
                bgBarGraphic.CornerRadius = ComicStyle.CornerSharp;
                bgBarGraphic.BorderWidth = ComicStyle.Border;
                bgBarGraphic.BorderColor = ComicStyle.Ink;
                bgBarGraphic.HardShadow = false;
                bgBarGraphic.ShadowOffset = Vector2.zero;
                bgBarGraphic.ShadowColor = ComicStyle.Ink;
                bgBarGraphic.color = ComicStyle.Ink;
                bgBarGraphic.Halftone = false;

                // Team A 填充条（左）
                var barAGo = new GameObject("FillBarA", typeof(RectTransform), typeof(Image));
                barAGo.transform.SetParent(scoreBarRoot, false);
                fillBarA = barAGo.GetComponent<RectTransform>();
                fillBarA.anchorMin = new Vector2(0.015f, 0.12f);
                fillBarA.anchorMax = new Vector2(0.5f, 0.88f);
                fillBarA.sizeDelta = Vector2.zero;

                fillImageA = barAGo.GetComponent<Image>();
                fillImageA.color = resolvedTheme.GetTeamColor(Team.TeamA);

                // Team B 填充条（右）
                var barBGo = new GameObject("FillBarB", typeof(RectTransform), typeof(Image));
                barBGo.transform.SetParent(scoreBarRoot, false);
                fillBarB = barBGo.GetComponent<RectTransform>();
                fillBarB.anchorMin = new Vector2(0.5f, 0.12f);
                fillBarB.anchorMax = new Vector2(0.985f, 0.88f);
                fillBarB.sizeDelta = Vector2.zero;

                fillImageB = barBGo.GetComponent<Image>();
                fillImageB.color = resolvedTheme.GetTeamColor(Team.TeamB);

                // 百分比文字 A（H3 级别大数字 ≥30）
                var textAGo = new GameObject("PercentTextA", typeof(RectTransform), typeof(TextMeshProUGUI));
                textAGo.transform.SetParent(scoreBarRoot, false);
                var textARect = textAGo.GetComponent<RectTransform>();
                textARect.anchorMin = new Vector2(0.02f, 0f);
                textARect.anchorMax = new Vector2(0.48f, 1f);
                textARect.pivot = new Vector2(0.5f, 0.5f);
                textARect.anchoredPosition = Vector2.zero;
                textARect.sizeDelta = Vector2.zero;

                percentTextA = textAGo.GetComponent<TextMeshProUGUI>();
                percentTextA.font = resolvedTheme.ResolveFont(true, "0.0%");
                percentTextA.fontSize = UITypography.FontSnapped(ComicStyle.FontH3);
                percentTextA.fontStyle = FontStyles.Bold;
                percentTextA.alignment = TextAlignmentOptions.Center;
                // 黑字压在队伍色块上：白字压橙对比度只有 2.7，黑字橙 7.7 / 蓝 4.6 都过 AA。
                percentTextA.color = ComicStyle.Ink;
                percentTextA.text = "0.0%";

                // 百分比文字 B（H3 级别大数字 ≥30）
                var textBGo = new GameObject("PercentTextB", typeof(RectTransform), typeof(TextMeshProUGUI));
                textBGo.transform.SetParent(scoreBarRoot, false);
                var textBRect = textBGo.GetComponent<RectTransform>();
                textBRect.anchorMin = new Vector2(0.52f, 0f);
                textBRect.anchorMax = new Vector2(0.98f, 1f);
                textBRect.pivot = new Vector2(0.5f, 0.5f);
                textBRect.anchoredPosition = Vector2.zero;
                textBRect.sizeDelta = Vector2.zero;

                percentTextB = textBGo.GetComponent<TextMeshProUGUI>();
                percentTextB.font = resolvedTheme.ResolveFont(true, "0.0%");
                percentTextB.fontSize = UITypography.FontSnapped(ComicStyle.FontH3);
                percentTextB.fontStyle = FontStyles.Bold;
                percentTextB.alignment = TextAlignmentOptions.Center;
                percentTextB.color = ComicStyle.Ink;
                percentTextB.text = "0.0%";
            }

            // 2. 中央计时器：超大数字 + 直角黑色块（无阴影、无网点、直角 SharpRect）
            if (timerBlobRoot == null)
            {
                var blobRootGo = new GameObject("TimerBlob", typeof(RectTransform), typeof(UISpringMotion));
                blobRootGo.transform.SetParent(transform, false);
                timerBlobRoot = blobRootGo.GetComponent<RectTransform>();
                timerBlobRoot.anchorMin = new Vector2(0.5f, 1f);
                timerBlobRoot.anchorMax = new Vector2(0.5f, 1f);
                timerBlobRoot.pivot = new Vector2(0.5f, 1f);
                timerBlobRoot.anchoredPosition = new Vector2(0f, -66f);
                timerBlobRoot.sizeDelta = blobSize;

                timerBlobMotion = blobRootGo.GetComponent<UISpringMotion>();
                timerBlobMotion.SetTheme(resolvedTheme);

                // 直角黑色块底
                var blobMeshGo = new GameObject("BlobMesh", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                blobMeshGo.transform.SetParent(timerBlobRoot, false);
                var blobMeshRect = blobMeshGo.GetComponent<RectTransform>();
                blobMeshRect.anchorMin = Vector2.zero;
                blobMeshRect.anchorMax = Vector2.one;
                blobMeshRect.sizeDelta = Vector2.zero;

                timerBlobGraphic = blobMeshGo.GetComponent<UIInkShapeGraphic>();
                timerBlobGraphic.Shape = UIInkShape.SharpRect;
                timerBlobGraphic.CornerRadius = ComicStyle.CornerSharp;
                timerBlobGraphic.SpeechTail = false;
                timerBlobGraphic.BorderWidth = ComicStyle.Border;
                timerBlobGraphic.BorderColor = ComicStyle.Ink;
                timerBlobGraphic.HardShadow = false;
                timerBlobGraphic.ShadowOffset = Vector2.zero;
                timerBlobGraphic.ShadowColor = ComicStyle.Ink;
                timerBlobGraphic.Halftone = false;
                timerBlobGraphic.HalftoneAlpha = 0f;
                timerBlobGraphic.GlossAlpha = 0f;
                timerBlobGraphic.DrawGloss = false;
                timerBlobGraphic.InnerRimAlpha = 0f;
                timerBlobGraphic.color = ComicStyle.Ink;

                // 超大倒计时数字（白字）
                var txtGo = new GameObject("TimerText", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtGo.transform.SetParent(timerBlobRoot, false);
                var txtRect = txtGo.GetComponent<RectTransform>();
                txtRect.anchorMin = Vector2.zero;
                txtRect.anchorMax = Vector2.one;
                txtRect.offsetMin = Vector2.zero;
                txtRect.offsetMax = Vector2.zero;

                timerText = txtGo.GetComponent<TextMeshProUGUI>();
                timerText.font = resolvedTheme.ResolveFont(true, "02:00");
                timerText.fontSize = timerFontSize;
                timerText.fontStyle = FontStyles.Bold;
                timerText.alignment = TextAlignmentOptions.Center;
                timerText.color = ComicStyle.Paper;
                timerText.text = "02:00";
            }

            // 3. 决战 60 秒横幅（直角 Red 底白字）
            if (finalMinuteBannerRoot == null)
            {
                var bannerGo = new GameObject("FinalMinuteBanner", typeof(RectTransform), typeof(UISpringMotion));
                bannerGo.transform.SetParent(transform, false);
                finalMinuteBannerRoot = bannerGo.GetComponent<RectTransform>();
                finalMinuteBannerRoot.anchorMin = new Vector2(0.5f, 1f);
                finalMinuteBannerRoot.anchorMax = new Vector2(0.5f, 1f);
                finalMinuteBannerRoot.pivot = new Vector2(0.5f, 1f);
                finalMinuteBannerRoot.anchoredPosition = new Vector2(0f, -148f);
                finalMinuteBannerRoot.sizeDelta = new Vector2(340f, 52f);

                bannerMotion = bannerGo.GetComponent<UISpringMotion>();
                bannerMotion.SetTheme(resolvedTheme);

                var bannerBgGo = new GameObject("BannerBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bannerBgGo.transform.SetParent(finalMinuteBannerRoot, false);
                var bannerBgRect = bannerBgGo.GetComponent<RectTransform>();
                bannerBgRect.anchorMin = Vector2.zero;
                bannerBgRect.anchorMax = Vector2.one;
                bannerBgRect.sizeDelta = Vector2.zero;

                bannerBgGraphic = bannerBgGo.GetComponent<UIInkShapeGraphic>();
                bannerBgGraphic.Shape = UIInkShape.SharpRect;
                bannerBgGraphic.ApplyComic(ComicStyle.Red, dots: false, border: ComicStyle.Border);
                bannerBgGraphic.CornerRadius = ComicStyle.CornerSharp;
                bannerBgGraphic.HardShadow = false;
                bannerBgGraphic.ShadowOffset = Vector2.zero;

                var bannerTxtGo = new GameObject("BannerText", typeof(RectTransform), typeof(TextMeshProUGUI));
                bannerTxtGo.transform.SetParent(finalMinuteBannerRoot, false);
                var bannerTxtRect = bannerTxtGo.GetComponent<RectTransform>();
                bannerTxtRect.anchorMin = Vector2.zero;
                bannerTxtRect.anchorMax = Vector2.one;
                bannerTxtRect.sizeDelta = Vector2.zero;

                bannerText = bannerTxtGo.GetComponent<TextMeshProUGUI>();
                bannerText.font = resolvedTheme.ResolveFont(true, "【 1:00 FINAL MINUTE! 】");
                bannerText.fontSize = UITypography.FontSnapped(24f);
                bannerText.fontStyle = FontStyles.Bold;
                bannerText.alignment = TextAlignmentOptions.Center;
                bannerText.color = ComicStyle.Paper;
                bannerText.text = "【 1:00 FINAL MINUTE! 】";

                finalMinuteBannerRoot.gameObject.SetActive(false);
            }
        }
    }
}
