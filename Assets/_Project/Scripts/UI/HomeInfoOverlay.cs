using System;
using P1.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 首页「战绩 / 键位说明」详情浮层（运行时装配，不改场景 YAML）：
    /// - 全屏墨色遮罩 + 居中纸白面板（4px 墨框 + Dialog 硬影），点击遮罩 / 关闭键 / Esc 退出；
    /// - 左侧身份卡 + 右侧 2×3 数据格（战绩），或左右两栏键位 + 底部规则速记（键位说明）；
    /// - 面板尺寸与字号来自 <see cref="HomeInfoLayoutMath"/>，内容来自 <see cref="HomeInfoContent"/>；
    /// - 静态 <see cref="IsOpen"/> / <see cref="MenuInputConsumedThisFrame"/> 给首页开赛快捷键、
    ///   暂停菜单与武器抽屉做同帧输入仲裁（一个 Esc 不能既关浮层又开暂停/抽屉）。
    /// </summary>
    [DisallowMultipleComponent]
    public class HomeInfoOverlay : MonoBehaviour
    {
        private static HomeInfoOverlay _instance;

        public static bool IsOpen => _instance != null;

        /// <summary>
        /// 本帧的 Esc / Start 已被某个菜单层消费，其他菜单输入应直接返回。
        /// 消费位统一放在 <see cref="MenuInputRouter"/>（首页曾出现「一个 Esc 同时开暂停 + 收放武器抽屉」）。
        /// </summary>
        public static bool MenuInputConsumedThisFrame => MenuInputRouter.ConsumedThisFrame;

        private RectTransform _host;
        private UIThemeSO _theme;
        private HomeInfoTab _tab;
        private int _lastScreenWidth;
        private int _lastScreenHeight;
        private Rect _lastSafeArea;

        public static HomeInfoOverlay Show(RectTransform host, UIThemeSO theme, HomeInfoTab tab, int sortingOrder = 50)
        {
            if (host == null) return null;

            CloseActive();

            // 独立 Overlay Canvas（默认 sortingOrder 50）：必须高于武器抽屉画布（15）与 HUD（10），
            // 否则首页左下「武器整备」卡会浮在遮罩之上、还能被点开抽屉。
            // 从暂停设置页打开指南时传 110，必须高于暂停 Canvas（100），否则会被设置面板盖住。
            var go = new GameObject("HomeInfoOverlay");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(MenuLayoutMath.RefWidth, MenuLayoutMath.RefHeight);
            scaler.matchWidthOrHeight = 0.5f;

            go.AddComponent<GraphicRaycaster>();

            var overlay = go.AddComponent<HomeInfoOverlay>();
            overlay._host = host;
            overlay._theme = UIThemeSO.Resolve(theme);
            overlay._tab = tab;
            overlay.Rebuild();
            overlay.CacheGeometry();
            _instance = overlay;
            return overlay;
        }

        public static void CloseIfOpen()
        {
            if (_instance != null) _instance.Close();
        }

        public static void CloseActive()
        {
            if (_instance != null) _instance.Close();
        }

        /// <summary>
        /// Esc / 手柄 Start 的统一入口：浮层打开时关闭并标记本帧已消费；
        /// 浮层已关闭但本帧刚被（任何菜单层）消费时返回 true，调用方不得再处理同一个输入。
        /// </summary>
        public static bool TryCloseFromMenuInput()
        {
            if (_instance != null)
            {
                _instance.Close();
                MenuInputRouter.MarkConsumed(MenuEscLayer.HomeInfo);
                return true;
            }

            return MenuInputRouter.ConsumedThisFrame;
        }

        private void Close()
        {
            if (_instance == this) _instance = null;
            ClearSelection();
            if (gameObject != null)
            {
                gameObject.SetActive(false);
                Destroy(gameObject);
            }
        }

        private void OnDisable()
        {
            // readyPanel 被对局关掉时浮层会跟着失活：静态 IsOpen 必须同步归零，
            // 否则首页开赛快捷键与暂停菜单会一直以为浮层还开着。
            if (_instance == this) _instance = null;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        private void Update()
        {
            if (Screen.width == _lastScreenWidth && Screen.height == _lastScreenHeight &&
                Screen.safeArea == _lastSafeArea)
            {
                return;
            }

            CacheGeometry();
            Rebuild();
        }

        private void CacheGeometry()
        {
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
            _lastSafeArea = Screen.safeArea;
        }

        private void Rebuild()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            BuildRoot();
        }

        // ==================== 根布局 ====================

        private void BuildRoot()
        {
            bool mobile = UITypography.IsMobileLike;
            HomeInfoMetrics m = HomeInfoLayoutMath.Metrics(mobile);
            Vector2 panelSize = ResolvePanelSize(mobile);

            // 1) 全屏遮罩：墨黑半透明 + raycastTarget，吃掉首页所有点击；点空白处关闭。
            var scrimGo = new GameObject("Scrim", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            scrimGo.transform.SetParent(transform, false);
            Stretch((RectTransform)scrimGo.transform);

            var scrimImage = scrimGo.GetComponent<Image>();
            scrimImage.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.74f);
            scrimImage.raycastTarget = true;

            var scrimButton = scrimGo.GetComponent<Button>();
            scrimButton.transition = Selectable.Transition.None;
            scrimButton.onClick.AddListener(Close);

            // 2) 居中面板：纸白 + 墨框 + 弹窗硬影。锚点跟随安全区中心，刘海屏不偏。
            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(UISpringMotion));
            panelGo.transform.SetParent(transform, false);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = ResolveSafeCenter();
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = panelSize;

            var panelShape = panelGo.GetComponent<UIInkShapeGraphic>();
            panelShape.Shape = UIInkShape.RoundedRect; // 圆角化：浮层面板自适应圆角，硬影同形状跟随
            panelShape.BorderWidth = ComicStyle.Border;
            panelShape.BorderColor = ComicStyle.Ink;
            panelShape.color = ComicStyle.Paper;
            panelShape.GlossAlpha = 0f;
            panelShape.Halftone = false;
            panelShape.raycastTarget = true;
            _theme.ApplyHardShadow(panelShape, UIHardShadowLevel.Dialog);

            var motion = panelGo.GetComponent<UISpringMotion>();
            motion.SetTheme(_theme);
            motion.Build();
            motion.SnapHome();
            motion.PlayIn();

            bool achievements = _tab == HomeInfoTab.Achievements;
            bool recordLike = _tab == HomeInfoTab.Record || achievements;
            Color accent = achievements ? ComicStyle.Yellow
                : (_tab == HomeInfoTab.Record ? ComicStyle.Green : ComicStyle.Blue);
            string tabTag = achievements ? "ACHIEVEMENTS"
                : (_tab == HomeInfoTab.Record ? "RECORD" : (mobile ? "GUIDE" : "KEYS"));
            string title = achievements ? "成就" : (_tab == HomeInfoTab.Record ? "战绩" : (mobile ? "操作指南" : "键位说明"));
            string subtitle = achievements ? "ACHIEVEMENTS"
                : (_tab == HomeInfoTab.Record ? "CAREER RECORD" : (mobile ? "TOUCH CONTROLS" : "CONTROLS"));

            BuildHeader(panel, m, mobile, panelSize, accent, tabTag, title, subtitle);

            float contentTop = m.HeaderHeight + m.ContentInset;
            float contentHeight = Mathf.Max(160f, panelSize.y - contentTop - m.FooterHeight);

            if (recordLike)
            {
                // 战绩 / 成就共用一个浮层页签条：一键互换，不用回首页重开。
                float switcherHeight = BuildTabSwitcher(panel, m, contentTop);
                contentTop += switcherHeight;
                contentHeight = Mathf.Max(120f, contentHeight - switcherHeight);

                if (achievements)
                {
                    BuildAchievementsView(panel, m, mobile, panelSize, contentTop, contentHeight);
                }
                else
                {
                    BuildRecordView(panel, m, mobile, panelSize, contentTop, contentHeight);
                }
            }
            else
            {
                BuildControlsView(panel, m, mobile, panelSize, contentTop, contentHeight);
            }

            BuildFooter(panel, m, mobile, panelSize);

            // 手柄：默认选中关闭键，A 直接退出；鼠标/触屏不受影响。
            if (EventSystem.current != null && CloseButton != null)
            {
                EventSystem.current.SetSelectedGameObject(CloseButton.gameObject);
            }
        }

        private Button CloseButton { get; set; }

        private Vector2 ResolvePanelSize(bool mobile)
        {
            Rect hostRect = _host != null ? _host.rect : new Rect(0f, 0f, MenuLayoutMath.RefWidth, MenuLayoutMath.RefHeight);
            float hostWidth = hostRect.width > 1f ? hostRect.width : MenuLayoutMath.RefWidth;
            float hostHeight = hostRect.height > 1f ? hostRect.height : MenuLayoutMath.RefHeight;

            Rect safe = Screen.safeArea;
            float safeWidth = hostWidth;
            float safeHeight = hostHeight;
            if (Screen.width > 0 && Screen.height > 0)
            {
                safeWidth = hostWidth * Mathf.Clamp01(safe.width / Screen.width);
                safeHeight = hostHeight * Mathf.Clamp01(safe.height / Screen.height);
            }

            return HomeInfoLayoutMath.ResolvePanelSize(mobile, safeWidth, safeHeight);
        }

        private Vector2 ResolveSafeCenter()
        {
            Rect safe = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0)
                return new Vector2(0.5f, 0.5f);

            float x = (safe.x + safe.width * 0.5f) / Screen.width;
            float y = (safe.y + safe.height * 0.5f) / Screen.height;
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        }

        // ==================== 头部 / 页脚 ====================

        private void BuildHeader(RectTransform panel, HomeInfoMetrics m, bool mobile, Vector2 panelSize,
            Color accent, string tagLabel, string title, string subtitle)
        {
            float innerWidth = panelSize.x - m.Padding * 2f;

            // 顶部身份色带（不承载文字）。圆角化：内缩到圆角直线段内并改胶囊短划
            float panelRadius = ComicStyle.CornerFor(panelSize.x, panelSize.y);
            var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(panel, false);
            var band = (RectTransform)bandGo.transform;
            band.anchorMin = new Vector2(0f, 1f);
            band.anchorMax = new Vector2(1f, 1f);
            band.pivot = new Vector2(0.5f, 1f);
            band.sizeDelta = new Vector2(-(panelRadius + ComicStyle.Border) * 2f, m.TopBandHeight);
            band.anchoredPosition = new Vector2(0f, -ComicStyle.Border);

            var bandShape = bandGo.GetComponent<UIInkShapeGraphic>();
            bandShape.Shape = UIInkShape.Pill;
            bandShape.BorderWidth = 0f;
            bandShape.color = accent;
            bandShape.HardShadow = false;
            bandShape.Halftone = false;
            bandShape.raycastTarget = false;

            float top = m.Padding + m.TopBandHeight;
            CreateTag(panel, "TabTag", tagLabel, m.TagSize, UITypography.FontSnapped(m.TagFont),
                ComicStyle.Ink, ComicStyle.Paper, new Vector2(m.Padding, top));

            float titleTop = top + m.TagSize.y + 12f;
            float titleFont = UITypography.FontSnapped(m.TitleFont);
            CreateText(panel, "Title", title, titleFont, true, ComicStyle.Ink,
                new Vector2(m.Padding, titleTop),
                new Vector2(innerWidth - m.CloseSize.x - 24f, titleFont * 1.25f),
                TextAlignmentOptions.MidlineLeft, 1f, FontStyles.Bold);

            float subtitleFont = UITypography.FontSnapped(m.SubtitleFont);
            CreateText(panel, "Subtitle", subtitle, subtitleFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f),
                new Vector2(m.Padding, titleTop + (titleFont - subtitleFont) * 0.5f),
                new Vector2(innerWidth - m.CloseSize.x - 24f, subtitleFont * 1.6f),
                TextAlignmentOptions.MidlineRight, 1.5f, FontStyles.Bold);

            CloseButton = CreateCloseButton(panel, m, new Vector2(panelSize.x - m.Padding - m.CloseSize.x, top));

            // 头部细分隔线
            CreateSolidRect(panel, "HeaderDivider",
                new Vector2(m.Padding, m.HeaderHeight),
                new Vector2(panelSize.x - m.Padding * 2f, 2f),
                ComicStyle.InkSoft, false);
        }

        private void BuildFooter(RectTransform panel, HomeInfoMetrics m, bool mobile, Vector2 panelSize)
        {
            float noteFont = UITypography.FontSnapped(m.NoteFont);
            float noteTop = panelSize.y - m.FooterHeight + (m.FooterHeight - noteFont * 1.5f) * 0.5f;
            string note;
            if (_tab == HomeInfoTab.Record)
            {
                RecordSummary summary = HomeInfoContent.Summarize(TryLoadProfile());
                note = summary.HasMatches
                    ? "每局结算后自动更新 · 数据仅保存在本机"
                    : "完成第一局后开始记录战绩 · 数据仅保存在本机";
            }
            else if (_tab == HomeInfoTab.Achievements)
            {
                note = "完成对局解锁更多成就 · 数据仅保存在本机";
            }
            else
            {
                note = mobile
                    ? HomeInfoContent.MobileLayoutHint
                    : "键位以当前版本为准 · 手柄同样支持";
            }

            CreateText(panel, "FooterNote", note, noteFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(m.Padding, noteTop),
                new Vector2(panelSize.x * 0.58f, noteFont * 1.6f),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            string closeHint = mobile ? "点击空白处关闭" : "ESC / 点击空白处关闭";
            CreateText(panel, "FooterCloseHint", closeHint, noteFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(m.Padding, noteTop),
                new Vector2(panelSize.x - m.Padding * 2f, noteFont * 1.6f),
                TextAlignmentOptions.MidlineRight, 0.5f, FontStyles.Bold);
        }

        // ==================== 战绩 / 成就 页签条 ====================

        /// <summary>
        /// 战绩 / 成就切换胶囊：激活页签墨底纸字，另一个纸白墨框。返回占用高度。
        /// </summary>
        private float BuildTabSwitcher(RectTransform panel, HomeInfoMetrics m, float contentTop)
        {
            bool mobile = UITypography.IsMobileLike;
            float pillHeight = mobile ? 46f : 40f;
            float pillWidth = mobile ? 152f : 128f;
            float gap = 12f;

            BuildTabPill(panel, "TabPill_Record", "战绩", HomeInfoTab.Record,
                new Vector2(m.Padding, contentTop), new Vector2(pillWidth, pillHeight));
            BuildTabPill(panel, "TabPill_Achievements", "成就", HomeInfoTab.Achievements,
                new Vector2(m.Padding + pillWidth + gap, contentTop), new Vector2(pillWidth, pillHeight));

            return pillHeight + 14f;
        }

        private void BuildTabPill(RectTransform panel, string name, string label, HomeInfoTab tab,
            Vector2 topLeft, Vector2 size)
        {
            bool active = _tab == tab;
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button));
            go.transform.SetParent(panel, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.Pill;
            shape.BorderWidth = active ? 0f : ComicStyle.BorderThin;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = active ? ComicStyle.Ink : ComicStyle.Paper;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = true;
            if (active) _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            float font = UITypography.FontSnapped(UITypography.IsMobileLike ? 19f : 17f);
            TextMeshProUGUI labelTmp = CreateText(rect, "Label", label, font, true,
                active ? ComicStyle.Paper : ComicStyle.Ink,
                Vector2.zero, size, TextAlignmentOptions.Center);
            if (labelTmp != null)
            {
                labelTmp.rectTransform.anchorMin = Vector2.zero;
                labelTmp.rectTransform.anchorMax = Vector2.one;
                labelTmp.rectTransform.offsetMin = Vector2.zero;
                labelTmp.rectTransform.offsetMax = Vector2.zero;
            }

            var button = go.GetComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(() =>
            {
                if (_tab == tab) return;
                _tab = tab;
                Rebuild();
            });
            UISoundBinder.EnsureBinder(button);

            var jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(_theme, button, shape);
            if (labelTmp != null) jelly.SetPressContent(labelTmp.rectTransform);
        }

        // ==================== 战绩视图 ====================

        private void BuildRecordView(RectTransform panel, HomeInfoMetrics m, bool mobile, Vector2 panelSize,
            float contentTop, float contentHeight)
        {
            RecordSummary summary = HomeInfoContent.Summarize(TryLoadProfile());
            float innerWidth = panelSize.x - m.Padding * 2f;
            float leftWidth = innerWidth * 0.34f;
            float rightWidth = innerWidth - leftWidth - m.ColumnGap;

            BuildIdentityBlock(panel, m, new Vector2(m.Padding, contentTop),
                new Vector2(leftWidth, contentHeight), summary);

            BuildStatGrid(panel, m, new Vector2(m.Padding + leftWidth + m.ColumnGap, contentTop),
                new Vector2(rightWidth, contentHeight), summary);
        }

        private void BuildIdentityBlock(RectTransform panel, HomeInfoMetrics m, Vector2 topLeft, Vector2 size,
            RecordSummary summary)
        {
            RectTransform block = CreateBlock(panel, "IdentityBlock", topLeft, size,
                ComicStyle.PaperAlt, ComicStyle.Green, 6f, UIHardShadowLevel.None);

            float pad = m.Padding * 0.6f;
            float tagTop = pad + 6f;
            CreateTag(block, "LevelTag", "LV.", m.IdentityTagSize,
                UITypography.FontSnapped(m.IdentityTagFont), ComicStyle.Ink, ComicStyle.Paper,
                new Vector2(pad, tagTop));

            float levelFont = UITypography.FontSnapped(m.IdentityLevelFont);
            float levelTop = tagTop + m.IdentityTagSize.y + 10f;
            CreateText(block, "LevelValue", summary.Level.ToString(), levelFont, true, ComicStyle.Ink,
                new Vector2(pad, levelTop),
                new Vector2(size.x - pad * 2f, levelFont * 1.15f),
                TextAlignmentOptions.MidlineLeft, -2f, FontStyles.Bold);

            float rankFont = UITypography.FontSnapped(m.IdentityRankFont);
            float rankTop = levelTop + levelFont * 1.15f + 4f;
            CreateText(block, "RankName", summary.RankName.ToUpperInvariant(), rankFont, true,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.78f),
                new Vector2(pad, rankTop),
                new Vector2(size.x - pad * 2f, rankFont * 1.35f),
                TextAlignmentOptions.MidlineLeft, 1f, FontStyles.Bold);

            // 底部从 ID 标签往上排，保证 XP 区永远不压到底边
            float labelFont = UITypography.FontSnapped(m.IdentityLabelFont);
            float idTop = size.y - pad - m.IdentityTagSize.y;
            float subTop = idTop - 14f - labelFont * 1.5f;
            float barTop = subTop - 8f - m.XpBarHeight;
            float xpLabelTop = barTop - 8f - labelFont * 1.35f;

            CreateText(block, "XpLabel", $"XP {summary.Xp} / {summary.XpForNextLevel}", labelFont, false,
                ComicStyle.Ink,
                new Vector2(pad, xpLabelTop),
                new Vector2(size.x - pad * 2f, labelFont * 1.35f),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            float barWidth = size.x - pad * 2f;
            CreateSolidRect(block, "XpTrack", new Vector2(pad, barTop), new Vector2(barWidth, m.XpBarHeight),
                ComicStyle.InkSoft, false);
            // 进度条与轨道都取左上 pivot：填充从左侧生长，不靠改 pivot（改 pivot 会带着位置一起位移）。
            CreateSolidRect(block, "XpFill", new Vector2(pad, barTop),
                new Vector2(barWidth * summary.LevelProgress01, m.XpBarHeight), ComicStyle.Green, false);

            CreateText(block, "XpToNext", $"距下一级 {summary.XpToNextLevel} XP", labelFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(pad, subTop),
                new Vector2(size.x - pad * 2f, labelFont * 1.5f),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            CreateTag(block, "PlayerTag", "ID · " + summary.PlayerName,
                new Vector2(size.x - pad * 2f, m.IdentityTagSize.y),
                UITypography.FontSnapped(m.IdentityTagFont), ComicStyle.Ink, ComicStyle.Paper,
                new Vector2(pad, idTop));
        }

        private void BuildStatGrid(RectTransform panel, HomeInfoMetrics m, Vector2 topLeft, Vector2 size,
            RecordSummary summary)
        {
            const int columns = 2;
            const int rows = 4;
            float tileWidth = (size.x - m.StatGap * (columns - 1)) / columns;
            float tileHeight = (size.y - m.StatGap * (rows - 1)) / rows;

            string[] labels =
            {
                "总场次", "胜场",
                "胜率", "总击杀",
                "总涂地", "场均涂地",
                "最高连杀", "成就",
            };

            string[] values =
            {
                summary.Matches.ToString(),
                summary.Wins.ToString(),
                HomeInfoContent.Percent(summary.WinRate01),
                summary.TotalKills.ToString(),
                summary.TotalTurf.ToString("0") + "㎡",
                summary.TurfPerMatch.ToString("0.0") + "㎡",
                summary.BestStreak.ToString(),
                summary.AchievementsUnlocked + "/" + summary.AchievementsTotal,
            };

            for (int i = 0; i < labels.Length; i++)
            {
                int col = i % columns;
                int row = i / columns;
                var position = new Vector2(
                    topLeft.x + col * (tileWidth + m.StatGap),
                    topLeft.y + row * (tileHeight + m.StatGap));
                bool isAchievements = i == labels.Length - 1;
                BuildStatTile(panel, m, "Stat_" + i, labels[i], values[i], position,
                    new Vector2(tileWidth, tileHeight), isAchievements);
            }
        }

        private void BuildStatTile(RectTransform panel, HomeInfoMetrics m, string name, string label, string value,
            Vector2 topLeft, Vector2 size, bool compactValue)
        {
            RectTransform tile = CreateBlock(panel, name, topLeft, size,
                ComicStyle.PaperAlt, ComicStyle.Green, 5f, UIHardShadowLevel.None);

            float pad = 20f;
            float labelFont = UITypography.FontSnapped(m.StatLabelFont);
            CreateText(tile, "Label", label, labelFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                new Vector2(pad, pad),
                new Vector2(size.x - pad * 2f, labelFont * 1.4f),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            float valueFont = UITypography.FontSnapped(compactValue ? m.StatRankFont : m.StatValueFont);
            CreateText(tile, "Value", value, valueFont, true, ComicStyle.Ink,
                new Vector2(pad, size.y - pad - valueFont * 1.15f),
                new Vector2(size.x - pad * 2f, valueFont * 1.15f),
                TextAlignmentOptions.MidlineLeft, compactValue ? 0.5f : -1f, FontStyles.Bold);
        }

        // ==================== 成就视图 ====================

        private static readonly AchievementCategory[] AchievementCategoryOrder =
        {
            AchievementCategory.Splat,
            AchievementCategory.Turf,
            AchievementCategory.Survive,
            AchievementCategory.Special,
            AchievementCategory.Egg,
            AchievementCategory.Career,
        };

        private static Color GetAchievementAccent(AchievementCategory category)
        {
            switch (category)
            {
                case AchievementCategory.Splat: return ComicStyle.Red;
                case AchievementCategory.Turf: return ComicStyle.Green;
                case AchievementCategory.Survive: return ComicStyle.Blue;
                case AchievementCategory.Special: return ComicStyle.Yellow;
                case AchievementCategory.Egg: return ComicStyle.WithAlpha(ComicStyle.Ink, 0.5f);
                default: return ComicStyle.Gold; // 生涯 = 荣誉金
            }
        }

        /// <summary>
        /// 成就视图：顶部汇总条 + 分类分节的方块卡网格（t-1 式：已解锁分类色实底，未解锁纸灰+进度条）。
        /// 37 条成就一屏放不下，走运行时 ScrollRect（RectMask2D 裁剪，无需 sprite mask）。
        /// </summary>
        private void BuildAchievementsView(RectTransform panel, HomeInfoMetrics m, bool mobile, Vector2 panelSize,
            float contentTop, float contentHeight)
        {
            try
            {
                PlayerProfile profile = TryLoadProfile();
                int unlocked = profile != null
                    ? Mathf.Clamp(profile.AchievementUnlockedCount, 0, AchievementCatalog.TotalCount)
                    : 0;
                float innerWidth = panelSize.x - m.Padding * 2f;

                // 1) 汇总条：已解锁 N / 24
                float summaryHeight = mobile ? 78f : 66f;
                RectTransform summary = CreateBlock(panel, "AchievementSummary",
                    new Vector2(m.Padding, contentTop), new Vector2(innerWidth, summaryHeight),
                    ComicStyle.PaperAlt, ComicStyle.Yellow, 6f, UIHardShadowLevel.None);

                float countFont = UITypography.FontSnapped(mobile ? 24f : 21f);
                CreateText(summary, "Count", $"已解锁 {unlocked} / {AchievementCatalog.TotalCount}",
                    countFont, true, ComicStyle.Ink,
                    new Vector2(20f, summaryHeight * 0.5f - countFont * 0.62f),
                    new Vector2(innerWidth * 0.6f, countFont * 1.3f),
                    TextAlignmentOptions.MidlineLeft);

                float hintFont = UITypography.FontSnapped(mobile ? 15f : 13f);
                CreateText(summary, "Hint", "按分类浏览 · 幽默说明仅供一笑", hintFont, false,
                    ComicStyle.WithAlpha(ComicStyle.Ink, 0.6f),
                    new Vector2(20f, summaryHeight * 0.5f - hintFont * 0.5f),
                    new Vector2(innerWidth - 40f, hintFont * 1.4f),
                    TextAlignmentOptions.MidlineRight);

                // 2) 滚动列表
                float listTop = contentTop + summaryHeight + 12f;
                float listHeight = contentHeight - summaryHeight - 12f;
                if (listHeight > 80f)
                {
                    BuildAchievementList(panel, m, mobile, new Vector2(m.Padding, listTop),
                        new Vector2(innerWidth, listHeight), profile);
                }
            }
            catch (Exception ex)
            {
                // 装饰性内容失败不能让整局游戏起不来，也不该触发 PlayMode 的 log-error 门禁。
                Debug.LogWarning("[HomeInfoOverlay] 成就视图构建失败：" + ex.Message);
            }
        }

        private void BuildAchievementList(RectTransform panel, HomeInfoMetrics m, bool mobile,
            Vector2 topLeft, Vector2 size, PlayerProfile profile)
        {
            // t-1 式「分类分节 + 方块卡网格」：桌面 3 列 / 移动 2 列，已解锁 = 分类色实底大色块。
            bool mobileLike = UITypography.IsMobileLike;
            int columns = mobileLike ? 2 : 3;
            float gap = mobileLike ? 12f : 16f;
            float tileHeight = mobileLike ? 196f : 180f;
            float headerHeight = mobileLike ? 40f : 34f;
            float headerGap = mobileLike ? 14f : 12f;
            float tileWidth = (size.x - (columns - 1) * gap) / columns;

            // 先量总高（分节头 + 方块行数），再建滚动结构
            float totalHeight = 0f;
            for (int c = 0; c < AchievementCategoryOrder.Length; c++)
            {
                int countInCategory = CountAchievementsInCategory(AchievementCategoryOrder[c]);
                if (countInCategory <= 0) continue;
                int rows = (countInCategory + columns - 1) / columns;
                totalHeight += headerHeight + headerGap + rows * tileHeight + (rows - 1) * gap;
            }
            totalHeight += 8f;

            var scrollGo = new GameObject("AchievementScroll", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(panel, false);
            var scrollRectT = (RectTransform)scrollGo.transform;
            scrollRectT.anchorMin = new Vector2(0f, 1f);
            scrollRectT.anchorMax = new Vector2(0f, 1f);
            scrollRectT.pivot = new Vector2(0f, 1f);
            scrollRectT.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            scrollRectT.sizeDelta = size;

            var scrollImage = scrollGo.GetComponent<Image>();
            scrollImage.color = Color.clear; // 透明拖拽热区
            scrollImage.raycastTarget = true;

            var scrollRect = scrollGo.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            Stretch(viewportRt);
            scrollRect.viewport = viewportRt;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = (RectTransform)contentGo.transform;
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, totalHeight);
            scrollRect.content = contentRt;

            float cursor = 0f;
            for (int c = 0; c < AchievementCategoryOrder.Length; c++)
            {
                AchievementCategory category = AchievementCategoryOrder[c];
                Color accent = GetAchievementAccent(category);

                int unlockedInCategory = 0;
                int totalInCategory = 0;
                for (int i = 0; i < AchievementCatalog.All.Length; i++)
                {
                    if (AchievementCatalog.All[i].Category != category) continue;
                    totalInCategory++;
                    if (profile == null || profile.IsAchievementUnlocked(AchievementCatalog.All[i].Id))
                    {
                        unlockedInCategory++;
                    }
                }
                if (totalInCategory <= 0) continue;

                BuildAchievementSectionHeader(contentGo.transform, m, mobileLike,
                    new Vector2(0f, cursor), size.x, headerHeight,
                    AchievementCatalog.GetCategoryLabel(category), accent,
                    unlockedInCategory, totalInCategory);
                cursor += headerHeight + headerGap;

                int col = 0;
                for (int i = 0; i < AchievementCatalog.All.Length; i++)
                {
                    AchievementDef def = AchievementCatalog.All[i];
                    if (def.Category != category) continue;

                    bool isUnlocked = profile != null && profile.IsAchievementUnlocked(def.Id);
                    BuildAchievementTile(contentGo.transform, m, mobileLike, tileHeight,
                        new Vector2(col * (tileWidth + gap), cursor), tileWidth,
                        def, isUnlocked, accent, category, profile);
                    col++;
                    if (col >= columns)
                    {
                        col = 0;
                        cursor += tileHeight + gap;
                    }
                }
                if (col > 0) cursor += tileHeight + gap; // 不满一行也要收尾推进
            }
        }

        private static int CountAchievementsInCategory(AchievementCategory category)
        {
            int count = 0;
            for (int i = 0; i < AchievementCatalog.All.Length; i++)
            {
                if (AchievementCatalog.All[i].Category == category) count++;
            }
            return count;
        }

        private void BuildAchievementSectionHeader(Transform parent, HomeInfoMetrics m, bool mobileLike,
            Vector2 topLeft, float width, float height, string title, Color accent, int unlocked, int total)
        {
            float barHeight = UITypography.FontSnapped(mobileLike ? 20f : 17f) * 1.1f;
            CreateSolidRect(parent, "CatBar", new Vector2(topLeft.x, topLeft.y + (height - barHeight) * 0.5f),
                new Vector2(6f, barHeight), accent, false);

            float font = UITypography.FontSnapped(mobileLike ? 20f : 17f);
            CreateText(parent, "CatTitle", "◆ " + title, font, true, ComicStyle.Ink,
                new Vector2(topLeft.x + 16f, topLeft.y),
                new Vector2(width * 0.5f, height),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            float countFont = UITypography.FontSnapped(mobileLike ? 14f : 12f);
            CreateText(parent, "CatCount", $"{unlocked}/{total}", countFont, true,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.55f),
                new Vector2(topLeft.x, topLeft.y),
                new Vector2(width - 16f, height),
                TextAlignmentOptions.MidlineRight, 1f, FontStyles.Bold);
        }

        /// <summary>已解锁方块的实底色（t-1「大色块奖章」语言）：彩蛋用墨底神秘奖章，生涯用荣誉金。</summary>
        private static Color GetAchievementTileFill(AchievementCategory category)
        {
            switch (category)
            {
                case AchievementCategory.Splat: return ComicStyle.Red;
                case AchievementCategory.Turf: return ComicStyle.Green;
                case AchievementCategory.Survive: return ComicStyle.Blue;
                case AchievementCategory.Special: return ComicStyle.Yellow;
                case AchievementCategory.Egg: return ComicStyle.Ink;
                default: return ComicStyle.Gold;
            }
        }

        /// <summary>
        /// 成就方块卡（t-1 式）：已解锁 = 分类色实底 + 硬阴影奖章；未解锁 = 纸灰底 + 描边 + 进度条。
        /// 本浮层 CreateText / CreateBlock 是「顶左锚点」约定：y 从方块顶边往下量。
        /// </summary>
        private void BuildAchievementTile(Transform parent, HomeInfoMetrics m, bool mobileLike, float tileHeight,
            Vector2 topLeft, float width, in AchievementDef def, bool isUnlocked, Color accent,
            AchievementCategory category, PlayerProfile profile)
        {
            Color fill = isUnlocked ? GetAchievementTileFill(category) : ComicStyle.PaperAlt;
            RectTransform tile = CreateBlock(parent, "Tile_" + def.Id, topLeft, new Vector2(width, tileHeight),
                fill, accent, 0f, isUnlocked ? UIHardShadowLevel.Button : UIHardShadowLevel.None);
            if (!isUnlocked)
            {
                // 未解锁：描边淡一点，视觉上「退后一步」
                UIInkShapeGraphic lockedShape = tile.GetComponent<UIInkShapeGraphic>();
                lockedShape.BorderColor = ComicStyle.WithAlpha(ComicStyle.Ink, 0.35f);
            }

            // 反色规则：亮色块（红/黄/绿/金）上用墨字 + 墨底图标盒；墨底块（彩蛋/生涯）上用纸字 + 纸底图标盒
            Color tileText = isUnlocked ? ComicStyle.ReadableTextOn(fill) : ComicStyle.WithAlpha(ComicStyle.Ink, 0.78f);
            Color iconBoxColor = isUnlocked ? tileText : ComicStyle.WithAlpha(ComicStyle.Ink, 0.4f);
            Color iconGlyphColor = isUnlocked ? fill : ComicStyle.WithAlpha(ComicStyle.Paper, 0.7f);

            float pad = mobileLike ? 16f : 14f;
            float iconBox = mobileLike ? 44f : 40f;

            // 左上图标盒（取不到图标时只隐藏 Image，保留色块）
            var iconBgGo = new GameObject("IconBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            iconBgGo.transform.SetParent(tile, false);
            var iconBgRect = (RectTransform)iconBgGo.transform;
            iconBgRect.anchorMin = new Vector2(0f, 1f);
            iconBgRect.anchorMax = new Vector2(0f, 1f);
            iconBgRect.pivot = new Vector2(0f, 1f);
            iconBgRect.anchoredPosition = new Vector2(pad, -pad);
            iconBgRect.sizeDelta = new Vector2(iconBox, iconBox);

            var iconBgShape = iconBgGo.GetComponent<UIInkShapeGraphic>();
            iconBgShape.Shape = UIInkShape.RoundedRect;
            iconBgShape.CornerRadius = ComicStyle.CornerFor(iconBox, iconBox);
            iconBgShape.BorderWidth = 0f;
            iconBgShape.color = iconBoxColor;
            iconBgShape.HardShadow = false;
            iconBgShape.Halftone = false;
            iconBgShape.raycastTarget = false;

            if (UIIconLibrary.TryGet(def.IconName, out Sprite icon))
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                iconGo.transform.SetParent(iconBgGo.transform, false);
                var iconRect = (RectTransform)iconGo.transform;
                iconRect.anchorMin = Vector2.zero;
                iconRect.anchorMax = Vector2.one;
                iconRect.offsetMin = new Vector2(9f, 9f);
                iconRect.offsetMax = new Vector2(-9f, -9f);
                var iconImage = iconGo.GetComponent<Image>();
                iconImage.sprite = icon;
                iconImage.color = iconGlyphColor;
                iconImage.raycastTarget = false;
            }

            // 右上状态签：已达成（反色胶囊）/ 未达成（灰描边胶囊）
            Vector2 pillSize = new Vector2(mobileLike ? 96f : 88f, mobileLike ? 32f : 28f);
            var pillGo = new GameObject("StatusTag", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            pillGo.transform.SetParent(tile, false);
            var pillRect = (RectTransform)pillGo.transform;
            pillRect.anchorMin = new Vector2(1f, 1f);
            pillRect.anchorMax = new Vector2(1f, 1f);
            pillRect.pivot = new Vector2(1f, 1f);
            pillRect.anchoredPosition = new Vector2(-pad, -pad + (mobileLike ? 6f : 4f));
            pillRect.sizeDelta = pillSize;

            var pillShape = pillGo.GetComponent<UIInkShapeGraphic>();
            pillShape.Shape = UIInkShape.Pill;
            pillShape.HardShadow = false;
            pillShape.Halftone = false;
            pillShape.raycastTarget = false;
            if (isUnlocked)
            {
                pillShape.BorderWidth = 0f;
                pillShape.color = iconBoxColor;
            }
            else
            {
                pillShape.BorderWidth = 2f;
                pillShape.BorderColor = ComicStyle.WithAlpha(ComicStyle.Ink, 0.3f);
                pillShape.color = ComicStyle.Paper;
            }

            float pillFont = UITypography.FontSnapped(mobileLike ? 15f : 13f);
            CreateText(pillGo.transform, "Text", isUnlocked ? "已达成" : "未达成", pillFont, true,
                isUnlocked ? iconGlyphColor : ComicStyle.WithAlpha(ComicStyle.Ink, 0.4f),
                Vector2.zero, pillSize, TextAlignmentOptions.Center);

            // 成就名（独占一行）+ 幽默描述（最多两行，Wrap 防截断）
            float textWidth = width - pad * 2f;
            float nameFont = UITypography.FontSnapped(mobileLike ? 21f : 19f);
            float nameTop = pad + iconBox + (mobileLike ? 12f : 10f);
            CreateText(tile, "Name", def.Name, nameFont, true, tileText,
                new Vector2(pad, nameTop),
                new Vector2(textWidth, nameFont * 1.35f),
                TextAlignmentOptions.MidlineLeft);

            float descFont = UITypography.FontSnapped(mobileLike ? 14f : 13f);
            float descTop = nameTop + nameFont * 1.35f + (mobileLike ? 6f : 4f);
            CreateText(tile, "Desc", def.Description, descFont, false,
                isUnlocked ? ComicStyle.WithAlpha(tileText, 0.8f) : ComicStyle.WithAlpha(ComicStyle.Ink, 0.45f),
                new Vector2(pad, descTop),
                new Vector2(textWidth, tileHeight - descTop - (mobileLike ? 34f : 30f)),
                TextAlignmentOptions.TopLeft, 0f, FontStyles.Normal, wrapping: true);

            // 底部：锁定且有量化进度 → 细进度条 + 数值；布尔型成就仅状态签
            if (!isUnlocked)
            {
                string progressText = AchievementEvaluator.GetProgressText(in def, profile);
                if (!string.IsNullOrEmpty(progressText))
                {
                    float barHeight = 8f;
                    float barTop = tileHeight - pad - barHeight;
                    float progressWidth = mobileLike ? 92f : 86f;
                    float barWidth = textWidth - progressWidth - 10f;
                    float progress01 = AchievementEvaluator.GetProgress01(in def, profile);
                    CreateSolidRect(tile, "ProgTrack", new Vector2(pad, barTop), new Vector2(barWidth, barHeight),
                        ComicStyle.WithAlpha(ComicStyle.Ink, 0.16f), false);
                    if (progress01 > 0.005f)
                    {
                        CreateSolidRect(tile, "ProgFill", new Vector2(pad, barTop), new Vector2(barWidth * progress01, barHeight),
                            accent, false);
                    }

                    float progFont = UITypography.FontSnapped(mobileLike ? 13f : 12f);
                    CreateText(tile, "Progress", progressText, progFont, true,
                        ComicStyle.WithAlpha(ComicStyle.Ink, 0.72f),
                        new Vector2(width - pad - progressWidth, barTop - 5f),
                        new Vector2(progressWidth, progFont * 1.5f),
                        TextAlignmentOptions.MidlineRight);
                }
            }
        }

        // ==================== 键位视图 ====================

        private void BuildControlsView(RectTransform panel, HomeInfoMetrics m, bool mobile, Vector2 panelSize,
            float contentTop, float contentHeight)
        {
            float innerWidth = panelSize.x - m.Padding * 2f;
            float columnWidth = (innerWidth - m.ColumnGap) * 0.5f;
            int rowsPerColumn = mobile ? 3 : 4;
            float sectionHeight = SectionHeaderHeight(m);
            float rowsTop = contentTop + sectionHeight + 10f;

            ControlRow[] movement = HomeInfoContent.RowsInGroup(mobile, ControlGroup.Movement);
            ControlRow[] combat = HomeInfoContent.RowsInGroup(mobile, ControlGroup.Combat);

            try
            {
                string moveTitle = mobile ? "触控 · 移动" : "移动与视角";
                string combatTitle = mobile ? "触控 · 战斗" : "战斗与系统";
                BuildSectionTitle(panel, m, new Vector2(m.Padding, contentTop), columnWidth,
                    moveTitle, mobile ? "TOUCH" : "MOVEMENT", ComicStyle.Green);
                BuildSectionTitle(panel, m, new Vector2(m.Padding + columnWidth + m.ColumnGap, contentTop),
                    columnWidth, combatTitle, mobile ? "TOUCH" : "COMBAT", ComicStyle.Red);

                BuildControlRows(panel, m, new Vector2(m.Padding, rowsTop), columnWidth, movement, rowsPerColumn);
                BuildControlRows(panel, m, new Vector2(m.Padding + columnWidth + m.ColumnGap, rowsTop),
                    columnWidth, combat, rowsPerColumn);
            }
            catch (Exception ex)
            {
                // 装饰性内容失败不能让整局游戏起不来，也不该触发 PlayMode 的 log-error 门禁。
                Debug.LogWarning("[HomeInfoOverlay] 键位栏构建失败：" + ex.Message);
            }

            float rowsBottom = rowsTop + rowsPerColumn * m.RowHeight + (rowsPerColumn - 1) * m.RowGap;
            float rulesTop = rowsBottom + 18f;
            float noteFont = UITypography.FontSnapped(m.NoteFont);
            if (!mobile)
            {
                CreateText(panel, "GamepadHint", HomeInfoContent.GamepadHint, noteFont, false,
                    ComicStyle.WithAlpha(ComicStyle.Ink, 0.62f),
                    new Vector2(m.Padding, rowsBottom + 12f),
                    new Vector2(innerWidth, noteFont * 1.7f),
                    TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);
                rulesTop = rowsBottom + 12f + noteFont * 1.7f + 16f;
            }

            float available = contentTop + contentHeight - rulesTop;
            float ruleRowHeight = UITypography.FontSnapped(m.RuleFont) * 1.9f;
            float needed = SectionHeaderHeight(m) + 10f + ruleRowHeight * 2f + 12f;
            if (available >= needed)
            {
                BuildRules(panel, m, new Vector2(m.Padding, rulesTop), innerWidth, ruleRowHeight,
                    HomeInfoContent.Rules(mobile));
            }
        }

        private static float SectionHeaderHeight(HomeInfoMetrics m)
        {
            return UITypography.FontSnapped(m.SectionFont) * 1.35f;
        }

        private void BuildSectionTitle(RectTransform panel, HomeInfoMetrics m, Vector2 topLeft, float width,
            string title, string tag, Color accent)
        {
            float font = UITypography.FontSnapped(m.SectionFont);
            float barHeight = font * 1.05f;
            CreateSolidRect(panel, "SectionBar", new Vector2(topLeft.x, topLeft.y),
                new Vector2(6f, barHeight), accent, false);

            CreateText(panel, "SectionTitle", title, font, true, ComicStyle.Ink,
                new Vector2(topLeft.x + 16f, topLeft.y),
                new Vector2(width - 16f, font * 1.35f),
                TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);

            float tagFont = UITypography.FontSnapped(m.NoteFont);
            CreateText(panel, "SectionTag", tag, tagFont, false,
                ComicStyle.WithAlpha(ComicStyle.Ink, 0.45f),
                new Vector2(topLeft.x, topLeft.y + (font * 1.35f - tagFont * 1.5f) * 0.5f),
                new Vector2(width, tagFont * 1.5f),
                TextAlignmentOptions.MidlineRight, 1.5f, FontStyles.Bold);
        }

        private void BuildControlRows(RectTransform panel, HomeInfoMetrics m, Vector2 topLeft, float width,
            ControlRow[] rows, int maxRows)
        {
            if (rows == null) return;
            int count = Mathf.Min(rows.Length, maxRows);
            float keyFont = UITypography.FontSnapped(m.KeyFont);
            float actionFont = UITypography.FontSnapped(m.ActionFont);

            for (int i = 0; i < count; i++)
            {
                float y = topLeft.y + i * (m.RowHeight + m.RowGap);

                RectTransform cap = CreateBlock(panel, "KeyCap_" + i, new Vector2(topLeft.x, y),
                    new Vector2(m.KeyCapWidth, m.RowHeight), ComicStyle.Ink, ComicStyle.Ink, 0f,
                    UIHardShadowLevel.Tag);
                CreateText(cap, "KeyText", rows[i].Key, keyFont, true, ComicStyle.Paper,
                    new Vector2(8f, 0f),
                    new Vector2(m.KeyCapWidth - 16f, m.RowHeight),
                    TextAlignmentOptions.Center, 0.5f, FontStyles.Bold);

                CreateText(panel, "Action_" + i, rows[i].Action, actionFont, false, ComicStyle.Ink,
                    new Vector2(topLeft.x + m.KeyCapWidth + 14f, y),
                    new Vector2(width - m.KeyCapWidth - 14f, m.RowHeight),
                    TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);
            }
        }

        private void BuildRules(RectTransform panel, HomeInfoMetrics m, Vector2 topLeft, float width,
            float rowHeight, string[] rules)
        {
            if (rules == null || rules.Length == 0) return;

            BuildSectionTitle(panel, m, topLeft, width, "规则速记", "FIELD NOTES", ComicStyle.Yellow);

            float rowsTop = topLeft.y + SectionHeaderHeight(m) + 10f;
            float columnWidth = (width - m.ColumnGap) * 0.5f;
            float font = UITypography.FontSnapped(m.RuleFont);
            Color[] accents = { ComicStyle.Yellow, ComicStyle.Red, ComicStyle.Blue, ComicStyle.Green };

            for (int i = 0; i < rules.Length && i < 4; i++)
            {
                int col = i % 2;
                int row = i / 2;
                float x = topLeft.x + col * (columnWidth + m.ColumnGap);
                float y = rowsTop + row * (rowHeight + 12f);

                CreateSolidRect(panel, "RuleBar_" + i, new Vector2(x, y),
                    new Vector2(6f, rowHeight), accents[i % accents.Length], false);

                CreateText(panel, "Rule_" + i, rules[i], font, false, ComicStyle.Ink,
                    new Vector2(x + 18f, y),
                    new Vector2(columnWidth - 18f, rowHeight),
                    TextAlignmentOptions.MidlineLeft, 0.5f, FontStyles.Bold);
            }
        }

        // ==================== 基础图元 ====================

        private Button CreateCloseButton(RectTransform panel, HomeInfoMetrics m, Vector2 topLeft)
        {
            var go = new GameObject("CloseButton", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button));
            go.transform.SetParent(panel, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = m.CloseSize;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.Pill; // 圆角化：关闭键改正圆
            shape.BorderWidth = 0f;
            shape.color = ComicStyle.Ink;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = true;
            _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            float font = UITypography.FontSnapped(m.CloseFont);
            TextMeshProUGUI label = CreateText(rect, "Label", "×", font, true, ComicStyle.Paper,
                new Vector2(0f, 0f), m.CloseSize, TextAlignmentOptions.Center, 0f, FontStyles.Bold);
            if (label != null)
            {
                label.rectTransform.anchorMin = Vector2.zero;
                label.rectTransform.anchorMax = Vector2.one;
                label.rectTransform.offsetMin = Vector2.zero;
                label.rectTransform.offsetMax = Vector2.zero;
            }

            var button = go.GetComponent<Button>();
            button.targetGraphic = shape;
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(Close);

            var jelly = go.AddComponent<UIJellyButton>();
            jelly.Configure(_theme, button, shape);
            if (label != null) jelly.SetPressContent(label.rectTransform);
            jelly.EnableThemeHardShadow(UIHardShadowLevel.Tag);

            UISoundBinder.EnsureBinder(button);
            return button;
        }

        private RectTransform CreateTag(Transform parent, string name, string label, Vector2 size, float font,
            Color fill, Color textColor, Vector2 topLeft)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.Pill; // 圆角化：角签改胶囊
            shape.BorderWidth = 0f;
            shape.color = fill;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = false;
            _theme.ApplyHardShadow(shape, UIHardShadowLevel.Tag);

            TextMeshProUGUI tmp = CreateText(rect, "Text", label, font, true, textColor,
                new Vector2(8f, 0f), new Vector2(Mathf.Max(20f, size.x - 16f), size.y),
                TextAlignmentOptions.Center, 1.5f, FontStyles.Bold);
            if (tmp != null) tmp.fontStyle |= FontStyles.UpperCase;
            return rect;
        }

        private RectTransform CreateBlock(Transform parent, string name, Vector2 topLeft, Vector2 size,
            Color fill, Color accent, float accentHeight, UIHardShadowLevel shadowLevel)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：信息块自适应圆角
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = ComicStyle.BorderThin;
            shape.BorderColor = ComicStyle.Ink;
            shape.color = fill;
            shape.GlossAlpha = 0f;
            shape.Halftone = false;
            shape.raycastTarget = false;
            if (shadowLevel != UIHardShadowLevel.None) _theme.ApplyHardShadow(shape, shadowLevel);

            if (accentHeight > 0f)
            {
                // 圆角化：色带内缩到圆角直线段内并改胶囊短划
                float blockRadius = ComicStyle.CornerFor(size.x, size.y);
                var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                bandGo.transform.SetParent(rect, false);
                var band = (RectTransform)bandGo.transform;
                band.anchorMin = new Vector2(0f, 1f);
                band.anchorMax = new Vector2(1f, 1f);
                band.pivot = new Vector2(0.5f, 1f);
                band.sizeDelta = new Vector2(-(blockRadius + ComicStyle.BorderThin) * 2f, accentHeight);
                band.anchoredPosition = new Vector2(0f, -ComicStyle.BorderThin);

                var bandShape = bandGo.GetComponent<UIInkShapeGraphic>();
                bandShape.Shape = UIInkShape.Pill;
                bandShape.BorderWidth = 0f;
                bandShape.color = accent;
                bandShape.HardShadow = false;
                bandShape.Halftone = false;
                bandShape.raycastTarget = false;
            }

            return rect;
        }

        private static RectTransform CreateSolidRect(Transform parent, string name, Vector2 topLeft, Vector2 size,
            Color color, bool raycast)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var shape = go.GetComponent<UIInkShapeGraphic>();
            shape.Shape = UIInkShape.RoundedRect; // 圆角化：细线/色块自适应（1~2px 线会被钳成直角）
            shape.CornerRadius = ComicStyle.CornerSharp;
            shape.BorderWidth = 0f;
            shape.color = color;
            shape.HardShadow = false;
            shape.Halftone = false;
            shape.raycastTarget = raycast;
            return rect;
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, string text, float fontSize,
            bool display, Color color, Vector2 topLeft, Vector2 size, TextAlignmentOptions align,
            float spacing = 0f, FontStyles style = FontStyles.Bold, bool wrapping = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(topLeft.x, -topLeft.y);
            rect.sizeDelta = size;

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.characterSpacing = spacing;
            tmp.alignment = align;
            tmp.color = color;
            tmp.fontStyle = style;
            tmp.raycastTarget = false;
            tmp.textWrappingMode = wrapping ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = false;

            TMP_FontAsset font = _theme.ResolveFont(display, text);
            if (font != null) tmp.font = font;
            return tmp;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static PlayerProfile TryLoadProfile()
        {
            try
            {
                return PlayerProfile.Current;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[HomeInfoOverlay] 读取玩家档案失败：" + ex.Message);
                return null;
            }
        }

        private void ClearSelection()
        {
            if (EventSystem.current == null) return;
            GameObject selected = EventSystem.current.currentSelectedGameObject;
            if (selected != null && selected.transform.IsChildOf(transform))
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }
}
