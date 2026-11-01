using System;
using System.Collections;
using P1.Core;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI.WeaponSelect
{
    /// <summary>
    /// 单张武器卡片视图（P14 几何大胆风 Geometric Bold · P19 波次3b t-1 新野兽派）：
    /// - 卡片尺寸/字号全部来自 <see cref="MenuLayoutMath.Drawer"/>，移动端是一套显式更大的数值
    ///   （桌面 592×164，移动端 759×236），不再只把字号 ×1.28 而盒子不动；
    /// - 卡片身份层级 = **纸白底 + 3px 墨框 + 面板硬影 (4,-5) + 顶部 5px 武器功能色带**；
    ///   选中态色带加高到 8px、墨框加到 4px，并播一记 select snap（0ms 压印 + 0.1s Cubic-Out 回弹）；
    /// - 图标从「直愣愣的黑方块」改成**正圆色盘**：队伍色填充 + 墨边 + 白色武器剪影，
    ///   选中时圆盘描边换成该武器的身份色，和纸白卡面形成硬切对比；
    /// - 右侧 7 段直角属性条：移动端 28px 方块（桌面 18px），段内填充队伍色；
    /// - 快捷键方块 [1]~[7] 只在桌面显示（手机没有数字键，占地方且误导）；
    /// - 图标为空时隐藏 Image，绝不出现白块。
    ///
    /// 注意：卡片挂在 ScrollRect 里，硬影会被视口 RectMask2D 裁掉超出部分，
    /// 因此卡片间距必须 ≥10px（桌面 10 / 移动端 16），底部内边距 ≥5px。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class WeaponCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        /// <summary>卡片顶部身份色带高度：常态 5px / 选中 8px（Docs/UI-GeometricBold.md「卡片身份色带」）。</summary>
        public const float AccentBandHeight = 5f;
        public const float AccentBandHeightSelected = 8f;

        /// <summary>卡片墨框宽度：常态 3px / 选中 4px。</summary>
        public const float CardBorderWidth = 3f;
        public const float CardBorderWidthSelected = 4f;

        private int _index;
        private WeaponKind _kind;
        private WeaponConfigSO _config;
        private UIThemeSO _theme;
        private Team _team = Team.TeamA;
        private Action<int> _onSelect;
        private bool _isSelected;
        private bool _mobile;

        private RectTransform _rectTransform;
        private UIInkShapeGraphic _bgGraphic;
        private UIInkShapeGraphic _accentBand;
        private RectTransform _accentBandRect;
        private UISpringMotion _motion;
        private Button _button;
        private CanvasGroup _canvasGroup;
        private Coroutine _snapRoutine;
        private bool _pointerInside;

        private Image _iconImage;
        private UIInkShapeGraphic _iconPlate;
        private GameObject _indexBadge;
        private UIInkShapeGraphic _hotkeyBadgeBg;
        private TextMeshProUGUI _indexBadgeText;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _enTitleText;
        private TextMeshProUGUI _roleTagText;
        private TextMeshProUGUI _statsText;
        private TextMeshProUGUI _specialText;

        private readonly UIInkShapeGraphic[] _statSegments = new UIInkShapeGraphic[7];
        private int _weaponRating = 4;

        private GameObject _equippedBadge;
        private ComicBadge _comicBadge;
        private TextMeshProUGUI _equippedBadgeText;
        private UIInkShapeGraphic _equippedBadgeBg;

        public int Index => _index;
        public WeaponKind Kind => _kind;
        public WeaponConfigSO Config => _config;
        public bool IsSelected => _isSelected;

        public void Initialize(
            int index,
            WeaponKind kind,
            WeaponConfigSO config,
            Sprite icon,
            UIThemeSO themeAsset,
            Team team,
            Action<int> onSelect)
        {
            _index = index;
            _kind = kind;
            _config = config;
            _theme = UIThemeSO.Resolve(themeAsset);
            _team = team;
            _onSelect = onSelect;
            _weaponRating = GetWeaponRating7(kind);
            _mobile = UITypography.IsMobileLike;

            BuildUIHierarchy();
            ApplyData(icon);
            SetSelected(false, animate: false);
        }

        private static int GetWeaponRating7(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Shooter: return 4;
                case WeaponKind.Roller: return 5;
                case WeaponKind.Charger: return 7;
                case WeaponKind.Dualies: return 4;
                case WeaponKind.Slosher: return 5;
                case WeaponKind.Blaster: return 6;
                case WeaponKind.Splatling: return 7;
                default: return 4;
            }
        }

        // ==================== 武器身份色（P19 波次3b） ====================
        // 7 把武器各有一条稳定的功能色带，是「卡片身份色」而不是同屏功能强调色：
        // 颜色全部取自 Docs/UI-GeometricBold.md 的色板（主行动黄 / 队伍橙蓝 / 语义绿红 / 墨黑），
        // 另外给泼桶一个队伍蓝的深调（同一色族的明度变化），避免新增第二种黄。
        // 这张表是 WeaponCardView 的稳定映射常量表：WeaponConfigSO 目前没有颜色字段，
        // 以后若 config 加了颜色字段（例如 weaponAccentColor），在这里优先读 config 再回退本表即可。

        /// <summary>该武器的卡片身份色（顶部色带 / 选中态圆盘描边）。</summary>
        public static Color GetWeaponAccentColor(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Shooter: return ComicStyle.Yellow;                 // #FFD400 主行动黄
                case WeaponKind.Roller: return ComicStyle.FromHex(0xFF7314);       // 队伍橙
                case WeaponKind.Charger: return ComicStyle.FromHex(0x296BFF);      // 队伍蓝
                case WeaponKind.Dualies: return ComicStyle.Green;                  // #1FC94F 语义绿
                case WeaponKind.Slosher: return ComicStyle.FromHex(0x1B3FA8);      // 队伍蓝深调（液体）
                case WeaponKind.Blaster: return ComicStyle.Red;                    // #FF2E2E 危险红
                case WeaponKind.Splatling: return ComicStyle.Ink;                  // #141414 墨黑
                default: return ComicStyle.Ink;
            }
        }

        public void SetTeam(Team team)
        {
            _team = team;
            RefreshIconPlate();
            RefreshStatSegments();
            if (_isSelected)
            {
                RefreshSelectedVisuals();
            }
        }

        public void SetSelected(bool selected, bool animate = true)
        {
            _isSelected = selected;

            if (selected)
            {
                RefreshSelectedVisuals();
                RefreshAccentBand();
                if (_comicBadge != null)
                {
                    if (animate) _comicBadge.Play();
                    else _comicBadge.ShowImmediate();
                }
                if (animate)
                {
                    PlaySelectSnap();
                }
            }
            else
            {
                RefreshUnselectedVisuals();
                RefreshAccentBand();
                if (_comicBadge != null)
                {
                    _comicBadge.HideImmediate();
                }
            }

            RefreshStatSegments();
        }

        /// <summary>
        /// 抽屉打开时逐张 pop-in：0.22s Back-Out + alpha，错峰由调用方按 0.05s/张 传入。
        /// 带延迟的卡片先把 CanvasGroup 压到 0，避免「先整张显示、轮到自己再消失」。
        /// </summary>
        public void PlayEntrance(float delay = 0f)
        {
            if (_canvasGroup != null && delay > 0.001f)
            {
                _canvasGroup.alpha = 0f;
            }
            if (_motion != null)
            {
                _motion.PlayIn(delay);
            }
        }

        /// <summary>
        /// 选中一记 select snap：0ms 硬切压印到 0.97，再用主题的回弹曲线（0.1s Cubic-Out）
        /// 收回 1.0。这里复用 UISpringMotion 的按压状态机，不再另起一套缩放驱动（会互相打架）。
        /// </summary>
        public void PlaySelectSnap()
        {
            if (_motion == null) return;
            if (_snapRoutine != null)
            {
                StopCoroutine(_snapRoutine);
            }
            _snapRoutine = StartCoroutine(SelectSnapRoutine());
        }

        private IEnumerator SelectSnapRoutine()
        {
            _motion.SetPressed(true);
            yield return null; // 压印至少保留一帧，0ms 硬切才看得见
            _motion.SetPressed(false);

            float wait = UIThemeSO.Resolve(_theme).pressReboundDuration + 0.02f;
            yield return new WaitForSecondsRealtime(wait);

            // 指针还停在卡上时回到 hover 态（1.05），别让选中把悬停反馈吃掉。
            if (_pointerInside && _motion != null)
            {
                _motion.SetHovered(true);
            }
            _snapRoutine = null;
        }

        private void BuildUIHierarchy()
        {
            WeaponDrawerMetrics m = MenuLayoutMath.Drawer(_mobile);

            float leftPad = _mobile ? 16f : 14f;
            float iconDiameter = m.CardIconDiameter;
            float infoLeft = leftPad + iconDiameter + (_mobile ? 20f : 18f);
            float equippedW = m.EquippedSize.x;
            float equippedReserve = equippedW + 14f + (_mobile ? 12f : 8f);

            float titleH = _mobile ? 50f : 34f;
            float enH = _mobile ? 30f : 22f;
            float statsH = _mobile ? 34f : 26f;
            float bottomRowH = _mobile ? 36f : 30f;
            float bottomPad = _mobile ? 18f : 10f;
            float statsBottom = bottomPad + bottomRowH + (_mobile ? 16f : 8f);

            _rectTransform = GetComponent<RectTransform>();
            _rectTransform.sizeDelta = new Vector2(_mobile ? 759f : 580f, m.CardHeight);

            // 1. 直角卡片背景：纸白底 + 3px 墨框 + 面板硬影 (4,-5)（t-1 卡片身份层级）
            _bgGraphic = gameObject.GetComponent<UIInkShapeGraphic>();
            if (_bgGraphic == null)
            {
                _bgGraphic = gameObject.AddComponent<UIInkShapeGraphic>();
            }
            _bgGraphic.Shape = UIInkShape.SharpRect;
            _bgGraphic.CornerRadius = ComicStyle.CornerSharp;
            _bgGraphic.BorderWidth = CardBorderWidth;
            _bgGraphic.BorderColor = ComicStyle.Ink;
            _bgGraphic.color = ComicStyle.Paper;
            _bgGraphic.Halftone = false;
            _bgGraphic.GlossAlpha = 0f;
            _bgGraphic.DrawGloss = false;
            _bgGraphic.raycastTarget = true;
            _theme.ApplyHardShadow(_bgGraphic, UIHardShadowLevel.Panel);

            // 入场 alpha 由 UISpringMotion 驱动（缺 CanvasGroup 它会自己补），这里提前拿引用，
            // 好让带错峰延迟的卡片先透明，再按 0.05s/张 依次进场。
            _canvasGroup = gameObject.GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            // 2. 动效控制器
            _motion = gameObject.GetComponent<UISpringMotion>();
            if (_motion == null)
            {
                _motion = gameObject.AddComponent<UISpringMotion>();
            }
            _motion.SetTheme(_theme);

            // 3. 按钮
            _button = gameObject.GetComponent<Button>();
            if (_button == null)
            {
                _button = gameObject.AddComponent<Button>();
            }
            _button.targetGraphic = _bgGraphic;
            _button.transition = Selectable.Transition.None;
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => _onSelect?.Invoke(_index));

            // 3.5 顶部武器身份色带（5px 常态 / 8px 选中，画在 3px 墨框内侧，绝不盖住边框）
            var bandGo = new GameObject("AccentBand", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            bandGo.transform.SetParent(transform, false);
            bandGo.transform.SetAsFirstSibling();
            _accentBandRect = bandGo.GetComponent<RectTransform>();
            _accentBandRect.anchorMin = new Vector2(0f, 1f);
            _accentBandRect.anchorMax = new Vector2(1f, 1f);
            _accentBandRect.pivot = new Vector2(0.5f, 1f);
            _accentBandRect.sizeDelta = new Vector2(-CardBorderWidth * 2f, AccentBandHeight);
            _accentBandRect.anchoredPosition = new Vector2(0f, -CardBorderWidth);

            _accentBand = bandGo.GetComponent<UIInkShapeGraphic>();
            _accentBand.Shape = UIInkShape.SharpRect;
            _accentBand.CornerRadius = ComicStyle.CornerSharp;
            _accentBand.BorderWidth = 0f;
            _accentBand.color = GetWeaponAccentColor(_kind);
            _accentBand.HardShadow = false;
            _accentBand.Halftone = false;
            _accentBand.GlossAlpha = 0f;
            _accentBand.DrawGloss = false;
            _accentBand.raycastTarget = false;

            // 4. 圆形武器色盘（队伍色 + 4px 墨边）。
            float iconCenterX = leftPad + iconDiameter * 0.5f;

            var iconPlateGo = new GameObject("IconPlate", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            iconPlateGo.transform.SetParent(transform, false);
            var plateRect = iconPlateGo.GetComponent<RectTransform>();
            if (_mobile)
            {
                plateRect.anchorMin = new Vector2(0f, 0.5f);
                plateRect.anchorMax = new Vector2(0f, 0.5f);
                plateRect.pivot = new Vector2(0.5f, 0.5f);
                plateRect.anchoredPosition = new Vector2(iconCenterX, 0f);
            }
            else
            {
                // 桌面：快捷键方块在上、色盘在下，整列竖直居中
                plateRect.anchorMin = new Vector2(0f, 1f);
                plateRect.anchorMax = new Vector2(0f, 1f);
                plateRect.pivot = new Vector2(0.5f, 0.5f);
                plateRect.anchoredPosition = new Vector2(
                    iconCenterX,
                    -(14f + m.IndexSize.y + 8f + iconDiameter * 0.5f));
            }
            plateRect.sizeDelta = new Vector2(iconDiameter, iconDiameter);

            _iconPlate = iconPlateGo.GetComponent<UIInkShapeGraphic>();
            _iconPlate.Shape = UIInkShape.Blob; // 正圆
            _iconPlate.WobbleAmplitude = 0f;
            _iconPlate.BorderWidth = ComicStyle.Border;
            _iconPlate.BorderColor = ComicStyle.Ink;
            _iconPlate.HardShadow = false;
            _iconPlate.Halftone = false;
            _iconPlate.GlossAlpha = 0f;
            _iconPlate.DrawGloss = false;
            _iconPlate.raycastTarget = false;

            var iconImgGo = new GameObject("IconImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconImgGo.transform.SetParent(iconPlateGo.transform, false);
            var iconImgRect = iconImgGo.GetComponent<RectTransform>();
            iconImgRect.anchorMin = new Vector2(0.16f, 0.16f);
            iconImgRect.anchorMax = new Vector2(0.84f, 0.84f);
            iconImgRect.sizeDelta = Vector2.zero;
            _iconImage = iconImgGo.GetComponent<Image>();
            _iconImage.preserveAspect = true;
            _iconImage.raycastTarget = false;

            // 5. 快捷键角标（仅桌面：手机没有 1~7 键）
            if (!_mobile)
            {
                var badgeGo = new GameObject("HotkeyBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                badgeGo.transform.SetParent(transform, false);
                var badgeRect = badgeGo.GetComponent<RectTransform>();
                badgeRect.anchorMin = new Vector2(0f, 1f);
                badgeRect.anchorMax = new Vector2(0f, 1f);
                badgeRect.pivot = new Vector2(0.5f, 0.5f);
                badgeRect.anchoredPosition = new Vector2(iconCenterX, -(14f + m.IndexSize.y * 0.5f));
                badgeRect.sizeDelta = m.IndexSize;

                _hotkeyBadgeBg = badgeGo.GetComponent<UIInkShapeGraphic>();
                _hotkeyBadgeBg.Shape = UIInkShape.SharpRect;
                _hotkeyBadgeBg.CornerRadius = ComicStyle.CornerSharp;
                _hotkeyBadgeBg.BorderWidth = 0f;
                _hotkeyBadgeBg.color = ComicStyle.Ink;
                _hotkeyBadgeBg.HardShadow = false;
                _hotkeyBadgeBg.Halftone = false;
                _hotkeyBadgeBg.raycastTarget = false;

                var badgeTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                badgeTextGo.transform.SetParent(badgeGo.transform, false);
                var btr = badgeTextGo.GetComponent<RectTransform>();
                btr.anchorMin = Vector2.zero;
                btr.anchorMax = Vector2.one;
                btr.sizeDelta = Vector2.zero;
                _indexBadgeText = badgeTextGo.GetComponent<TextMeshProUGUI>();
                _indexBadgeText.alignment = TextAlignmentOptions.Center;
                _indexBadgeText.fontSize = UITypography.FontSnapped(m.IndexFont);
                _indexBadgeText.fontStyle = FontStyles.Bold;
                _indexBadgeText.color = Color.white;
                _indexBadgeText.raycastTarget = false;
                _indexBadgeText.font = _theme.ResolveFont(true, (_index + 1).ToString());

                _indexBadge = badgeGo;
            }

            // 6. 中部文字与属性容器
            var infoGo = new GameObject("InfoContainer", typeof(RectTransform));
            infoGo.transform.SetParent(transform, false);
            var infoRect = infoGo.GetComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0f, 0f);
            infoRect.anchorMax = new Vector2(1f, 1f);
            infoRect.offsetMin = new Vector2(infoLeft, bottomPad);
            // 顶部让出「墨框 3 + 身份色带 5」再留 5~10px 呼吸，标题不会被色带压住。
            infoRect.offsetMax = new Vector2(-equippedReserve, -(CardBorderWidth + AccentBandHeight + (_mobile ? 10f : 5f)));

            // 6.1 标题行（中文名 + 右侧定位标签）
            var titleRow = new GameObject("TitleRow", typeof(RectTransform));
            titleRow.transform.SetParent(infoGo.transform, false);
            var titleRowRt = titleRow.GetComponent<RectTransform>();
            titleRowRt.anchorMin = new Vector2(0f, 1f);
            titleRowRt.anchorMax = new Vector2(1f, 1f);
            titleRowRt.pivot = new Vector2(0f, 1f);
            titleRowRt.anchoredPosition = Vector2.zero;
            titleRowRt.sizeDelta = new Vector2(0f, titleH);

            var cnNameGo = new GameObject("CnName", typeof(RectTransform), typeof(TextMeshProUGUI));
            cnNameGo.transform.SetParent(titleRow.transform, false);
            var cnNameRt = cnNameGo.GetComponent<RectTransform>();
            cnNameRt.anchorMin = new Vector2(0f, 0f);
            cnNameRt.anchorMax = new Vector2(1f, 1f);
            cnNameRt.offsetMin = Vector2.zero;
            cnNameRt.offsetMax = new Vector2(_mobile ? -220f : -170f, 0f);
            _titleText = cnNameGo.GetComponent<TextMeshProUGUI>();
            _titleText.fontSize = UITypography.FontSnapped(m.NameFont);
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.alignment = TextAlignmentOptions.MidlineLeft;
            _titleText.color = ComicStyle.Ink;
            _titleText.textWrappingMode = TextWrappingModes.NoWrap;
            _titleText.overflowMode = TextOverflowModes.Overflow;
            _titleText.raycastTarget = false;

            var roleGo = new GameObject("RoleTag", typeof(RectTransform), typeof(TextMeshProUGUI));
            roleGo.transform.SetParent(titleRow.transform, false);
            var roleRt = roleGo.GetComponent<RectTransform>();
            roleRt.anchorMin = new Vector2(1f, 0f);
            roleRt.anchorMax = new Vector2(1f, 1f);
            roleRt.pivot = new Vector2(1f, 0.5f);
            roleRt.anchoredPosition = Vector2.zero;
            roleRt.sizeDelta = new Vector2(_mobile ? 220f : 170f, 0f);
            _roleTagText = roleGo.GetComponent<TextMeshProUGUI>();
            _roleTagText.fontSize = UITypography.FontSnapped(m.RoleFont);
            _roleTagText.fontStyle = FontStyles.Bold;
            _roleTagText.alignment = TextAlignmentOptions.MidlineRight;
            _roleTagText.color = ComicStyle.Ink;
            _roleTagText.textWrappingMode = TextWrappingModes.NoWrap;
            _roleTagText.raycastTarget = false;

            // 6.2 英文名
            var enNameGo = new GameObject("EnName", typeof(RectTransform), typeof(TextMeshProUGUI));
            enNameGo.transform.SetParent(infoGo.transform, false);
            var enNameRt = enNameGo.GetComponent<RectTransform>();
            enNameRt.anchorMin = new Vector2(0f, 1f);
            enNameRt.anchorMax = new Vector2(1f, 1f);
            enNameRt.pivot = new Vector2(0f, 1f);
            enNameRt.anchoredPosition = new Vector2(0f, -(titleH + 2f));
            enNameRt.sizeDelta = new Vector2(0f, enH);
            _enTitleText = enNameGo.GetComponent<TextMeshProUGUI>();
            _enTitleText.fontSize = UITypography.FontSnapped(m.EnglishFont);
            _enTitleText.fontStyle = FontStyles.Bold;
            _enTitleText.alignment = TextAlignmentOptions.MidlineLeft;
            _enTitleText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.65f);
            _enTitleText.textWrappingMode = TextWrappingModes.NoWrap;
            _enTitleText.overflowMode = TextOverflowModes.Overflow;
            _enTitleText.raycastTarget = false;

            // 6.3 属性文字（伤/距/频/耗）
            var statsGo = new GameObject("StatsText", typeof(RectTransform), typeof(TextMeshProUGUI));
            statsGo.transform.SetParent(infoGo.transform, false);
            var sr = statsGo.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 0f);
            sr.anchorMax = new Vector2(1f, 0f);
            sr.pivot = new Vector2(0f, 0f);
            sr.anchoredPosition = new Vector2(0f, statsBottom);
            sr.sizeDelta = new Vector2(0f, statsH);
            _statsText = statsGo.GetComponent<TextMeshProUGUI>();
            _statsText.fontSize = UITypography.FontSnapped(m.StatsFont);
            _statsText.fontStyle = FontStyles.Normal;
            _statsText.alignment = TextAlignmentOptions.MidlineLeft;
            _statsText.color = ComicStyle.Ink;
            _statsText.textWrappingMode = TextWrappingModes.NoWrap;
            _statsText.overflowMode = TextOverflowModes.Overflow;
            _statsText.raycastTarget = false;

            // 6.4 底部行：7 段直角方块 + 大招信息
            var bottomRow = new GameObject("StatBarRow", typeof(RectTransform));
            bottomRow.transform.SetParent(infoGo.transform, false);
            var bottomRowRt = bottomRow.GetComponent<RectTransform>();
            bottomRowRt.anchorMin = new Vector2(0f, 0f);
            bottomRowRt.anchorMax = new Vector2(1f, 0f);
            bottomRowRt.pivot = new Vector2(0f, 0f);
            bottomRowRt.anchoredPosition = new Vector2(0f, bottomPad);
            bottomRowRt.sizeDelta = new Vector2(0f, bottomRowH);

            float block = m.StatBlockSize;
            float gap = m.StatBlockGap;
            float blocksWidth = block * 7f + gap * 6f;

            var blocksGo = new GameObject("ChunkyBlocks7", typeof(RectTransform));
            blocksGo.transform.SetParent(bottomRow.transform, false);
            var blocksRt = blocksGo.GetComponent<RectTransform>();
            blocksRt.anchorMin = new Vector2(0f, 0.5f);
            blocksRt.anchorMax = new Vector2(0f, 0.5f);
            blocksRt.pivot = new Vector2(0f, 0.5f);
            blocksRt.anchoredPosition = Vector2.zero;
            blocksRt.sizeDelta = new Vector2(blocksWidth, block);

            for (int b = 0; b < 7; b++)
            {
                var blockGo = new GameObject($"Block_{b}", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                blockGo.transform.SetParent(blocksGo.transform, false);
                var brt = blockGo.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0f, 0.5f);
                brt.anchorMax = new Vector2(0f, 0.5f);
                brt.pivot = new Vector2(0f, 0.5f);
                brt.anchoredPosition = new Vector2(b * (block + gap), 0f);
                brt.sizeDelta = new Vector2(block, block);

                var bshape = blockGo.GetComponent<UIInkShapeGraphic>();
                bshape.Shape = UIInkShape.SharpRect;
                bshape.CornerRadius = ComicStyle.CornerSharp;
                bshape.BorderWidth = 2f;
                bshape.BorderColor = ComicStyle.Ink;
                bshape.color = ComicStyle.PaperAlt;
                bshape.HardShadow = false;
                bshape.Halftone = false;
                bshape.raycastTarget = false;

                _statSegments[b] = bshape;
            }

            var specialGo = new GameObject("SpecialText", typeof(RectTransform), typeof(TextMeshProUGUI));
            specialGo.transform.SetParent(bottomRow.transform, false);
            var spr = specialGo.GetComponent<RectTransform>();
            spr.anchorMin = new Vector2(0f, 0f);
            spr.anchorMax = new Vector2(1f, 1f);
            spr.offsetMin = new Vector2(blocksWidth + 14f, 0f);
            spr.offsetMax = Vector2.zero;
            _specialText = specialGo.GetComponent<TextMeshProUGUI>();
            _specialText.fontSize = UITypography.FontSnapped(m.SpecialFont);
            _specialText.fontStyle = FontStyles.Normal;
            _specialText.alignment = TextAlignmentOptions.MidlineLeft;
            _specialText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.75f);
            _specialText.textWrappingMode = TextWrappingModes.NoWrap;
            _specialText.overflowMode = TextOverflowModes.Overflow;
            _specialText.raycastTarget = false;

            // 7. 右侧「已选定」直角徽章
            _equippedBadge = new GameObject("EquippedBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(ComicBadge));
            _equippedBadge.transform.SetParent(transform, false);
            var eqRect = _equippedBadge.GetComponent<RectTransform>();
            eqRect.anchorMin = new Vector2(1f, 0.5f);
            eqRect.anchorMax = new Vector2(1f, 0.5f);
            eqRect.pivot = new Vector2(1f, 0.5f);
            eqRect.anchoredPosition = new Vector2(-14f, 0f);
            eqRect.sizeDelta = m.EquippedSize;

            _equippedBadgeBg = _equippedBadge.GetComponent<UIInkShapeGraphic>();
            _equippedBadgeBg.Shape = UIInkShape.SharpRect;
            _equippedBadgeBg.CornerRadius = ComicStyle.CornerSharp;
            _equippedBadgeBg.BorderWidth = ComicStyle.BorderThin;
            _equippedBadgeBg.BorderColor = ComicStyle.Ink;
            _equippedBadgeBg.color = ComicStyle.Yellow;
            _equippedBadgeBg.Halftone = false;
            _equippedBadgeBg.raycastTarget = false;
            _theme.ApplyHardShadow(_equippedBadgeBg, UIHardShadowLevel.Tag);

            _comicBadge = _equippedBadge.GetComponent<ComicBadge>();

            var eqTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            eqTextGo.transform.SetParent(_equippedBadge.transform, false);
            var eqtr = eqTextGo.GetComponent<RectTransform>();
            eqtr.anchorMin = Vector2.zero;
            eqtr.anchorMax = Vector2.one;
            eqtr.sizeDelta = Vector2.zero;
            _equippedBadgeText = eqTextGo.GetComponent<TextMeshProUGUI>();
            _equippedBadgeText.alignment = TextAlignmentOptions.Center;
            _equippedBadgeText.fontSize = UITypography.FontSnapped(m.EquippedFont);
            _equippedBadgeText.fontStyle = FontStyles.Bold;
            _equippedBadgeText.color = ComicStyle.Ink;
            _equippedBadgeText.text = "已选定";
            _equippedBadgeText.raycastTarget = false;
            _equippedBadgeText.font = _theme.ResolveFont(false, "已选定");
        }

        private void ApplyData(Sprite icon)
        {
            if (_indexBadgeText != null)
            {
                _indexBadgeText.text = (_index + 1).ToString();
            }

            if (_iconImage != null)
            {
                if (icon != null)
                {
                    _iconImage.sprite = icon;
                    _iconImage.color = Color.white;
                    _iconImage.gameObject.SetActive(true);
                }
                else
                {
                    _iconImage.gameObject.SetActive(false);
                }
            }

            RefreshIconPlate();

            string enName = WeaponSelectMath.GetWeaponEnglishName(_kind);
            string cnName = WeaponSelectMath.GetWeaponChineseName(_kind);
            string role = WeaponSelectMath.GetWeaponRoleTag(_kind);

            if (_titleText != null)
            {
                _titleText.font = _theme.ResolveFont(true, cnName);
                _titleText.text = cnName;
            }

            if (_enTitleText != null)
            {
                _enTitleText.font = _theme.GetFont(true);
                _enTitleText.text = enName.ToUpperInvariant();
            }

            if (_roleTagText != null)
            {
                _roleTagText.font = _theme.ResolveFont(false, role);
                _roleTagText.text = $"【{role}】";
            }

            string dmg = WeaponSelectMath.FormatDamage(_config, _kind);
            string rng = WeaponSelectMath.FormatRange(_config, _kind);
            string rate = WeaponSelectMath.FormatFireRate(_config, _kind);
            string ink = WeaponSelectMath.FormatInkCost(_config, _kind);

            if (_statsText != null)
            {
                if (_mobile)
                {
                    rate = StripParenthetical(CompactRate(rate));
                }
                string text = _mobile
                    ? $"伤{dmg} 距{rng} 频{rate} 耗{CompactPercent(ink)}"
                    : $"伤{dmg} 距{rng} 频{rate} 耗{ink}";
                _statsText.font = _theme.ResolveFont(false, text);
                _statsText.text = text;
            }

            string ult = WeaponSelectMath.FormatSpecial(_config, _kind);
            if (_mobile)
            {
                int paren = ult.IndexOf('(');
                if (paren > 0) ult = ult.Substring(0, paren).Trim();
            }
            if (_specialText != null)
            {
                string text = $"大招: {ult}";
                _specialText.font = _theme.ResolveFont(false, text);
                _specialText.text = text;
            }

            RefreshStatSegments();
        }

        /// <summary>移动端卡片较窄，频率去掉多余小数（10.0/s → 10/s）。</summary>
        private static string CompactRate(string rate)
        {
            return rate.Replace(".0/s", "/s").Replace(".0/sec", "/s");
        }

        /// <summary>移动端卡片较窄，去掉括号里的补充射速（架枪15/s）。</summary>
        private static string StripParenthetical(string value)
        {
            int paren = value.IndexOf('(');
            return paren > 0 ? value.Substring(0, paren).Trim() : value;
        }

        /// <summary>移动端卡片较窄，耗墨去掉多余小数（1.0% → 1%）。</summary>
        private static string CompactPercent(string percent)
        {
            return percent.Replace(".0%", "%");
        }

        private void RefreshIconPlate()
        {
            if (_iconPlate == null) return;
            _iconPlate.color = _theme.GetTeamColor(_team);
            _iconPlate.Refresh();
        }

        private void RefreshStatSegments()
        {
            Color teamColor = _theme.GetTeamColor(_team);
            for (int b = 0; b < _statSegments.Length; b++)
            {
                if (_statSegments[b] == null) continue;
                bool filled = b < _weaponRating;
                // 纸白卡面上空槽统一 PaperAlt（选中不再翻深灰，选中只靠色带/墨框表达）
                _statSegments[b].color = filled ? teamColor : ComicStyle.PaperAlt;
                _statSegments[b].Refresh();
            }
        }

        private void RefreshAccentBand()
        {
            if (_accentBand == null || _accentBandRect == null) return;

            float border = _isSelected ? CardBorderWidthSelected : CardBorderWidth;
            float height = _isSelected ? AccentBandHeightSelected : AccentBandHeight;
            _accentBandRect.sizeDelta = new Vector2(-border * 2f, height);
            _accentBandRect.anchoredPosition = new Vector2(0f, -border);
            _accentBand.color = GetWeaponAccentColor(_kind);
            _accentBand.Refresh();
        }

        private void RefreshSelectedVisuals()
        {
            Color accent = GetWeaponAccentColor(_kind);

            if (_bgGraphic != null)
            {
                // 选中态：仍是纸白卡面（信息层保持可读），墨框加粗到 4px + 色带加高到 8px
                _bgGraphic.color = ComicStyle.Paper;
                _bgGraphic.BorderWidth = CardBorderWidthSelected;
                _bgGraphic.BorderColor = ComicStyle.Ink;
                _bgGraphic.Halftone = false;
                _theme.ApplyHardShadow(_bgGraphic, UIHardShadowLevel.Panel);
                _bgGraphic.Refresh();
            }

            // 选中时圆盘描边换成武器身份色（纸白卡面上墨边不再有对比），身份色再次落点
            if (_iconPlate != null)
            {
                _iconPlate.BorderWidth = ComicStyle.Border;
                _iconPlate.BorderColor = accent;
                _iconPlate.Refresh();
            }

            if (_hotkeyBadgeBg != null)
            {
                _hotkeyBadgeBg.color = accent;
                _hotkeyBadgeBg.Refresh();
            }
            if (_indexBadgeText != null)
            {
                _indexBadgeText.color = Color.white;
            }

            if (_rectTransform != null)
            {
                _rectTransform.localRotation = Quaternion.identity;
            }

            if (_titleText != null) _titleText.color = ComicStyle.Ink;
            if (_enTitleText != null) _enTitleText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.65f);
            if (_roleTagText != null) _roleTagText.color = ComicStyle.Ink;
            if (_statsText != null) _statsText.color = ComicStyle.Ink;
            if (_specialText != null) _specialText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.75f);
        }

        private void RefreshUnselectedVisuals()
        {
            if (_bgGraphic != null)
            {
                _bgGraphic.color = ComicStyle.Paper;
                _bgGraphic.BorderWidth = CardBorderWidth;
                _bgGraphic.BorderColor = ComicStyle.Ink;
                _bgGraphic.Halftone = false;
                _theme.ApplyHardShadow(_bgGraphic, UIHardShadowLevel.Panel);
                _bgGraphic.Refresh();
            }

            if (_iconPlate != null)
            {
                _iconPlate.BorderWidth = ComicStyle.Border;
                _iconPlate.BorderColor = ComicStyle.Ink;
                _iconPlate.Refresh();
            }

            if (_hotkeyBadgeBg != null)
            {
                _hotkeyBadgeBg.color = ComicStyle.Ink;
                _hotkeyBadgeBg.Refresh();
            }
            if (_indexBadgeText != null)
            {
                _indexBadgeText.color = Color.white;
            }

            if (_rectTransform != null)
            {
                _rectTransform.localRotation = Quaternion.identity;
            }

            if (_titleText != null) _titleText.color = ComicStyle.Ink;
            if (_enTitleText != null) _enTitleText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.65f);
            if (_roleTagText != null) _roleTagText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.80f);
            if (_statsText != null) _statsText.color = ComicStyle.Ink;
            if (_specialText != null) _specialText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.75f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _pointerInside = true;
            if (_motion != null)
            {
                _motion.SetHovered(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _pointerInside = false;
            if (_motion != null)
            {
                _motion.SetHovered(false);
            }
        }
    }
}