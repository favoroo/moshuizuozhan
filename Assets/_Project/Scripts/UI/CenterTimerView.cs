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
        private bool _bannerShown;
        private float _bannerTimer;

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

            // 最后 30 秒剧烈心跳缩放
            if (_lastRemainingSeconds > 0f && _lastRemainingSeconds <= 30f && timerBlobRoot != null)
            {
                // 3Hz 剧烈脉冲
                float pulse = 1.0f + Mathf.Abs(Mathf.Sin(Time.unscaledTime * 7.5f)) * 0.14f;
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
            if (!isPlayingOrFinished)
            {
                if (timerText != null) timerText.text = "READY";
                ResetBlobColorToNormal();
                return;
            }

            float prev = _lastRemainingSeconds;
            _lastRemainingSeconds = Mathf.Max(0f, remainingSeconds);

            var (minutes, seconds) = TopRosterMath.SplitTime(_lastRemainingSeconds);
            int totalSec = Mathf.CeilToInt(_lastRemainingSeconds);

            if (timerText != null)
            {
                timerText.text = $"{minutes}:{seconds:00}";
            }

            // 1. 最后 60 秒决战触发（60.0s 穿越）
            if (TopRosterMath.DidCrossSixtySeconds(prev, _lastRemainingSeconds) && !_bannerShown)
            {
                TriggerFinalMinuteBanner();
            }

            // 2. 状态颜色与脉冲切换
            TimerPhase phase = TopRosterMath.GetTimerPhase(_lastRemainingSeconds);
            if (phase == TimerPhase.FinalThirty)
            {
                // 最后 30 秒：变红警报 + 每秒整数跳变果冻晃动
                UIThemeSO resolvedTheme = UIThemeSO.Resolve(theme);
                Color dangerColor = resolvedTheme != null ? resolvedTheme.dangerRed : BlobFinalAlertColor;
                if (timerBlobGraphic != null)
                {
                    timerBlobGraphic.color = dangerColor;
                }
                if (timerText != null)
                {
                    timerText.color = Color.white;
                }

                if (totalSec != _lastSecFloor)
                {
                    _lastSecFloor = totalSec;
                    if (timerBlobGraphic != null)
                    {
                        timerBlobGraphic.PlayWobble(0.12f, 0.35f);
                    }
                }
            }
            else if (phase == TimerPhase.LastMinute)
            {
                // 最后 60 秒：暗紫红墨滴 + 金黄大字
                if (timerBlobGraphic != null)
                {
                    timerBlobGraphic.color = BlobLastMinuteColor;
                }
                if (timerText != null)
                {
                    timerText.color = TextLastMinuteColor;
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
                timerBlobGraphic.color = BlobNormalColor;
            }
            if (timerText != null)
            {
                timerText.color = Color.white;
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
                if (bannerBgGraphic != null)
                {
                    bannerBgGraphic.PlayWobble(0.15f, 0.45f);
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

            if (percentTextA != null) percentTextA.text = $"{valA:F1}%";
            if (percentTextB != null) percentTextB.text = $"{valB:F1}%";

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
            if (timerBlobGraphic != null && timerText != null && scoreBarRoot != null) return;

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

            rootRect.anchorMin = new Vector2(0.5f, 1f);
            rootRect.anchorMax = new Vector2(0.5f, 1f);
            rootRect.pivot = new Vector2(0.5f, 1f);
            rootRect.anchoredPosition = new Vector2(0f, -6f);
            rootRect.sizeDelta = new Vector2(240f, 96f);

            // 1. 顶部比分条 ScoreBar
            if (scoreBarRoot == null)
            {
                var scoreBarGo = new GameObject("TopScoreBar", typeof(RectTransform));
                scoreBarGo.transform.SetParent(transform, false);
                scoreBarRoot = scoreBarGo.GetComponent<RectTransform>();
                scoreBarRoot.anchorMin = new Vector2(0.5f, 1f);
                scoreBarRoot.anchorMax = new Vector2(0.5f, 1f);
                scoreBarRoot.pivot = new Vector2(0.5f, 1f);
                scoreBarRoot.anchoredPosition = new Vector2(0f, 0f);
                scoreBarRoot.sizeDelta = new Vector2(210f, 22f);

                // 黑边底座
                var bgBarGo = new GameObject("BarBackground", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bgBarGo.transform.SetParent(scoreBarRoot, false);
                var bgBarRect = bgBarGo.GetComponent<RectTransform>();
                bgBarRect.anchorMin = Vector2.zero;
                bgBarRect.anchorMax = Vector2.one;
                bgBarRect.sizeDelta = new Vector2(4f, 4f);

                var bgBarGraphic = bgBarGo.GetComponent<UIInkShapeGraphic>();
                bgBarGraphic.Shape = UIInkShape.RoundedRect;
                bgBarGraphic.CornerRadius = 11f;
                bgBarGraphic.BorderWidth = 2.5f;
                bgBarGraphic.BorderColor = resolvedTheme.outlineColor;
                bgBarGraphic.color = resolvedTheme.outlineColor;

                // Team A 填充条（左）
                var barAGo = new GameObject("FillBarA", typeof(RectTransform), typeof(Image));
                barAGo.transform.SetParent(scoreBarRoot, false);
                fillBarA = barAGo.GetComponent<RectTransform>();
                fillBarA.anchorMin = new Vector2(0.02f, 0.12f);
                fillBarA.anchorMax = new Vector2(0.5f, 0.88f);
                fillBarA.sizeDelta = Vector2.zero;

                fillImageA = barAGo.GetComponent<Image>();
                fillImageA.color = resolvedTheme.teamAColor;

                // Team B 填充条（右）
                var barBGo = new GameObject("FillBarB", typeof(RectTransform), typeof(Image));
                barBGo.transform.SetParent(scoreBarRoot, false);
                fillBarB = barBGo.GetComponent<RectTransform>();
                fillBarB.anchorMin = new Vector2(0.5f, 0.12f);
                fillBarB.anchorMax = new Vector2(0.98f, 0.88f);
                fillBarB.sizeDelta = Vector2.zero;

                fillImageB = barBGo.GetComponent<Image>();
                fillImageB.color = resolvedTheme.teamBColor;

                // 百分比文字 A
                var textAGo = new GameObject("PercentTextA", typeof(RectTransform), typeof(TextMeshProUGUI));
                textAGo.transform.SetParent(scoreBarRoot, false);
                var textARect = textAGo.GetComponent<RectTransform>();
                textARect.anchorMin = new Vector2(0f, 0.5f);
                textARect.anchorMax = new Vector2(0f, 0.5f);
                textARect.pivot = new Vector2(1f, 0.5f);
                textARect.anchoredPosition = new Vector2(-6f, 0f);
                textARect.sizeDelta = new Vector2(58f, 20f);

                percentTextA = textAGo.GetComponent<TextMeshProUGUI>();
                if (titanOneFont != null) percentTextA.font = titanOneFont;
                percentTextA.fontSize = 13f;
                percentTextA.alignment = TextAlignmentOptions.Right;
                percentTextA.color = resolvedTheme.teamAColor;
                percentTextA.text = "0.0%";

                // 百分比文字 B
                var textBGo = new GameObject("PercentTextB", typeof(RectTransform), typeof(TextMeshProUGUI));
                textBGo.transform.SetParent(scoreBarRoot, false);
                var textBRect = textBGo.GetComponent<RectTransform>();
                textBRect.anchorMin = new Vector2(1f, 0.5f);
                textBRect.anchorMax = new Vector2(1f, 0.5f);
                textBRect.pivot = new Vector2(0f, 0.5f);
                textBRect.anchoredPosition = new Vector2(6f, 0f);
                textBRect.sizeDelta = new Vector2(58f, 20f);

                percentTextB = textBGo.GetComponent<TextMeshProUGUI>();
                if (titanOneFont != null) percentTextB.font = titanOneFont;
                percentTextB.fontSize = 13f;
                percentTextB.alignment = TextAlignmentOptions.Left;
                percentTextB.color = resolvedTheme.teamBColor;
                percentTextB.text = "0.0%";
            }

            // 2. 中央墨滴计时球 TimerBlob
            if (timerBlobRoot == null)
            {
                var blobRootGo = new GameObject("TimerBlob", typeof(RectTransform), typeof(UISpringMotion));
                blobRootGo.transform.SetParent(transform, false);
                timerBlobRoot = blobRootGo.GetComponent<RectTransform>();
                timerBlobRoot.anchorMin = new Vector2(0.5f, 1f);
                timerBlobRoot.anchorMax = new Vector2(0.5f, 1f);
                timerBlobRoot.pivot = new Vector2(0.5f, 1f);
                timerBlobRoot.anchoredPosition = new Vector2(0f, -24f);
                timerBlobRoot.sizeDelta = new Vector2(136f, 62f);

                timerBlobMotion = blobRootGo.GetComponent<UISpringMotion>();
                timerBlobMotion.SetTheme(resolvedTheme);

                // 墨滴网格背景
                var blobMeshGo = new GameObject("BlobMesh", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                blobMeshGo.transform.SetParent(timerBlobRoot, false);
                var blobMeshRect = blobMeshGo.GetComponent<RectTransform>();
                blobMeshRect.anchorMin = Vector2.zero;
                blobMeshRect.anchorMax = Vector2.one;
                blobMeshRect.sizeDelta = Vector2.zero;

                timerBlobGraphic = blobMeshGo.GetComponent<UIInkShapeGraphic>();
                timerBlobGraphic.Shape = UIInkShape.Blob;
                timerBlobGraphic.BorderWidth = 4f;
                timerBlobGraphic.BorderColor = resolvedTheme.outlineColor;
                timerBlobGraphic.WobbleAmplitude = 0.065f;
                timerBlobGraphic.color = BlobNormalColor;

                // Titan One 粗体倒计时
                var txtGo = new GameObject("TimerText", typeof(RectTransform), typeof(TextMeshProUGUI));
                txtGo.transform.SetParent(timerBlobRoot, false);
                var txtRect = txtGo.GetComponent<RectTransform>();
                txtRect.anchorMin = Vector2.zero;
                txtRect.anchorMax = Vector2.one;
                txtRect.sizeDelta = Vector2.zero;

                timerText = txtGo.GetComponent<TextMeshProUGUI>();
                if (titanOneFont != null) timerText.font = titanOneFont;
                timerText.fontSize = 32f;
                timerText.alignment = TextAlignmentOptions.Center;
                timerText.color = Color.white;
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
                finalMinuteBannerRoot.anchoredPosition = new Vector2(0f, -92f);
                finalMinuteBannerRoot.sizeDelta = new Vector2(280f, 40f);

                bannerMotion = bannerGo.GetComponent<UISpringMotion>();
                bannerMotion.SetTheme(resolvedTheme);

                var bannerBgGo = new GameObject("BannerBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bannerBgGo.transform.SetParent(finalMinuteBannerRoot, false);
                var bannerBgRect = bannerBgGo.GetComponent<RectTransform>();
                bannerBgRect.anchorMin = Vector2.zero;
                bannerBgRect.anchorMax = Vector2.one;
                bannerBgRect.sizeDelta = Vector2.zero;

                bannerBgGraphic = bannerBgGo.GetComponent<UIInkShapeGraphic>();
                bannerBgGraphic.Shape = UIInkShape.RoundedRect;
                bannerBgGraphic.CornerRadius = 14f;
                bannerBgGraphic.BorderWidth = 3f;
                bannerBgGraphic.BorderColor = resolvedTheme.outlineColor;
                bannerBgGraphic.color = BlobLastMinuteColor;

                var bannerTxtGo = new GameObject("BannerText", typeof(RectTransform), typeof(TextMeshProUGUI));
                bannerTxtGo.transform.SetParent(finalMinuteBannerRoot, false);
                var bannerTxtRect = bannerTxtGo.GetComponent<RectTransform>();
                bannerTxtRect.anchorMin = Vector2.zero;
                bannerTxtRect.anchorMax = Vector2.one;
                bannerTxtRect.sizeDelta = Vector2.zero;

                bannerText = bannerTxtGo.GetComponent<TextMeshProUGUI>();
                if (titanOneFont != null) bannerText.font = titanOneFont;
                bannerText.fontSize = 18f;
                bannerText.alignment = TextAlignmentOptions.Center;
                bannerText.color = TextLastMinuteColor;
                bannerText.text = "★ 1:00 FINAL MINUTE! ★";

                finalMinuteBannerRoot.gameObject.SetActive(false);
            }
        }
    }
}
