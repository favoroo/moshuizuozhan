using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 比赛全流程界面控制器（P4-D 重构升级）：
    /// 1. 赛前 Ready 界面（潮流圆角大按钮 + 键盘一键开赛）；
    /// 2. 开局 3-2-1 弹性大字与 READY / GO 墨水炸开全屏横幅（MatchBannerView）；
    /// 3. 1:00 决战横幅与 FINISH 终局定格横幅；
    /// 4. 结算界面（Battle Judgement）：
    ///    - 双方墨水柱爆发对决（圆角对撞槽）；
    ///    - 百分比数字 Cubic-easeout 滚动上升；
    ///    - VICTORY / DEFEAT 胜负徽章弹性弹入；
    ///    - 一键再来一局街头果冻按钮。
    /// 保持所有原有 public 签名与 BindReferences 向后兼容。
    /// </summary>
    public class MatchFlowUI : MonoBehaviour
    {
        [Header("赛前面板")]
        [SerializeField] private GameObject readyPanel;
        [SerializeField] private Text titleText;
        [SerializeField] private Text subtitleText;
        [SerializeField] private Button startButton;
        [SerializeField] private Text startButtonLabel;

        [Header("版本与检查更新")]
        [SerializeField] private Button checkUpdateButton;
        [SerializeField] private Text versionText;

        public Button CheckUpdateButton => checkUpdateButton;
        public Text VersionText => versionText;

        [Header("既有简单文本引用（保留向后兼容）")]
        [SerializeField] private Text countdownText;
        [SerializeField] private Text finishText;

        [Header("全屏流程横幅系统（P4-D）")]
        [SerializeField] private MatchBannerView bannerView;
        [SerializeField] private UIThemeSO uiTheme;

        [Header("结算面板")]
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultTitleText;
        [SerializeField] private Image resultBarA;
        [SerializeField] private Image resultBarB;
        [SerializeField] private Text resultPercentA;
        [SerializeField] private Text resultPercentB;
        [SerializeField] private Text verdictText;
        [SerializeField] private Button restartButton;

        [Header("结算精致化徽章与效果（P4-D）")]
        [SerializeField] private RectTransform victoryBadgeRoot;
        [SerializeField] private UIInkShapeGraphic victoryBadgeBg;
        [SerializeField] private TMP_Text victoryBadgeText;
        [SerializeField] private UISpringMotion victoryBadgeMotion;
        [SerializeField] private UIBurstGraphic victoryBurst;

        private float _resultElapsed;
        private MatchManager.Phase _lastPhase = (MatchManager.Phase)(-1);
        private int _lastCountdownNumber = -1;
        private bool _oneMinuteTriggered;
        private bool _badgePlayed;

        public MatchBannerView BannerView
        {
            get
            {
                EnsureBannerView();
                return bannerView;
            }
        }

        public void BindReferences(
            GameObject ready, Text title, Text subtitle, Button start, Text startLabel,
            Text countdown, Text finish,
            GameObject result, Text resultTitle,
            Image barA, Image barB, Text percentA, Text percentB, Text verdict, Button restart)
        {
            readyPanel = ready;
            titleText = title;
            subtitleText = subtitle;
            startButton = start;
            startButtonLabel = startLabel;
            countdownText = countdown;
            finishText = finish;
            resultPanel = result;
            resultTitleText = resultTitle;
            resultBarA = barA;
            resultBarB = barB;
            resultPercentA = percentA;
            resultPercentB = percentB;
            verdictText = verdict;
            restartButton = restart;

            EnsureBannerView();
        }

        private void Awake()
        {
            EnsureBannerView();
            EnsureResultVisuals();
            EnsureUpdateUI();

            if (startButton != null) startButton.onClick.AddListener(OnStartClicked);
            if (restartButton != null) restartButton.onClick.AddListener(OnStartClicked);
        }

        private void OnDestroy()
        {
            if (startButton != null) startButton.onClick.RemoveListener(OnStartClicked);
            if (restartButton != null) restartButton.onClick.RemoveListener(OnStartClicked);
            if (checkUpdateButton != null) checkUpdateButton.onClick.RemoveListener(OnCheckUpdateClicked);
        }

        public void SetTheme(UIThemeSO themeAsset)
        {
            if (themeAsset != null)
            {
                uiTheme = themeAsset;
                if (bannerView != null) bannerView.SetTheme(uiTheme);
            }
        }

        public void EnsureBannerView()
        {
            if (bannerView == null) bannerView = GetComponentInChildren<MatchBannerView>(true);

            if (bannerView == null)
            {
                var bannerGo = new GameObject("MatchBannerView", typeof(RectTransform), typeof(MatchBannerView));
                bannerGo.transform.SetParent(transform, false);
                bannerView = bannerGo.GetComponent<MatchBannerView>();
                bannerView.SetTheme(uiTheme);
                bannerView.EnsureBuilt();
            }
        }

        private void EnsureResultVisuals()
        {
            if (resultPanel == null || victoryBadgeRoot != null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(uiTheme);

            var badgeGo = new GameObject("VictoryBadge", typeof(RectTransform), typeof(UISpringMotion));
            badgeGo.transform.SetParent(resultPanel.transform, false);
            victoryBadgeRoot = badgeGo.GetComponent<RectTransform>();
            victoryBadgeRoot.anchorMin = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.anchorMax = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.pivot = new Vector2(0.5f, 0.5f);
            victoryBadgeRoot.anchoredPosition = new Vector2(0f, -60f);
            victoryBadgeRoot.sizeDelta = new Vector2(480f, 120f);

            victoryBadgeMotion = badgeGo.GetComponent<UISpringMotion>();
            victoryBadgeMotion.SetTheme(resolved);

            var burstGo = new GameObject("Burst", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIBurstGraphic));
            burstGo.transform.SetParent(victoryBadgeRoot, false);
            var burstRect = (RectTransform)burstGo.transform;
            burstRect.anchorMin = Vector2.zero;
            burstRect.anchorMax = Vector2.one;
            burstRect.sizeDelta = Vector2.zero;
            victoryBurst = burstGo.GetComponent<UIBurstGraphic>();
            victoryBurst.Configure(0.9f, 16, resolved.readyGold);
            victoryBurst.raycastTarget = false;

            var bgGo = new GameObject("BadgeBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bgGo.transform.SetParent(victoryBadgeRoot, false);
            var bgRect = (RectTransform)bgGo.transform;
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.sizeDelta = Vector2.zero;
            victoryBadgeBg = bgGo.GetComponent<UIInkShapeGraphic>();
            victoryBadgeBg.Shape = UIInkShape.Blob;
            victoryBadgeBg.BorderWidth = 6f;
            victoryBadgeBg.BorderColor = resolved.outlineColor;
            victoryBadgeBg.color = resolved.readyGold;
            victoryBadgeBg.raycastTarget = false;

            var txtGo = new GameObject("BadgeText", typeof(RectTransform), typeof(TextMeshProUGUI));
            txtGo.transform.SetParent(victoryBadgeRoot, false);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.sizeDelta = Vector2.zero;

            victoryBadgeText = txtGo.GetComponent<TextMeshProUGUI>();
            victoryBadgeText.fontSize = 58f;
            victoryBadgeText.fontStyle = FontStyles.Bold;
            victoryBadgeText.alignment = TextAlignmentOptions.Center;
            victoryBadgeText.color = Color.white;
            victoryBadgeText.text = "VICTORY!";
            victoryBadgeText.raycastTarget = false;

            victoryBadgeRoot.gameObject.SetActive(false);
        }

        public void EnsureUpdateUI()
        {
            if (readyPanel == null) return;

            if (checkUpdateButton == null)
            {
                var existingBtn = readyPanel.transform.Find("CheckUpdateButton");
                if (existingBtn != null) checkUpdateButton = existingBtn.GetComponent<Button>();
            }

            if (versionText == null)
            {
                var existingVer = readyPanel.transform.Find("VersionText");
                if (existingVer != null) versionText = existingVer.GetComponent<Text>();
            }

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // 动态创建 VersionText
            if (versionText == null)
            {
                var verGo = new GameObject("VersionText", typeof(RectTransform), typeof(Text));
                verGo.transform.SetParent(readyPanel.transform, false);
                var verRt = verGo.GetComponent<RectTransform>();
                verRt.anchorMin = new Vector2(0.5f, 0f);
                verRt.anchorMax = new Vector2(0.5f, 0f);
                verRt.anchoredPosition = new Vector2(0f, 95f);
                verRt.sizeDelta = new Vector2(400f, 32f);

                versionText = verGo.GetComponent<Text>();
                versionText.font = font;
                versionText.fontSize = 20;
                versionText.alignment = TextAnchor.MiddleCenter;
                versionText.color = new Color(0.65f, 0.72f, 0.85f, 0.9f);
            }
            if (versionText != null)
            {
                versionText.text = $"{GameVersion.APP_NAME} {GameVersion.APP_VERSION_NAME}";
            }

            // 动态创建 CheckUpdateButton
            if (checkUpdateButton == null)
            {
                var btnGo = new GameObject("CheckUpdateButton", typeof(RectTransform), typeof(Image), typeof(Button));
                btnGo.transform.SetParent(readyPanel.transform, false);
                var btnRt = btnGo.GetComponent<RectTransform>();
                btnRt.anchorMin = new Vector2(0.5f, 0f);
                btnRt.anchorMax = new Vector2(0.5f, 0f);
                btnRt.anchoredPosition = new Vector2(0f, 48f);
                btnRt.sizeDelta = new Vector2(200f, 44f);

                var img = btnGo.GetComponent<Image>();
                img.color = new Color(0.18f, 0.22f, 0.30f, 0.92f);

                checkUpdateButton = btnGo.GetComponent<Button>();
                checkUpdateButton.targetGraphic = img;

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
                labelGo.transform.SetParent(btnGo.transform, false);
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = Vector2.zero;
                labelRt.offsetMax = Vector2.zero;

                var labelText = labelGo.GetComponent<Text>();
                labelText.font = font;
                labelText.fontSize = 20;
                labelText.fontStyle = FontStyle.Bold;
                labelText.alignment = TextAnchor.MiddleCenter;
                labelText.color = new Color(0.85f, 0.92f, 1f, 1f);
                labelText.text = "检 查 更 新";
            }

            if (checkUpdateButton != null)
            {
                checkUpdateButton.onClick.RemoveListener(OnCheckUpdateClicked);
                checkUpdateButton.onClick.AddListener(OnCheckUpdateClicked);
            }
        }

        private void OnCheckUpdateClicked()
        {
            UpdateManager.Instance?.CheckForUpdate(manual: true);
        }

        private void OnStartClicked()
        {
            MatchManager.Instance?.StartMatch();
        }

        private void Update()
        {
            var match = MatchManager.Instance;
            if (match == null) return;

            // 快捷键重开
            if (match.CurrentPhase == MatchManager.Phase.Ready || match.CurrentPhase == MatchManager.Phase.Result)
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.rKey.wasPressedThisFrame))
                {
                    match.StartMatch();
                }
            }

            // 阶段切换触发
            if (match.CurrentPhase != _lastPhase)
            {
                _lastPhase = match.CurrentPhase;
                HandlePhaseChanged(match);
            }

            if (match.CurrentPhase == MatchManager.Phase.Playing)
            {
                // 1:00 决战时刻横幅
                if (!_oneMinuteTriggered && match.RemainingSeconds <= 60f)
                {
                    _oneMinuteTriggered = true;
                    if (bannerView != null) bannerView.ShowOneMinute();
                }
            }

            if (match.CurrentPhase == MatchManager.Phase.Result && resultPanel != null && resultPanel.activeSelf)
            {
                _resultElapsed += Time.deltaTime;
            }

            ApplyPhase(match);
        }

        private void HandlePhaseChanged(MatchManager match)
        {
            var phase = match.CurrentPhase;
            if (phase == MatchManager.Phase.Countdown)
            {
                _lastCountdownNumber = -1;
                _oneMinuteTriggered = false;
                _badgePlayed = false;
                if (victoryBadgeRoot != null) victoryBadgeRoot.gameObject.SetActive(false);
            }
            else if (phase == MatchManager.Phase.Playing)
            {
                if (bannerView != null && bannerView.CurrentType == MatchBannerType.Countdown)
                {
                    bannerView.ShowGo();
                }
            }
            else if (phase == MatchManager.Phase.Finished)
            {
                if (bannerView != null)
                {
                    bannerView.ShowFinish();
                }
            }
            else if (phase == MatchManager.Phase.Result)
            {
                _resultElapsed = 0f;
                _badgePlayed = false;
                if (bannerView != null) bannerView.HideImmediate();
                PlayResultSound(match);
            }
        }

        private void ApplyPhase(MatchManager match)
        {
            var phase = match.CurrentPhase;

            if (readyPanel != null) readyPanel.SetActive(phase == MatchManager.Phase.Ready);
            if (resultPanel != null) resultPanel.SetActive(phase == MatchManager.Phase.Result);

            // 倒计时处理
            if (phase == MatchManager.Phase.Countdown)
            {
                int n = match.CountdownNumber;
                if (n != _lastCountdownNumber)
                {
                    _lastCountdownNumber = n;
                    if (bannerView != null)
                    {
                        bannerView.ShowCountdown(n);
                    }
                }
            }

            // 保持旧 Text 兼容
            if (countdownText != null)
            {
                bool showCountdown = phase == MatchManager.Phase.Countdown && bannerView == null;
                countdownText.gameObject.SetActive(showCountdown);
                if (showCountdown)
                {
                    int n = match.CountdownNumber;
                    countdownText.text = n > 0 ? n.ToString() : "GO!";
                }
            }

            if (finishText != null)
            {
                bool showFinish = phase == MatchManager.Phase.Finished && bannerView == null;
                finishText.gameObject.SetActive(showFinish);
            }

            if (phase == MatchManager.Phase.Result)
            {
                UpdateResultNumbers(match);
            }
        }

        private void UpdateResultNumbers(MatchManager match)
        {
            // Cubic-easeout 滚动算法
            float rollDuration = Mathf.Max(0.01f, match.ResultRollSeconds);
            float rollProgress = Mathf.Clamp01(_resultElapsed / rollDuration);
            float eased = 1f - Mathf.Pow(1f - rollProgress, 3f);

            float a = match.FinalCoverageA * eased;
            float b = match.FinalCoverageB * eased;

            if (resultPercentA != null) resultPercentA.text = $"{a:F1}%";
            if (resultPercentB != null) resultPercentB.text = $"{b:F1}%";
            if (resultBarA != null) resultBarA.fillAmount = Mathf.Clamp01(a / 100f);
            if (resultBarB != null) resultBarB.fillAmount = Mathf.Clamp01(b / 100f);

            if (verdictText != null)
            {
                if (rollProgress < 1f)
                {
                    verdictText.text = "正在计算全图涂地覆盖率...";
                    verdictText.color = new Color(0.85f, 0.9f, 1f, 1f);
                }
                else
                {
                    bool won = match.PlayerWon;
                    verdictText.text = won ? "★ 胜 利 获 胜 ★" : "惜 败 · 再 接 再 厉";
                    verdictText.color = won ? new Color(1f, 0.85f, 0.25f, 1f) : new Color(0.6f, 0.72f, 1f, 1f);

                    // 胜利徽章弹入
                    if (!_badgePlayed)
                    {
                        _badgePlayed = true;
                        TriggerVictoryBadge(won);
                    }
                }
            }
        }

        private void TriggerVictoryBadge(bool playerWon)
        {
            if (victoryBadgeRoot == null) return;

            UIThemeSO resolved = UIThemeSO.Resolve(uiTheme);

            if (victoryBadgeBg != null)
            {
                victoryBadgeBg.color = playerWon ? resolved.readyGold : new Color(0.35f, 0.38f, 0.45f, 0.95f);
                victoryBadgeBg.PlayWobble(0.18f, 0.6f);
            }

            if (victoryBadgeText != null)
            {
                victoryBadgeText.text = playerWon ? "VICTORY!" : "DEFEAT";
                victoryBadgeText.color = playerWon ? resolved.outlineColor : Color.white;
            }

            if (playerWon && victoryBurst != null)
            {
                victoryBurst.Play();
            }

            victoryBadgeRoot.gameObject.SetActive(true);
            if (victoryBadgeMotion != null)
            {
                victoryBadgeMotion.SnapHome();
                victoryBadgeMotion.PlayIn(0.05f);
                victoryBadgeMotion.Punch(0.18f);
            }
        }

        private void PlayResultSound(MatchManager match)
        {
            if (AudioManager.Instance == null) return;
            AudioClip clip = match.PlayerWon ? AudioManager.Instance.resultWinClip : AudioManager.Instance.resultLoseClip;
            if (clip != null)
            {
                AudioManager.Instance.PlayOne(clip, 0.85f, 1f, 1f);
            }
        }
    }
}
