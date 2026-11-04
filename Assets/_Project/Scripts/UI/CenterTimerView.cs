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
        // 旧字段（TitanOne）已退役：字体统一由 UIThemeSO.ResolveFont 提供，场景里残留的序列化值不再读取。

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
        [SerializeField] private Image bannerIcon;
        [SerializeField] private UISpringMotion bannerMotion;

        // 比分条填充条的四边内缩（四周露出等宽墨黑边框）：左右 1.5% / 上下 12%，在 520×56 底座上约 8px / 7px。
        // ApplyScoreBarVisuals 每帧重写 x 锚点时必须保留这套内缩，否则会像历史 bug 那样
        // A 队顶边、B 队底边直接贴到面板边缘，四边框忽有忽无。
        internal const float FillInsetX = 0.015f;
        internal const float FillInsetY = 0.12f;

        // 顶部倒计时几何（2026-11-04 整体缩小一档）：旧值 260×112 / 字号 76 在 1080p 下块头过大，
        // 现在桌面 210×92 / 64，移动端球体 240×104、字号由 FontSnapped ×1.28 放大到 82。
        private const float TimerFontBaseSize = 64f;
        private const float TimerReadyFontBaseSize = 44f;
        private static readonly Vector2 TimerBlobDesktopSize = new Vector2(210f, 92f);
        private static readonly Vector2 TimerBlobMobileSize = new Vector2(240f, 104f);

        private Vector2 ResolveBlobSize()
            => _isMobileLayout ? TimerBlobMobileSize : TimerBlobDesktopSize;

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
            // 计时器档 64（移动端 ×1.28 → 82）；黑块桌面 210×92，移动端再放大一档
            float timerFontSize = UITypography.FontSnapped(TimerFontBaseSize);
            Vector2 blobSize = ResolveBlobSize();

            if (timerBlobRoot != null)
            {
                timerBlobRoot.sizeDelta = blobSize;
            }
            if (timerText != null)
            {
                timerText.fontSize = _lastRemainingSeconds > 0f ? timerFontSize : UITypography.FontSnapped(TimerReadyFontBaseSize);
            }
            if (percentTextA != null)
            {
                percentTextA.fontSize = UITypography.FontSnapped(ComicStyle.FontH2);
            }
            if (percentTextB != null)
            {
                percentTextB.fontSize = UITypography.FontSnapped(ComicStyle.FontH2);
            }

            ApplyFinalMinuteBannerMetrics(blobSize);
        }

        /// <summary>
        /// 决战横幅跟着计时球的真实高度重新落位：
        /// 旧实现把 y 固定成 -190（按桌面 112 高球体算的 8px 间距），
        /// 移动端球体加高到 124 后横幅顶边会压进 Hero 硬影与球体 4~11px。
        /// 这里以「球顶 -70 + 球高 + 16 呼吸位」推导，桌面/移动端都不会再叠。
        /// </summary>
        private void ApplyFinalMinuteBannerMetrics(Vector2 blobSize)
        {
            if (finalMinuteBannerRoot != null)
            {
                finalMinuteBannerRoot.anchoredPosition = new Vector2(0f, -(70f + blobSize.y + 16f));
                finalMinuteBannerRoot.sizeDelta = _isMobileLayout
                    ? new Vector2(560f, 104f)
                    : new Vector2(500f, 88f);
            }

            if (bannerIcon != null)
            {
                var iconRect = (RectTransform)bannerIcon.transform;
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = new Vector2(28f, 0f);
                iconRect.sizeDelta = new Vector2(30f, 30f);
            }

            if (bannerText != null)
            {
                float maxFont = UITypography.FontSnapped(26f);
                bannerText.enableAutoSizing = true;
                bannerText.fontSizeMin = UITypography.FontSnapped(18f);
                bannerText.fontSizeMax = maxFont;
                bannerText.fontSize = maxFont;
                bannerText.textWrappingMode = TextWrappingModes.NoWrap;
                bannerText.overflowMode = TextOverflowModes.Overflow;
                // 左侧 10px 黄条 + 时钟图标；无图标时退回 26px 内边距。
                float leftInset = bannerIcon != null && bannerIcon.enabled ? 54f : 26f;
                bannerText.rectTransform.offsetMin = new Vector2(leftInset, 6f);
                bannerText.rectTransform.offsetMax = new Vector2(-18f, -6f);
            }

            var self = transform as RectTransform;
            if (self != null)
            {
                self.sizeDelta = new Vector2(620f, _isMobileLayout ? 350f : 320f);
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
            if (!UICanvasVisibility.IsVisible(this, ref _visibilityCanvasChain))
                return;

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
        private Canvas[] _visibilityCanvasChain;
        private float _lastAppliedRatioA = -1f;

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
            float timerFontSize = UITypography.FontSnapped(TimerFontBaseSize);

            if (!isPlayingOrFinished)
            {
                if (timerText != null && !_lastTimerTextReady)
                {
                    _lastTimerTextReady = true;
                    _lastMinutes = -1;
                    _lastSeconds = -1;
                    timerText.text = "准备";
                    timerText.font = UIThemeSO.Resolve(theme).ResolveFont(true, "准备");
                    timerText.fontSize = UITypography.FontSnapped(TimerReadyFontBaseSize);
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

        /// <summary>本局「最后 1 分钟」横幅是否已经弹过（供测试/诊断）。</summary>
        public bool BannerShown => _bannerShown;

        /// <summary>
        /// 新一局开始（Countdown / 计时重新从整局时长开始）时复位每局一次的状态。
        /// 历史 bug——_bannerShown 置 true 后全文件无重置，第二局「最后 1 分钟」横幅再也不弹。
        /// </summary>
        public void ResetForMatch()
        {
            _bannerShown = false;
            _bannerTimer = 0f;
            _lastRemainingSeconds = -1f;
            _lastMinutes = -1;
            _lastSeconds = -1;
            _lastTimerTextReady = false;

            if (finalMinuteBannerRoot != null)
            {
                if (bannerMotion != null) bannerMotion.PlayOut();
                else finalMinuteBannerRoot.gameObject.SetActive(false);
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

            // 进度条宽度比例（只动左右分界，上下/外侧四边内缩保持不变，露出等宽墨黑边框）
            float ratioA = TopRosterMath.CalculateScoreSplitRatio(valA, valB);

            if (fillBarA != null && fillBarB != null && Mathf.Abs(ratioA - _lastAppliedRatioA) >= 0.0005f)
            {
                _lastAppliedRatioA = ratioA;
                fillBarA.anchorMin = new Vector2(FillInsetX, FillInsetY);
                fillBarA.anchorMax = new Vector2(ratioA, 1f - FillInsetY);
                fillBarB.anchorMin = new Vector2(ratioA, FillInsetY);
                fillBarB.anchorMax = new Vector2(1f - FillInsetX, 1f - FillInsetY);
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

            RectTransform rootRect = GetComponent<RectTransform>();
            if (rootRect == null) rootRect = gameObject.AddComponent<RectTransform>();

            float timerFontSize = UITypography.FontSnapped(TimerFontBaseSize);
            Vector2 blobSize = ResolveBlobSize();

            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -6f);
            rootRect.sizeDelta = new Vector2(580f, 260f);

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
                scoreBarRoot.sizeDelta = new Vector2(520f, 56f);

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
                bgBarGraphic.color = ComicStyle.Ink;
                bgBarGraphic.Halftone = false;
                // 面板档硬影 (4,-5)
                resolvedTheme.ApplyHardShadow(bgBarGraphic, UIHardShadowLevel.Panel);

                // Team A 填充条（左）——四边内缩走 FillInsetX/FillInsetY，与 ApplyScoreBarVisuals 同源
                var barAGo = new GameObject("FillBarA", typeof(RectTransform), typeof(Image));
                barAGo.transform.SetParent(scoreBarRoot, false);
                fillBarA = barAGo.GetComponent<RectTransform>();
                fillBarA.anchorMin = new Vector2(FillInsetX, FillInsetY);
                fillBarA.anchorMax = new Vector2(0.5f, 1f - FillInsetY);
                fillBarA.sizeDelta = Vector2.zero;

                fillImageA = barAGo.GetComponent<Image>();
                fillImageA.color = resolvedTheme.GetTeamColor(Team.TeamA);

                // Team B 填充条（右）——同上，四边内缩同源
                var barBGo = new GameObject("FillBarB", typeof(RectTransform), typeof(Image));
                barBGo.transform.SetParent(scoreBarRoot, false);
                fillBarB = barBGo.GetComponent<RectTransform>();
                fillBarB.anchorMin = new Vector2(0.5f, FillInsetY);
                fillBarB.anchorMax = new Vector2(1f - FillInsetX, 1f - FillInsetY);
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
                percentTextA.fontSize = UITypography.FontSnapped(ComicStyle.FontH2);
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
                percentTextB.fontSize = UITypography.FontSnapped(ComicStyle.FontH2);
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
                timerBlobRoot.anchoredPosition = new Vector2(0f, -70f);
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
                timerBlobGraphic.Halftone = false;
                timerBlobGraphic.HalftoneAlpha = 0f;
                timerBlobGraphic.GlossAlpha = 0f;
                timerBlobGraphic.DrawGloss = false;
                timerBlobGraphic.InnerRimAlpha = 0f;
                timerBlobGraphic.color = ComicStyle.Ink;
                // HUD 视觉锚点：Hero 档硬影 (6,-7)
                resolvedTheme.ApplyHardShadow(timerBlobGraphic, UIHardShadowLevel.Hero);

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
                // 初值只作占位：BuildHierarchy 末尾会按真实球高重算（桌面 198 / 移动 210）。
                finalMinuteBannerRoot.anchoredPosition = new Vector2(0f, -198f);
                finalMinuteBannerRoot.sizeDelta = new Vector2(500f, 88f);

                // 左侧行动黄竖条：让横幅从「一条红条」变成有层级的通知卡（与 HUD 卡色带同语言）。
                var accentGo = new GameObject("BannerAccent", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                accentGo.transform.SetParent(finalMinuteBannerRoot, false);
                var accentRect = accentGo.GetComponent<RectTransform>();
                accentRect.anchorMin = new Vector2(0f, 0f);
                accentRect.anchorMax = new Vector2(0f, 1f);
                accentRect.pivot = new Vector2(0f, 0.5f);
                accentRect.anchoredPosition = new Vector2(0f, 0f);
                accentRect.sizeDelta = new Vector2(10f, -ComicStyle.Border * 2f);
                var accentGraphic = accentGo.GetComponent<UIInkShapeGraphic>();
                accentGraphic.Shape = UIInkShape.SharpRect;
                accentGraphic.CornerRadius = ComicStyle.CornerSharp;
                accentGraphic.BorderWidth = 0f;
                accentGraphic.HardShadow = false;
                accentGraphic.Halftone = false;
                accentGraphic.GlossAlpha = 0f;
                accentGraphic.DrawGloss = false;
                accentGraphic.InnerRimAlpha = 0f;
                accentGraphic.color = ComicStyle.Yellow;
                accentGraphic.raycastTarget = false;

                // 时钟图标：把「只剩 1 分钟」从一行文字变成一个一眼能读的符号；取不到图标就隐藏。
                var iconGo = new GameObject("BannerIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(finalMinuteBannerRoot, false);
                bannerIcon = iconGo.GetComponent<Image>();
                bannerIcon.sprite = UIIconLibrary.Get("glyph_clock");
                bannerIcon.preserveAspect = true;
                bannerIcon.color = ComicStyle.Paper;
                bannerIcon.raycastTarget = false;
                bannerIcon.enabled = bannerIcon.sprite != null;

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
                // 横幅是浮起的信息卡：面板档硬影 (4,-5)
                resolvedTheme.ApplyHardShadow(bannerBgGraphic, UIHardShadowLevel.Panel);

                var bannerTxtGo = new GameObject("BannerText", typeof(RectTransform), typeof(TextMeshProUGUI));
                bannerTxtGo.transform.SetParent(finalMinuteBannerRoot, false);
                var bannerTxtRect = bannerTxtGo.GetComponent<RectTransform>();
                bannerTxtRect.anchorMin = Vector2.zero;
                bannerTxtRect.anchorMax = Vector2.one;
                bannerTxtRect.offsetMin = new Vector2(26f, 6f);
                bannerTxtRect.offsetMax = new Vector2(-18f, -6f);

                bannerText = bannerTxtGo.GetComponent<TextMeshProUGUI>();
                bannerText.font = resolvedTheme.ResolveFont(true, "最后 1 分钟");
                bannerText.fontSize = UITypography.FontSnapped(26f);
                bannerText.fontStyle = FontStyles.Bold;
                bannerText.alignment = TextAlignmentOptions.Center;
                bannerText.color = ComicStyle.Paper;
                bannerText.text = "最后 1 分钟 · 全力涂地!";

                finalMinuteBannerRoot.gameObject.SetActive(false);
            }

            // 横幅尺寸 / 字号 / 与计时球的间距统一走同一入口（桌面与移动端各一套几何）。
            ApplyFinalMinuteBannerMetrics(blobSize);
        }
    }
}
