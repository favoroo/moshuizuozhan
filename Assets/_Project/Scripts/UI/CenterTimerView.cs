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
            float timerFontSize = _isMobileLayout ? 68f : 80f;
            Vector2 blobSize = _isMobileLayout ? new Vector2(210f, 96f) : new Vector2(250f, 114f);

            if (timerBlobRoot != null)
            {
                timerBlobRoot.sizeDelta = blobSize;
            }
            if (timerText != null && _lastRemainingSeconds > 0f)
            {
                timerText.fontSize = timerFontSize;
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

            // 最后 30 秒剧烈心跳缩放（仅 0.08s 频率的 scale 抖动，不要弹簧）
            if (_lastRemainingSeconds > 0f && _lastRemainingSeconds <= 30f && timerBlobRoot != null)
            {
                float phase = (Time.unscaledTime % 0.08f) / 0.08f;
                float pulse = 1.0f + (phase < 0.5f ? 0.06f : 0f);
                timerBlobRoot.localScale = new Vector3(pulse, pulse, 1f);
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
            float timerFontSize = _isMobileLayout ? 68f : 80f;

            if (!isPlayingOrFinished)
            {
                if (timerText != null && !_lastTimerTextReady)
                {
                    _lastTimerTextReady = true;
                    _lastMinutes = -1;
                    _lastSeconds = -1;
                    timerText.text = "READY";
                    timerText.fontSize = _isMobileLayout ? 48f : 56f;
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

            // 2. 状态颜色与脉冲切换（纸面黑字 / 60s 黄底黑字 / 30s 红底纸字）
            TimerPhase phase = TopRosterMath.GetTimerPhase(_lastRemainingSeconds);
            if (phase == TimerPhase.FinalThirty)
            {
                // 最后 30 秒：翻红警报，白/纸面大字
                if (timerBlobGraphic != null)
                {
                    timerBlobGraphic.color = ComicStyle.Red;
                }
                if (timerText != null)
                {
                    timerText.color = ComicStyle.Paper;
                }
            }
            else if (phase == TimerPhase.LastMinute)
            {
                // 最后 60 秒：黄底气泡 + 粗黑大字
                if (timerBlobGraphic != null)
                {
                    timerBlobGraphic.color = ComicStyle.Yellow;
                }
                if (timerText != null)
                {
                    timerText.color = ComicStyle.Ink;
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
                timerBlobGraphic.color = ComicStyle.Paper;
            }
            if (timerText != null)
            {
                timerText.color = ComicStyle.Ink;
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

            float timerFontSize = _isMobileLayout ? 68f : 80f;
            Vector2 blobSize = _isMobileLayout ? new Vector2(210f, 96f) : new Vector2(250f, 114f);

            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -6f);
            rootRect.sizeDelta = new Vector2(300f, 140f);

            // 1. 顶部比分条 ScoreBar（黑边粗块 + 队伍色 + 硬阴影）
            if (scoreBarRoot == null)
            {
                var scoreBarGo = new GameObject("TopScoreBar", typeof(RectTransform));
                scoreBarGo.transform.SetParent(transform, false);
                scoreBarRoot = scoreBarGo.GetComponent<RectTransform>();
                scoreBarRoot.anchorMin = new Vector2(0.5f, 1f);
                scoreBarRoot.anchorMax = new Vector2(0.5f, 1f);
                scoreBarRoot.pivot = new Vector2(0.5f, 1f);
                // 顶部比分条必须整体落在屏幕内：锚点在父级顶边，中心下移 18px 才不会被裁掉
                scoreBarRoot.anchoredPosition = new Vector2(0f, -18f);
                scoreBarRoot.sizeDelta = new Vector2(260f, 26f);

                // 黑边底座粗块
                var bgBarGo = new GameObject("BarBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bgBarGo.transform.SetParent(scoreBarRoot, false);
                var bgBarRect = bgBarGo.GetComponent<RectTransform>();
                bgBarRect.anchorMin = Vector2.zero;
                bgBarRect.anchorMax = Vector2.one;
                bgBarRect.sizeDelta = Vector2.zero;

                var bgBarGraphic = bgBarGo.GetComponent<UIInkShapeGraphic>();
                bgBarGraphic.Shape = UIInkShape.RoundedRect;
                bgBarGraphic.CornerRadius = ComicStyle.CornerSharp;
                bgBarGraphic.BorderWidth = ComicStyle.Border;
                bgBarGraphic.BorderColor = ComicStyle.Ink;
                bgBarGraphic.HardShadow = true;
                bgBarGraphic.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
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

                // 百分比文字 A（大于 16px）
                var textAGo = new GameObject("PercentTextA", typeof(RectTransform), typeof(TextMeshProUGUI));
                textAGo.transform.SetParent(scoreBarRoot, false);
                var textARect = textAGo.GetComponent<RectTransform>();
                textARect.anchorMin = new Vector2(0f, 0.5f);
                textARect.anchorMax = new Vector2(0f, 0.5f);
                textARect.pivot = new Vector2(1f, 0.5f);
                textARect.anchoredPosition = new Vector2(-10f, 0f);
                textARect.sizeDelta = new Vector2(68f, 24f);

                percentTextA = textAGo.GetComponent<TextMeshProUGUI>();
                percentTextA.font = resolvedTheme.ResolveFont(true, "0.0%");
                percentTextA.fontSize = 18f;
                percentTextA.fontStyle = FontStyles.Bold;
                percentTextA.alignment = TextAlignmentOptions.Right;
                percentTextA.color = resolvedTheme.GetTeamColor(Team.TeamA);
                percentTextA.text = "0.0%";

                // 百分比文字 B（大于 16px）
                var textBGo = new GameObject("PercentTextB", typeof(RectTransform), typeof(TextMeshProUGUI));
                textBGo.transform.SetParent(scoreBarRoot, false);
                var textBRect = textBGo.GetComponent<RectTransform>();
                textBRect.anchorMin = new Vector2(1f, 0.5f);
                textBRect.anchorMax = new Vector2(1f, 0.5f);
                textBRect.pivot = new Vector2(0f, 0.5f);
                textBRect.anchoredPosition = new Vector2(10f, 0f);
                textBRect.sizeDelta = new Vector2(68f, 24f);

                percentTextB = textBGo.GetComponent<TextMeshProUGUI>();
                percentTextB.font = resolvedTheme.ResolveFont(true, "0.0%");
                percentTextB.fontSize = 18f;
                percentTextB.fontStyle = FontStyles.Bold;
                percentTextB.alignment = TextAlignmentOptions.Left;
                percentTextB.color = resolvedTheme.GetTeamColor(Team.TeamB);
                percentTextB.text = "0.0%";
            }

            // 2. 中央计时器：巨型 TitanOne 数字 + 纸面气泡（SpeechBubble + 下指尾）
            if (timerBlobRoot == null)
            {
                var blobRootGo = new GameObject("TimerBlob", typeof(RectTransform), typeof(UISpringMotion));
                blobRootGo.transform.SetParent(transform, false);
                timerBlobRoot = blobRootGo.GetComponent<RectTransform>();
                timerBlobRoot.anchorMin = new Vector2(0.5f, 1f);
                timerBlobRoot.anchorMax = new Vector2(0.5f, 1f);
                timerBlobRoot.pivot = new Vector2(0.5f, 1f);
                timerBlobRoot.anchoredPosition = new Vector2(0f, -54f);
                timerBlobRoot.sizeDelta = blobSize;

                timerBlobMotion = blobRootGo.GetComponent<UISpringMotion>();
                timerBlobMotion.SetTheme(resolvedTheme);

                // 纸面气泡背景（下指尾，粗墨线 6px + 实心硬阴影 (6,-6)）
                var blobMeshGo = new GameObject("BlobMesh", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                blobMeshGo.transform.SetParent(timerBlobRoot, false);
                var blobMeshRect = blobMeshGo.GetComponent<RectTransform>();
                blobMeshRect.anchorMin = Vector2.zero;
                blobMeshRect.anchorMax = Vector2.one;
                blobMeshRect.sizeDelta = Vector2.zero;

                timerBlobGraphic = blobMeshGo.GetComponent<UIInkShapeGraphic>();
                timerBlobGraphic.Shape = UIInkShape.SpeechBubble;
                timerBlobGraphic.CornerRadius = ComicStyle.CornerBubble;
                timerBlobGraphic.SpeechTail = true;
                timerBlobGraphic.TailSize = new Vector2(24f, 22f);
                timerBlobGraphic.TailAngle = 270f; // 下指尾
                timerBlobGraphic.BorderWidth = ComicStyle.Border;
                timerBlobGraphic.BorderColor = ComicStyle.Ink;
                timerBlobGraphic.HardShadow = true;
                timerBlobGraphic.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
                timerBlobGraphic.ShadowColor = ComicStyle.Ink;
                timerBlobGraphic.Halftone = true;
                timerBlobGraphic.HalftoneAlpha = 0.12f;
                timerBlobGraphic.GlossAlpha = 0f;
                timerBlobGraphic.DrawGloss = false;
                timerBlobGraphic.InnerRimAlpha = 0f;
                timerBlobGraphic.color = ComicStyle.Paper;

                // 巨型 TitanOne 倒计时数字（76~88px 桌面，64~72px 移动）
                var txtGo = new GameObject("TimerText", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtGo.transform.SetParent(timerBlobRoot, false);
                var txtRect = txtGo.GetComponent<RectTransform>();
                txtRect.anchorMin = Vector2.zero;
                txtRect.anchorMax = Vector2.one;
                txtRect.offsetMin = new Vector2(0f, 8f);
                txtRect.offsetMax = new Vector2(0f, 0f);

                timerText = txtGo.GetComponent<TextMeshProUGUI>();
                timerText.font = resolvedTheme.ResolveFont(true, "02:00");
                timerText.fontSize = timerFontSize;
                timerText.fontStyle = FontStyles.Bold;
                timerText.alignment = TextAlignmentOptions.Center;
                timerText.color = ComicStyle.Ink;
                timerText.text = "02:00";
            }

            // 3. 决战 60 秒横幅
            if (finalMinuteBannerRoot == null)
            {
                var bannerGo = new GameObject("FinalMinuteBanner", typeof(RectTransform), typeof(UISpringMotion));
                bannerGo.transform.SetParent(transform, false);
                finalMinuteBannerRoot = bannerGo.GetComponent<RectTransform>();
                finalMinuteBannerRoot.anchorMin = new Vector2(0.5f, 1f);
                finalMinuteBannerRoot.anchorMax = new Vector2(0.5f, 1f);
                finalMinuteBannerRoot.pivot = new Vector2(0.5f, 1f);
                finalMinuteBannerRoot.anchoredPosition = new Vector2(0f, -148f);
                finalMinuteBannerRoot.sizeDelta = new Vector2(320f, 48f);

                bannerMotion = bannerGo.GetComponent<UISpringMotion>();
                bannerMotion.SetTheme(resolvedTheme);

                var bannerBgGo = new GameObject("BannerBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bannerBgGo.transform.SetParent(finalMinuteBannerRoot, false);
                var bannerBgRect = bannerBgGo.GetComponent<RectTransform>();
                bannerBgRect.anchorMin = Vector2.zero;
                bannerBgRect.anchorMax = Vector2.one;
                bannerBgRect.sizeDelta = Vector2.zero;

                bannerBgGraphic = bannerBgGo.GetComponent<UIInkShapeGraphic>();
                bannerBgGraphic.ApplyComic(ComicStyle.Yellow, dots: true, border: ComicStyle.Border);
                bannerBgGraphic.CornerRadius = ComicStyle.CornerSharp;

                var bannerTxtGo = new GameObject("BannerText", typeof(RectTransform), typeof(TextMeshProUGUI));
                bannerTxtGo.transform.SetParent(finalMinuteBannerRoot, false);
                var bannerTxtRect = bannerTxtGo.GetComponent<RectTransform>();
                bannerTxtRect.anchorMin = Vector2.zero;
                bannerTxtRect.anchorMax = Vector2.one;
                bannerTxtRect.sizeDelta = Vector2.zero;

                bannerText = bannerTxtGo.GetComponent<TextMeshProUGUI>();
                bannerText.font = resolvedTheme.ResolveFont(true, "【 1:00 FINAL MINUTE! 】");
                bannerText.fontSize = 20f;
                bannerText.fontStyle = FontStyles.Bold;
                bannerText.alignment = TextAlignmentOptions.Center;
                bannerText.color = ComicStyle.Ink;
                bannerText.text = "【 1:00 FINAL MINUTE! 】";

                finalMinuteBannerRoot.gameObject.SetActive(false);
            }
        }
    }
}
