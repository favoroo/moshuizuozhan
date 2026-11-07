using System.Collections;
using System.Collections.Generic;
using P1.AI;
using P1.Core;
using P1.Ink;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI.Menus
{
    /// <summary>
    /// 开局 VS 阵容卡（对标 INKWAVE intro lineup，src/ui/hud.js 的 buildIntro）：
    /// Countdown 入场演出即浮现两队「昵称 + 武器图标 + 武器名」卡片，
    /// 进入 Playing 约 1.2 秒后整层退场；终场/结算立即隐藏。
    ///
    /// 纯运行时装配（不碰场景 YAML），样式遵守 Docs/UI-GeometricBold.md：
    /// 直角纯色块 + 4px 实线墨边 + 硬影、MiSans 双字重、禁 Truncate/方块底板/白方块图标。
    /// 装配模式仿 WeaponSelectBootstrap / BotLoadoutService：RuntimeInitialize 自建主机 +
    /// MatchManager 晚就绪重试。整层是装饰：构建失败只 LogWarning，绝不能拦腰掐死一局游戏。
    /// </summary>
    [DefaultExecutionOrder(-4)]
    public class MatchIntroLineupOverlay : MonoBehaviour
    {
        public static MatchIntroLineupOverlay Instance { get; private set; }

        private const string EditorThemePath = "Assets/_Project/ScriptableObjects/UITheme.asset";
        private const int CanvasOrder = 90;

        private static readonly Vector2 CardSize = new Vector2(340f, 46f);
        private const float CardGap = 10f;
        private const float ColumnOffsetX = 430f;

        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _root;
        private UIThemeSO _theme;
        private TMP_FontAsset _displayFont;
        private TMP_FontAsset _bodyFont;

        private MatchManager _currentMatch;
        private bool _subscribedMatch;
        private float _setupRetryTimer;
        private Coroutine _showRoutine;
        private Coroutine _hideRoutine;

        // 阵容签名：BotLoadoutService 每局重建 CurrentRoster；签名变化才重做卡片树。
        private string _rosterSignature = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeAtRuntime()
        {
            if (!Application.isPlaying) return;
            if (Instance != null) return;

            var hostGo = new GameObject("[MatchIntroLineupOverlay]");
            DontDestroyOnLoad(hostGo);
            hostGo.AddComponent<MatchIntroLineupOverlay>();
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            else if (Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            BuildCanvas();
            HideImmediately();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            UnsubscribeMatch();
        }

        private void Start()
        {
            SetupSystem();
        }

        private void Update()
        {
            if (_currentMatch != null) return;

            _setupRetryTimer += Time.unscaledDeltaTime;
            if (_setupRetryTimer < 0.5f) return;
            _setupRetryTimer = 0f;
            SetupSystem();
        }

        private void SetupSystem()
        {
            MatchManager match = MatchManager.Instance != null
                ? MatchManager.Instance
                : Object.FindAnyObjectByType<MatchManager>();
            if (match == null) return;

            if (_subscribedMatch && _currentMatch == match) return;

            UnsubscribeMatch();
            _currentMatch = match;
            match.PhaseChanged += HandlePhaseChanged;
            _subscribedMatch = true;
        }

        private void UnsubscribeMatch()
        {
            if (!_subscribedMatch) return;
            if (_currentMatch != null)
                _currentMatch.PhaseChanged -= HandlePhaseChanged;
            _currentMatch = null;
            _subscribedMatch = false;
        }

        private void HandlePhaseChanged(MatchManager.Phase phase)
        {
            switch (phase)
            {
                case MatchManager.Phase.Countdown:
                    if (_showRoutine != null) StopCoroutine(_showRoutine);
                    _showRoutine = StartCoroutine(ShowAfterDraft());
                    break;

                case MatchManager.Phase.Playing:
                    if (_showRoutine != null)
                    {
                        StopCoroutine(_showRoutine);
                        _showRoutine = null;
                    }

                    if (_group != null && _group.alpha > 0.01f && _hideRoutine == null)
                        _hideRoutine = StartCoroutine(HideSoon(1.2f));
                    break;

                case MatchManager.Phase.Finished:
                case MatchManager.Phase.Result:
                    if (_group != null && _group.alpha > 0.01f)
                        HideImmediately();
                    break;
            }
        }

        /// <summary>
        /// BotLoadoutService 在 Countdown 后一帧才完成抽卡并填充 CurrentRoster，
        /// 这里多等两帧拿到完整阵容（约 33ms，墨幕还没撕开，观感无差）。
        /// </summary>
        private IEnumerator ShowAfterDraft()
        {
            yield return null;
            yield return null;
            _showRoutine = null;

            if (_currentMatch != null && _currentMatch.CurrentPhase != MatchManager.Phase.Countdown)
                yield break;

            TryShow();
        }

        private void TryShow()
        {
            if (_group == null || _root == null) return;

            IReadOnlyList<BotLoadoutService.RosterEntry> roster = BotLoadoutService.CurrentRoster;
            if (roster == null || roster.Count < 2) return; // 阵容未就绪：不显示也不报错

            try
            {
                string signature = Signature();
                if (signature != _rosterSignature)
                    RebuildCards();

                _group.alpha = 1f;

                // 重放硬切入场（UISpringMotion.PlayIn 参数是延迟秒数，逐张错开 30ms）
                for (int i = 0; i < _root.childCount; i++)
                {
                    var motion = _root.GetChild(i).GetComponent<UISpringMotion>();
                    if (motion != null)
                        motion.PlayIn(i * 0.03f);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MatchIntroLineup] 阵容卡构建失败（不影响对局）: " + e.Message);
            }
        }

        private void RebuildCards()
        {
            for (int i = _root.childCount - 1; i >= 0; i--)
                Destroy(_root.GetChild(i).gameObject);

            Team playerTeam = Team.TeamA;
            var local = P1.Player.PlayerController.LocalPlayer;
            if (local != null && local.Health != null) playerTeam = local.Health.Team;

            var allies = new List<BotLoadoutService.RosterEntry>(4);
            var enemies = new List<BotLoadoutService.RosterEntry>(4);
            IReadOnlyList<BotLoadoutService.RosterEntry> roster = BotLoadoutService.CurrentRoster;
            for (int i = 0; i < roster.Count; i++)
            {
                if (roster[i].Team == playerTeam) allies.Add(roster[i]);
                else enemies.Add(roster[i]);
            }

            BuildColumn(allies, playerTeam, 0);
            BuildColumn(enemies, Opposite(playerTeam), 1);
            BuildVersusBadge(playerTeam, Opposite(playerTeam));

            _rosterSignature = Signature();
        }

        private void BuildColumn(List<BotLoadoutService.RosterEntry> entries, Team team, int columnIndex)
        {
            if (entries.Count == 0) return;

            Color teamColor = ResolveTeamColor(team);
            bool sideLeft = columnIndex == 0;
            float columnX = sideLeft ? -ColumnOffsetX : ColumnOffsetX;
            float totalHeight = entries.Count * CardSize.y + (entries.Count - 1) * CardGap;

            for (int i = 0; i < entries.Count; i++)
            {
                BotLoadoutService.RosterEntry entry = entries[i];
                float y = totalHeight * 0.5f - (CardSize.y + CardGap) * i - CardSize.y * 0.5f + CardGap * 0.5f;

                RectTransform card = CreateRect("Card_" + (sideLeft ? "A" : "B") + i, _root,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    CardSize, new Vector2(columnX, y));

                UIInkShapeGraphic bg = UIInkShapeGraphic.CreateChildBackground(card, "Background");
                bg.Shape = UIInkShape.SharpRect;
                bg.color = _theme.panelColor;
                bg.BorderColor = ComicStyle.Ink;
                bg.BorderWidth = 4f;
                _theme.ApplyHardShadow(bg, UIHardShadowLevel.Panel);

                // 队伍色身份条（左缘竖条，直角）
                RectTransform stripe = CreateRect("TeamStripe", card,
                    new Vector2(0f, 0f), new Vector2(0f, 1f),
                    new Vector2(8f, CardSize.y - 8f), new Vector2(4f, 0f));
                Image stripeImg = EnsureImage(stripe);
                stripeImg.sprite = UIFlatSprite.White;
                stripeImg.color = teamColor;

                // 武器图标：队伍色正圆色盘 + 白色剪影（几何风禁方块底板；图标缺失必须隐藏，禁白方块）
                RectTransform plate = CreateRect("IconPlate", card,
                    new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(34f, 34f), new Vector2(22f, 0f));
                var plateShape = plate.gameObject.AddComponent<UIInkShapeGraphic>();
                plateShape.Shape = UIInkShape.Blob;
                plateShape.color = teamColor;
                plateShape.BorderColor = ComicStyle.Ink;
                plateShape.BorderWidth = 3f;

                RectTransform icon = CreateRect("Icon", plate,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new Vector2(24f, 24f), Vector2.zero);
                Image iconImg = EnsureImage(icon);
                iconImg.sprite = ResolveWeaponIcon(entry.WeaponKind);
                iconImg.color = Color.white;
                if (iconImg.sprite == null) icon.gameObject.SetActive(false);

                // 昵称（标题字重，上半行）+ 武器名（正文 muted，下半行），NoWrap 防叠字
                string nameText = entry.IsLocalPlayer ? "你" : entry.DisplayName;
                TextMeshProUGUI nameT = CreateText("Name", card, new Vector2(46f, 11f),
                    new Vector2(CardSize.x - 56f, 22f), nameText, _displayFont,
                    UITypography.FontSnapped(20f), ComicStyle.Ink,
                    TextAlignmentOptions.MidlineLeft);

                TextMeshProUGUI weaponT = CreateText("Weapon", card, new Vector2(46f, 3f),
                    new Vector2(CardSize.x - 56f, 19f), entry.WeaponDisplayName, _bodyFont,
                    UITypography.FontSnapped(18f), _theme.textMuted,
                    TextAlignmentOptions.MidlineLeft);
                weaponT.rectTransform.anchorMin = new Vector2(0f, 0f);
                weaponT.rectTransform.anchorMax = new Vector2(0f, 0f);
                weaponT.rectTransform.pivot = new Vector2(0f, 0f);

                if (entry.IsLocalPlayer)
                    BuildYouTag(card);

                card.gameObject.AddComponent<UISpringMotion>();
            }
        }

        private void BuildYouTag(RectTransform card)
        {
            RectTransform tag = CreateRect("YouTag", card,
                new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(34f, 22f), new Vector2(-12f, -12f));
            tag.pivot = new Vector2(1f, 1f);

            var tagShape = tag.gameObject.AddComponent<UIInkShapeGraphic>();
            tagShape.Shape = UIInkShape.SharpRect;
            tagShape.color = _theme.readyGold;
            tagShape.BorderColor = ComicStyle.Ink;
            tagShape.BorderWidth = 2f;

            CreateText("YouTagText", tag, Vector2.zero, new Vector2(34f, 22f),
                "你", _displayFont, UITypography.FontSnapped(18f), ComicStyle.Ink,
                TextAlignmentOptions.Center);
        }

        private void BuildVersusBadge(Team teamA, Team teamB)
        {
            RectTransform vs = CreateRect("Versus", _root,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(120f, 64f), Vector2.zero);

            UIInkShapeGraphic bg = UIInkShapeGraphic.CreateChildBackground(vs, "Background");
            bg.Shape = UIInkShape.SharpRect;
            bg.color = ComicStyle.Ink;
            bg.BorderColor = ComicStyle.Ink;
            bg.BorderWidth = 4f;

            CreateText("VsText", vs, Vector2.zero, new Vector2(120f, 64f),
                "VS", _displayFont, UITypography.FontSnapped(44f), Color.white,
                TextAlignmentOptions.Center);

            CreateDot("DotA", vs, ResolveTeamColor(teamA), new Vector2(-72f, 0f));
            CreateDot("DotB", vs, ResolveTeamColor(teamB), new Vector2(72f, 0f));

            vs.gameObject.AddComponent<UISpringMotion>();
        }

        private void CreateDot(string name, RectTransform parent, Color color, Vector2 pos)
        {
            RectTransform dot = CreateRect(name, parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(14f, 14f), pos);
            var shape = dot.gameObject.AddComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.Blob;
            shape.color = color;
            shape.BorderColor = ComicStyle.Ink;
            shape.BorderWidth = 2f;
        }

        private void BuildCanvas()
        {
            var rt = gameObject.AddComponent<RectTransform>();
            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = CanvasOrder;

            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _group = gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.interactable = false;
            _group.blocksRaycasts = false;

            _root = new GameObject("Root", typeof(RectTransform), typeof(CanvasRenderer)).transform as RectTransform;
            _root.SetParent(rt, false);
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            LoadTheme();
        }

        private void LoadTheme()
        {
            RuntimeAssetLibrary library = RuntimeAssetLibrary.Load();
            if (library != null && library.uiTheme != null)
                _theme = library.uiTheme;

#if UNITY_EDITOR
            if (_theme == null)
                _theme = UnityEditor.AssetDatabase.LoadAssetAtPath<UIThemeSO>(EditorThemePath);
#endif
            _theme = UIThemeSO.Resolve(_theme);

            _displayFont = _theme.GetFont(true);
            _bodyFont = _theme.GetFont(false);
            if (_bodyFont == null) _bodyFont = TMP_Settings.defaultFontAsset;
            if (_displayFont == null) _displayFont = _bodyFont;
        }

        private static Sprite ResolveWeaponIcon(WeaponKind kind)
        {
            RuntimeAssetLibrary library = RuntimeAssetLibrary.Load();
            Sprite icon = library != null ? library.GetWeaponIcon(kind) : null;

#if UNITY_EDITOR
            if (icon == null)
                icon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                    WeaponSelect.WeaponSelectMath.GetWeaponIconAssetPath(kind));
#endif
            return icon;
        }

        private Color ResolveTeamColor(Team team)
        {
            var surface = InkSurfaceController.Instance;
            if (surface != null)
            {
                Color c = surface.GetTeamColor(team);
                if (c.a > 0.01f) return c;
            }

            return team == Team.TeamB ? _theme.teamBColor : _theme.teamAColor;
        }

        private static Team Opposite(Team team) => team == Team.TeamB ? Team.TeamA : Team.TeamB;

        private static RectTransform CreateRect(string name, RectTransform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta, Vector2 anchoredPosition)
        {
            // 运行时 Graphic 构造红线：必须同时带 CanvasRenderer（见 AGENTS 常见坑）。
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPosition;
            return rt;
        }

        private static Image EnsureImage(RectTransform rt)
        {
            Image img = rt.GetComponent<Image>();
            if (img == null) img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        private TextMeshProUGUI CreateText(string name, RectTransform parent, Vector2 offset,
            Vector2 size, string text, TMP_FontAsset font, float fontSize, Color color,
            TextAlignmentOptions alignment)
        {
            RectTransform rt = CreateRect(name, parent,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size, offset);

            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.raycastTarget = false;
            tmp.text = text;
            return tmp;
        }

        private string Signature()
        {
            IReadOnlyList<BotLoadoutService.RosterEntry> roster = BotLoadoutService.CurrentRoster;
            var sb = new System.Text.StringBuilder(96);
            sb.Append(roster.Count);
            for (int i = 0; i < roster.Count; i++)
                sb.Append('|').Append(roster[i].DisplayName).Append((int)roster[i].WeaponKind);
            return sb.ToString();
        }

        private IEnumerator HideSoon(float seconds)
        {
            yield return new WaitForSecondsRealtime(seconds);
            _hideRoutine = null;
            HideImmediately();
        }

        private void HideImmediately()
        {
            if (_hideRoutine != null)
            {
                StopCoroutine(_hideRoutine);
                _hideRoutine = null;
            }

            if (_group != null) _group.alpha = 0f;

            _rosterSignature = string.Empty;
        }
    }
}
