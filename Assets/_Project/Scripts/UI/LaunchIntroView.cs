using System;
using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 游戏内启动动画（P1 品牌层）：Unity 启动画面之后、首页可交互之前播放一次。
    ///
    /// 演出顺序（时序见 <see cref="LaunchIntroMath"/>）：
    /// 1. 墨黑舞台 + 低透明几何装饰（不参与循环动画，只负责构图）；
    /// 2. 橙色墨点从中心炸开 → App 图标贴纸（纸白 + 墨框 + 硬影）弹入；
    /// 3. 「墨水大作战」逐字显示 + `INK TURF` 副标 + 载入块逐格填满；
    /// 4. 整块舞台向上硬擦除（底缘一道黄/蓝双色线），露出已就绪的首页。
    ///
    /// 设计约束：
    /// - 不碰场景 YAML：运行时自建独立 Overlay Canvas（sortingOrder 10000），
    ///   与 MenuBootstrap / WeaponSelectBootstrap 的「零入侵装配」一致；
    /// - 全屏底板 raycastTarget = true，保证动画期间不会点穿到首页按钮；
    /// - 只在主场景（存在 MatchManager）的首次启动出现一次；PlayMode 测试的
    ///   InitTestScene 没有 MatchManager，因此自动化不会看到它，无需测试特判；
    /// - 任意键/点击/触屏可跳过；跳过只快进到擦除段，不做瞬时消失（硬切更符合风格）；
    /// - 所有装饰性构建 / 播放都包 try-catch：启动演出失败绝不能拖垮整局游戏。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class LaunchIntroView : MonoBehaviour
    {
        private const int SortingOrder = 10000;
        private const float ReferenceWidth = 1920f;
        private const float ReferenceHeight = 1080f;
        private const int BarBlockCount = 7;

        private static readonly Color StageInk = ComicStyle.FromHex(0x0B0B0B);
        private static readonly Color BarTrack = ComicStyle.FromHex(0x2A2A2A);
        private static readonly Color TeamOrange = ComicStyle.FromHex(0xFF700F);
        private static readonly Color DecorBlue = new Color(0.15f, 0.42f, 1f, 0.16f);
        private static readonly Color DecorYellow = new Color(1f, 0.831f, 0f, 0.16f);

        private static LaunchIntroView s_instance;
        private static bool s_shown;

        /// <summary>最近一次创建的启动动画（测试/自动化可直接跳过它）。</summary>
        public static LaunchIntroView Instance => s_instance;

        /// <summary>测试或自动化可临时关掉启动演出；默认开启。</summary>
        public static bool Disabled { get; set; }

        private UIThemeSO _theme;
        private float _time;
        private bool _finished;
        private bool _playedMark;
        private bool _playedWipe;

        private RectTransform _stageRoot;
        private RectTransform _markRoot;
        private Image _splatImage;
        private RectTransform _titleRoot;
        private TMP_Text _title;
        private UIInkShapeGraphic[] _barBlocks;
        private CanvasGroup _skipHintGroup;
        private float _titlePop;
        private int _lastVisibleChars;
        private int _lastFilledBlocks;
        private bool _previewPaused;

        public bool IsFinished => _finished;
        public float ElapsedTime => _time;
        public int VisibleTitleChars => _lastVisibleChars;
        public int FilledBarBlocks => _lastFilledBlocks;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticsForPlayMode()
        {
            // 项目开启了 Enter Play Mode Options（DisableDomainReload）时静态字段不会自动归零：
            // 第一次 Play 结束后 s_shown 仍是 true，第二次进 Play 启动动画就永远不播。
            // SubsystemRegistration 每次进入 Play 都会执行，必须在这里显式重置。
            s_instance = null;
            s_shown = false;
            Disabled = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreateAtStartup()
        {
            if (!Application.isPlaying) return;

            // 只在主游戏场景（存在 MatchManager）的首次启动出现。
            // 测试框架的 InitTestScene / UIDemo / 样式画廊都不该被启动动画遮挡。
            if (UnityEngine.Object.FindAnyObjectByType<MatchManager>() == null) return;

            Create();
        }

        /// <summary>
        /// 创建启动动画。默认遵守「每局会话只播一次」；
        /// <paramref name="force"/> 仅供编辑器预览 / 测试重复取景，正式流程不要传 true。
        /// </summary>
        public static LaunchIntroView Create(bool force = false)
        {
            if (!Application.isPlaying || Disabled) return null;
            if (s_instance != null) return s_instance;
            if (s_shown && !force) return null;

            var go = new GameObject("[LaunchIntro]", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(go);

            LaunchIntroView view = go.AddComponent<LaunchIntroView>();
            s_instance = view;
            s_shown = true;
            return view;
        }

        /// <summary>测试收尾：立即结束并销毁，不等待时序。</summary>
        public static void DestroyImmediateInstance()
        {
            if (s_instance != null)
            {
                s_instance.Finish();
            }
        }

        private void Awake()
        {
            try
            {
                _theme = UIThemeSO.Resolve(RuntimeAssetLibrary.Load() != null
                    ? RuntimeAssetLibrary.Load().uiTheme
                    : null);
                Build();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[P1][LaunchIntro] 启动动画构建失败，已跳过：" + e.Message);
                Finish();
            }
        }

        private void Update()
        {
            if (_finished || _previewPaused) return;

            try
            {
                _time += Time.unscaledDeltaTime;

                if (LaunchIntroMath.CanSkip(_time) && AnySkipPressed())
                {
                    _time = LaunchIntroMath.WipeStart;
                }

                Apply(LaunchIntroMath.Evaluate(_time, _title != null ? _title.text.Length : 0));

                if (_time >= LaunchIntroMath.TotalDuration)
                {
                    Finish();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[P1][LaunchIntro] 启动动画播放异常，已跳过：" + e.Message);
                Finish();
            }
        }

        private void Finish()
        {
            if (_finished) return;
            _finished = true;
            if (s_instance == this) s_instance = null;
            Destroy(gameObject);
        }

        public void SkipNow()
        {
            if (_finished) return;
            _time = Mathf.Max(_time, LaunchIntroMath.WipeStart);
        }

        /// <summary>
        /// 编辑器/自动化取景：把画面定格在指定时刻（不推进时间）。
        /// 正式运行时不会调用；用于截图自查与 UI 回归时精确复现某一帧。
        /// </summary>
        public void SetPreviewPose(float time)
        {
            if (_finished) return;
            _previewPaused = true;
            _time = Mathf.Max(0f, time);
            Apply(LaunchIntroMath.Evaluate(_time, _title != null ? _title.text.Length : 0));
        }

        /// <summary>解除取景定格，从当前时刻继续播放。</summary>
        public void ResumeFromPreview()
        {
            _previewPaused = false;
        }

        private void Apply(LaunchIntroPose pose)
        {
            if (_markRoot != null)
            {
                _markRoot.localScale = Vector3.one * pose.MarkScale;
                _markRoot.localEulerAngles = new Vector3(0f, 0f, pose.MarkAngle);
            }

            if (_splatImage != null)
            {
                _splatImage.rectTransform.localScale = Vector3.one * pose.SplatScale;
                _splatImage.rectTransform.localEulerAngles = new Vector3(0f, 0f, pose.SplatAngle);
            }

            if (_title != null && _lastVisibleChars != pose.TitleChars)
            {
                _title.maxVisibleCharacters = pose.TitleChars;
                _lastVisibleChars = pose.TitleChars;
                _titlePop = 1f;
            }

            _titlePop = Mathf.MoveTowards(_titlePop, 0f, Time.unscaledDeltaTime * 7f);
            if (_titleRoot != null)
            {
                _titleRoot.localScale = Vector3.one * (1f + 0.035f * _titlePop);
            }

            int filled = Mathf.Clamp(Mathf.FloorToInt(pose.BarFill * BarBlockCount), 0, BarBlockCount);
            if (filled != _lastFilledBlocks && _barBlocks != null)
            {
                for (int i = 0; i < _barBlocks.Length; i++)
                {
                    if (_barBlocks[i] != null)
                    {
                        _barBlocks[i].color = i < filled ? ComicStyle.Yellow : BarTrack;
                    }
                }

                _lastFilledBlocks = filled;
            }

            if (_stageRoot != null)
            {
                _stageRoot.anchoredPosition = new Vector2(0f, pose.WipeOffset);
            }

            if (_skipHintGroup != null)
            {
                _skipHintGroup.alpha = pose.WipeProgress > 0f ? 0f : 1f;
            }

            PlayStingers(pose);
        }

        private void PlayStingers(LaunchIntroPose pose)
        {
            if (AudioManager.Instance == null) return;

            if (!_playedMark && _time >= LaunchIntroMath.MarkStart)
            {
                _playedMark = true;
                AudioManager.Instance.PlayUISound(UiSoundKind.Confirm);
            }

            if (!_playedWipe && pose.WipeProgress > 0f)
            {
                _playedWipe = true;
                AudioManager.Instance.PlayUISound(UiSoundKind.Click);
            }
        }

        // ---------------------------------------------------------------- 构建

        private void Build()
        {
            var rt = (RectTransform)transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;

            gameObject.AddComponent<GraphicRaycaster>();

            _stageRoot = CreateRect("StageRoot", transform);
            Stretch(_stageRoot);

            var stageBg = CreateImage("StageInk", _stageRoot, StageInk);
            Stretch(stageBg.rectTransform);
            stageBg.raycastTarget = true;

            BuildDecorations();
            BuildMark();
            BuildTitle();
            BuildProgressBar();
            BuildCorners();
            BuildWipeEdge();
        }

        private void BuildDecorations()
        {
            // 舞台装饰：低透明几何，只做构图张力（Docs/UI-GeometricBold.md 允许 alpha 0.10~0.18）。
            var ring = CreateShape("CobaltRing", _stageRoot, UIInkShape.Blob, new Color(0f, 0f, 0f, 0f),
                DecorBlue, 6f);
            ring.raycastTarget = false;
            var ringRt = ring.rectTransform;
            ringRt.anchorMin = ringRt.anchorMax = new Vector2(0.5f, 0.5f);
            ringRt.sizeDelta = new Vector2(720f, 720f);
            ringRt.anchoredPosition = new Vector2(-690f, -170f);

            var square = CreateShape("YellowSquare", _stageRoot, UIInkShape.SharpRect, new Color(0f, 0f, 0f, 0f),
                DecorYellow, 5f);
            square.raycastTarget = false;
            var squareRt = square.rectTransform;
            squareRt.anchorMin = squareRt.anchorMax = new Vector2(0.5f, 0.5f);
            squareRt.sizeDelta = new Vector2(300f, 300f);
            squareRt.anchoredPosition = new Vector2(760f, 300f);
            squareRt.localEulerAngles = new Vector3(0f, 0f, 13f);

            var small = CreateShape("CobaltSquare", _stageRoot, UIInkShape.SharpRect, new Color(0f, 0f, 0f, 0f),
                DecorBlue, 4f);
            small.raycastTarget = false;
            var smallRt = small.rectTransform;
            smallRt.anchorMin = smallRt.anchorMax = new Vector2(0.5f, 0.5f);
            smallRt.sizeDelta = new Vector2(150f, 150f);
            smallRt.anchoredPosition = new Vector2(-780f, 320f);
            smallRt.localEulerAngles = new Vector3(0f, 0f, -11f);
        }

        private void BuildMark()
        {
            _markRoot = CreateRect("MarkRoot", _stageRoot);
            _markRoot.anchorMin = _markRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _markRoot.sizeDelta = new Vector2(380f, 380f);
            _markRoot.anchoredPosition = new Vector2(0f, 168f);

            // 墨点：单独一层，先于贴纸炸开，贴纸弹入时墨点已就位。
            var splatGo = new GameObject("Splat", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            splatGo.transform.SetParent(_markRoot, false);
            _splatImage = splatGo.GetComponent<Image>();
            Sprite splatSprite = UIIconLibrary.Get("brand_splat");
            _splatImage.sprite = splatSprite;
            _splatImage.color = TeamOrange;
            _splatImage.raycastTarget = false;
            _splatImage.preserveAspect = true;
            splatGo.SetActive(splatSprite != null);

            var splatRt = (RectTransform)splatGo.transform;
            splatRt.anchorMin = splatRt.anchorMax = new Vector2(0.5f, 0.5f);
            splatRt.sizeDelta = new Vector2(680f, 680f);
            splatRt.anchoredPosition = Vector2.zero;
            splatRt.localEulerAngles = new Vector3(0f, 0f, -8f);

            // 贴纸：纸白 + 6px 墨框 + 弹窗档硬影（8,-9），内容用 App 图标本体。
            var sticker = CreateShape("Sticker", _markRoot, UIInkShape.SharpRect, ComicStyle.Paper,
                ComicStyle.Ink, 6f);
            sticker.ApplyHardShadow(new Vector2(8f, -9f));
            sticker.raycastTarget = false;
            var stickerRt = sticker.rectTransform;
            stickerRt.anchorMin = stickerRt.anchorMax = new Vector2(0.5f, 0.5f);
            stickerRt.sizeDelta = new Vector2(360f, 360f);
            stickerRt.anchoredPosition = Vector2.zero;

            var iconGo = new GameObject("AppIcon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconGo.transform.SetParent(stickerRt, false);
            var icon = iconGo.GetComponent<Image>();
            Sprite iconSprite = UIIconLibrary.Get("brand_app_icon");
            icon.sprite = iconSprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
            var iconRt = (RectTransform)iconGo.transform;
            Stretch(iconRt);
            iconRt.offsetMin = new Vector2(22f, 22f);
            iconRt.offsetMax = new Vector2(-22f, -22f);
            iconGo.SetActive(iconSprite != null);

            // 首帧就按起始姿态摆放，避免第一帧闪一下完整尺寸。
            _markRoot.localScale = Vector3.one * LaunchIntroMath.MarkStartScale;
            _markRoot.localEulerAngles = new Vector3(0f, 0f, 9f);
            splatRt.localScale = Vector3.one * LaunchIntroMath.SplatStartScale;
            splatRt.localEulerAngles = new Vector3(0f, 0f, -26f);
        }

        private void BuildTitle()
        {
            _titleRoot = CreateRect("TitleRoot", _stageRoot);
            _titleRoot.anchorMin = _titleRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _titleRoot.sizeDelta = new Vector2(1500f, 190f);
            _titleRoot.anchoredPosition = new Vector2(0f, -118f);

            _title = CreateText("Title", _titleRoot, _theme.ResolveFont(true, GameVersion.APP_NAME),
                GameVersion.APP_NAME, UITypography.Font(112f), ComicStyle.Paper);
            Stretch(_title.rectTransform);
            _title.alignment = TextAlignmentOptions.Center;
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            _title.overflowMode = TextOverflowModes.Overflow;
            _title.maxVisibleCharacters = 0;

            var subtitle = CreateText("Subtitle", _titleRoot,
                _theme.ResolveFont(true, "INK TURF"), "INK TURF",
                UITypography.Font(38f), ComicStyle.Yellow);
            var subRt = subtitle.rectTransform;
            subRt.anchorMin = subRt.anchorMax = new Vector2(0.5f, 0.5f);
            subRt.sizeDelta = new Vector2(1000f, 60f);
            subRt.anchoredPosition = new Vector2(0f, -104f);
            subtitle.alignment = TextAlignmentOptions.Center;
            subtitle.characterSpacing = 26f;
            subtitle.textWrappingMode = TextWrappingModes.NoWrap;
            subtitle.overflowMode = TextOverflowModes.Overflow;

            // 标题下的硬边分隔线：两头各一枚小方块，纯几何语言。
            var rule = CreateShape("TitleRule", _titleRoot, UIInkShape.SharpRect, ComicStyle.Paper,
                ComicStyle.Paper, 0f);
            rule.raycastTarget = false;
            var ruleRt = rule.rectTransform;
            ruleRt.anchorMin = ruleRt.anchorMax = new Vector2(0.5f, 0.5f);
            ruleRt.sizeDelta = new Vector2(760f, 4f);
            ruleRt.anchoredPosition = new Vector2(0f, -148f);

            var pipLeft = CreateShape("RulePipL", _titleRoot, UIInkShape.SharpRect, ComicStyle.Yellow,
                ComicStyle.Yellow, 0f);
            pipLeft.raycastTarget = false;
            var pipLeftRt = pipLeft.rectTransform;
            pipLeftRt.anchorMin = pipLeftRt.anchorMax = new Vector2(0.5f, 0.5f);
            pipLeftRt.sizeDelta = new Vector2(14f, 14f);
            pipLeftRt.anchoredPosition = new Vector2(-382f, -148f);

            var pipRight = CreateShape("RulePipR", _titleRoot, UIInkShape.SharpRect, ComicStyle.Yellow,
                ComicStyle.Yellow, 0f);
            pipRight.raycastTarget = false;
            var pipRightRt = pipRight.rectTransform;
            pipRightRt.anchorMin = pipRightRt.anchorMax = new Vector2(0.5f, 0.5f);
            pipRightRt.sizeDelta = new Vector2(14f, 14f);
            pipRightRt.anchoredPosition = new Vector2(382f, -148f);
        }

        private void BuildProgressBar()
        {
            var barRoot = CreateRect("ProgressBar", _stageRoot);
            barRoot.anchorMin = barRoot.anchorMax = new Vector2(0.5f, 0.5f);
            barRoot.sizeDelta = new Vector2(560f, 24f);
            barRoot.anchoredPosition = new Vector2(0f, -360f);

            const float blockWidth = 68f;
            const float gap = 10f;
            float total = BarBlockCount * blockWidth + (BarBlockCount - 1) * gap;
            float startX = -total * 0.5f + blockWidth * 0.5f;

            _barBlocks = new UIInkShapeGraphic[BarBlockCount];
            for (int i = 0; i < BarBlockCount; i++)
            {
                var block = CreateShape("BarBlock" + i, barRoot, UIInkShape.SharpRect, BarTrack,
                    ComicStyle.Ink, 2f);
                block.raycastTarget = false;
                var blockRt = block.rectTransform;
                blockRt.anchorMin = blockRt.anchorMax = new Vector2(0f, 0.5f);
                blockRt.sizeDelta = new Vector2(blockWidth, 18f);
                blockRt.anchoredPosition = new Vector2(startX + i * (blockWidth + gap), 0f);
                _barBlocks[i] = block;
            }

            var hint = CreateText("SkipHint", _stageRoot, _theme.ResolveFont(false, "点击跳过"),
                "点击任意处 / 按键跳过    TAP TO SKIP", UITypography.Font(24f),
                new Color(1f, 1f, 1f, 0.55f));
            var hintRt = hint.rectTransform;
            hintRt.anchorMin = hintRt.anchorMax = new Vector2(0.5f, 0.5f);
            hintRt.sizeDelta = new Vector2(1100f, 52f);
            hintRt.anchoredPosition = new Vector2(0f, -452f);
            hint.alignment = TextAlignmentOptions.Center;
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            hint.overflowMode = TextOverflowModes.Overflow;
            _skipHintGroup = hint.gameObject.AddComponent<CanvasGroup>();
            _skipHintGroup.alpha = 1f;
        }

        private void BuildCorners()
        {
            // 左上角签：黄底墨字的 TURF WAR 标签（几何风角签档位 2,-3）。
            var tag = CreateShape("Tag", _stageRoot, UIInkShape.SharpRect, ComicStyle.Yellow,
                ComicStyle.Ink, 3f);
            tag.ApplyHardShadow(ComicStyle.ShadowTag);
            tag.raycastTarget = false;
            var tagRt = tag.rectTransform;
            tagRt.anchorMin = tagRt.anchorMax = new Vector2(0f, 1f);
            tagRt.pivot = new Vector2(0f, 1f);
            tagRt.sizeDelta = new Vector2(220f, 48f);
            tagRt.anchoredPosition = new Vector2(46f, -40f);

            var tagText = CreateText("TagText", tagRt, _theme.ResolveFont(true, "TURF WAR"),
                "TURF WAR", UITypography.Font(22f), ComicStyle.Ink);
            Stretch(tagText.rectTransform);
            tagText.alignment = TextAlignmentOptions.Center;
            tagText.characterSpacing = 4f;

            var version = CreateText("Version", _stageRoot, _theme.ResolveFont(false, "INK TURF"),
                $"{GameVersion.APP_NAME_EN} {GameVersion.APP_VERSION_NAME}",
                UITypography.Font(20f), new Color(1f, 1f, 1f, 0.45f));
            var versionRt = version.rectTransform;
            versionRt.anchorMin = versionRt.anchorMax = new Vector2(1f, 0f);
            versionRt.pivot = new Vector2(1f, 0f);
            versionRt.sizeDelta = new Vector2(420f, 40f);
            versionRt.anchoredPosition = new Vector2(-46f, 34f);
            version.alignment = TextAlignmentOptions.Right;
        }

        private void BuildWipeEdge()
        {
            // 擦除前缘：黄 + 蓝双色线，贴在舞台底边；舞台整体上滑时它就是「擦窗器」的刃口。
            var yellow = CreateShape("WipeEdgeYellow", _stageRoot, UIInkShape.SharpRect,
                ComicStyle.Yellow, ComicStyle.Yellow, 0f);
            yellow.raycastTarget = false;
            var yellowRt = yellow.rectTransform;
            yellowRt.anchorMin = new Vector2(0f, 0f);
            yellowRt.anchorMax = new Vector2(1f, 0f);
            yellowRt.pivot = new Vector2(0.5f, 0f);
            yellowRt.sizeDelta = new Vector2(0f, 12f);
            yellowRt.anchoredPosition = Vector2.zero;

            var blue = CreateShape("WipeEdgeBlue", _stageRoot, UIInkShape.SharpRect,
                ComicStyle.Blue, ComicStyle.Blue, 0f);
            blue.raycastTarget = false;
            var blueRt = blue.rectTransform;
            blueRt.anchorMin = new Vector2(0f, 0f);
            blueRt.anchorMax = new Vector2(1f, 0f);
            blueRt.pivot = new Vector2(0.5f, 0f);
            blueRt.sizeDelta = new Vector2(0f, 5f);
            blueRt.anchoredPosition = new Vector2(0f, 12f);
        }

        // ---------------------------------------------------------------- 小工具

        private static bool AnySkipPressed()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame) return true;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) return true;

            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null &&
                (pad.buttonSouth.wasPressedThisFrame || pad.startButton.wasPressedThisFrame))
                return true;

            return false;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private static Image CreateImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static UIInkShapeGraphic CreateShape(string name, Transform parent, UIInkShape shape,
            Color fill, Color border, float borderWidth)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);

            var graphic = go.GetComponent<UIInkShapeGraphic>();
            graphic.Shape = shape;
            graphic.CornerRadius = ComicStyle.CornerSharp;
            graphic.BorderWidth = borderWidth;
            graphic.BorderColor = border;
            graphic.color = fill;
            graphic.Halftone = false;
            graphic.raycastTarget = false;
            return graphic;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, TMP_FontAsset font,
            string text, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            return tmp;
        }
    }
}