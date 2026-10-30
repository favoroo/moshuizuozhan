using System;
using P1.Core;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace P1.UI.WeaponSelect
{
    /// <summary>
    /// 单张武器卡片视图（P14 几何大胆风 Geometric Bold · P14-I 移动端重排）：
    /// - 卡片尺寸/字号全部来自 <see cref="MenuLayoutMath.Drawer"/>，移动端是一套显式更大的数值
    ///   （桌面 592×164，移动端 759×260），不再只把字号 ×1.28 而盒子不动；
    /// - 图标从「直愣愣的黑方块」改成**正圆色盘**：队伍色填充 + 4px 墨边 + 白色武器剪影，
    ///   选中时圆盘描边翻纸白，和墨底卡片形成硬切对比；
    /// - 右侧 7 段直角属性条：移动端 28px 方块（桌面 18px），段内填充队伍色；
    /// - 快捷键方块 [1]~[7] 只在桌面显示（手机没有数字键，占地方且误导）；
    /// - 图标为空时隐藏 Image，绝不出现白块。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class WeaponCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
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
        private UISpringMotion _motion;
        private Button _button;

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
                if (_comicBadge != null)
                {
                    if (animate) _comicBadge.Play();
                    else _comicBadge.ShowImmediate();
                }
                if (animate && _motion != null)
                {
                    _motion.Punch(0.04f);
                }
            }
            else
            {
                RefreshUnselectedVisuals();
                if (_comicBadge != null)
                {
                    _comicBadge.HideImmediate();
                }
            }

            RefreshStatSegments();
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

            // 1. 直角卡片背景（白底墨边；选中态由 Refresh*Visuals 硬切）
            _bgGraphic = gameObject.GetComponent<UIInkShapeGraphic>();
            if (_bgGraphic == null)
            {
                _bgGraphic = gameObject.AddComponent<UIInkShapeGraphic>();
            }
            _bgGraphic.Shape = UIInkShape.SharpRect;
            _bgGraphic.CornerRadius = ComicStyle.CornerSharp;
            _bgGraphic.BorderWidth = ComicStyle.Border;
            _bgGraphic.BorderColor = ComicStyle.Ink;
            _bgGraphic.color = ComicStyle.Paper;
            _bgGraphic.HardShadow = false;
            _bgGraphic.Halftone = false;
            _bgGraphic.GlossAlpha = 0f;
            _bgGraphic.DrawGloss = false;
            _bgGraphic.raycastTarget = true;

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
                    -(10f + m.IndexSize.y + 8f + iconDiameter * 0.5f));
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
                badgeRect.anchoredPosition = new Vector2(iconCenterX, -(10f + m.IndexSize.y * 0.5f));
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
            infoRect.offsetMax = new Vector2(-equippedReserve, -(_mobile ? 12f : 10f));

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
            _equippedBadgeBg.HardShadow = false;
            _equippedBadgeBg.Halftone = false;
            _equippedBadgeBg.raycastTarget = false;

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
                _statSegments[b].color = filled ? teamColor : (_isSelected ? ComicStyle.FromHex(0x353535) : ComicStyle.PaperAlt);
                _statSegments[b].Refresh();
            }
        }

        private void RefreshSelectedVisuals()
        {
            Color teamColor = _theme.GetTeamColor(_team);

            if (_bgGraphic != null)
            {
                // 选中态：墨底黑块 + 队伍色粗边硬切
                _bgGraphic.color = ComicStyle.Ink;
                _bgGraphic.BorderWidth = ComicStyle.BorderHeavy;
                _bgGraphic.BorderColor = teamColor;
                _bgGraphic.HardShadow = false;
                _bgGraphic.Halftone = false;
                _bgGraphic.Refresh();
            }

            // 墨底上给色盘换纸白描边，避免圆盘和底色糊在一起
            if (_iconPlate != null)
            {
                _iconPlate.BorderWidth = ComicStyle.Border;
                _iconPlate.BorderColor = ComicStyle.Paper;
                _iconPlate.Refresh();
            }

            if (_hotkeyBadgeBg != null)
            {
                _hotkeyBadgeBg.color = teamColor;
                _hotkeyBadgeBg.Refresh();
            }
            if (_indexBadgeText != null)
            {
                _indexBadgeText.color = ComicStyle.Ink;
            }

            if (_rectTransform != null)
            {
                _rectTransform.localRotation = Quaternion.identity;
            }

            if (_titleText != null) _titleText.color = ComicStyle.Paper;
            if (_enTitleText != null) _enTitleText.color = ComicStyle.WithAlpha(ComicStyle.Paper, 0.75f);
            if (_roleTagText != null) _roleTagText.color = ComicStyle.Yellow;
            if (_statsText != null) _statsText.color = ComicStyle.Paper;
            if (_specialText != null) _specialText.color = ComicStyle.WithAlpha(ComicStyle.Paper, 0.85f);
        }

        private void RefreshUnselectedVisuals()
        {
            if (_bgGraphic != null)
            {
                _bgGraphic.color = ComicStyle.Paper;
                _bgGraphic.BorderWidth = ComicStyle.Border;
                _bgGraphic.BorderColor = ComicStyle.Ink;
                _bgGraphic.HardShadow = false;
                _bgGraphic.Halftone = false;
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
            if (_motion != null)
            {
                _motion.SetHovered(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_motion != null)
            {
                _motion.SetHovered(false);
            }
        }
    }
}