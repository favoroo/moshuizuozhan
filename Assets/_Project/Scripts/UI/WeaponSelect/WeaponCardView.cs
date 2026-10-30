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
    /// 单张武器卡片视图（P8 选枪系统，P10 漫画风重构）：
    /// - 卡片高度 124px，纸面底色 + 6px 墨线粗边框 + 漫画硬阴影；
    /// - 选中态：漫画明黄底色 + 倾斜 -1.5° + ComicBadge 弹出「已选定」；
    /// - 悬停态：阴影减半 (3px) + 22% 半调网点浮现；
    /// - 热键徽章 [1]~[7] 放大至 44×44 墨底白字；
    /// - 武器名：中文 26px（黄油体）+ 英文 14px（TitanOne）；
    /// - 属性条：5 段粗块（每段 4px 墨边），段内填充队伍色；
    /// - 图标为空时隐藏 Image 组件，杜绝白块。
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

        private RectTransform _rectTransform;
        private UIInkShapeGraphic _bgGraphic;
        private UISpringMotion _motion;
        private Button _button;

        private Image _iconImage;
        private TextMeshProUGUI _indexBadgeText;
        private TextMeshProUGUI _titleText;
        private TextMeshProUGUI _enTitleText;
        private TextMeshProUGUI _roleTagText;
        private TextMeshProUGUI _statsText;
        private TextMeshProUGUI _specialText;

        // 5 段粗块属性条
        private readonly UIInkShapeGraphic[] _statSegments = new UIInkShapeGraphic[5];
        private int _weaponRating = 3;

        // ComicBadge「已选定」徽章
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
            _weaponRating = GetWeaponRating(kind);

            BuildUIHierarchy();
            ApplyData(icon);
            SetSelected(false, animate: false);
        }

        private static int GetWeaponRating(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Shooter: return 3;
                case WeaponKind.Roller: return 4;
                case WeaponKind.Charger: return 5;
                case WeaponKind.Dualies: return 3;
                case WeaponKind.Slosher: return 4;
                case WeaponKind.Blaster: return 4;
                case WeaponKind.Splatling: return 5;
                default: return 3;
            }
        }

        public void SetTeam(Team team)
        {
            _team = team;
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
                    _motion.Punch(0.06f);
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
        }

        private void BuildUIHierarchy()
        {
            _rectTransform = GetComponent<RectTransform>();
            _rectTransform.sizeDelta = new Vector2(540f, 124f);

            // 1. 卡片背景 (纸面 + 6px 墨边 + 硬阴影)
            _bgGraphic = gameObject.GetComponent<UIInkShapeGraphic>();
            if (_bgGraphic == null)
            {
                _bgGraphic = gameObject.AddComponent<UIInkShapeGraphic>();
            }
            _bgGraphic.Shape = UIInkShape.RoundedRect;
            _bgGraphic.CornerRadius = ComicStyle.CornerSharp;
            _bgGraphic.BorderWidth = ComicStyle.Border;
            _bgGraphic.BorderColor = ComicStyle.Ink;
            _bgGraphic.color = ComicStyle.Paper;
            _bgGraphic.HardShadow = true;
            _bgGraphic.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
            _bgGraphic.ShadowColor = ComicStyle.Ink;
            _bgGraphic.Halftone = false;
            _bgGraphic.HalftoneAlpha = ComicStyle.HalftoneAlpha;
            _bgGraphic.GlossAlpha = 0f;
            _bgGraphic.DrawGloss = false;
            _bgGraphic.raycastTarget = true;

            // 2. 动效控制器 (严格遵守漫画动效规范，无弹簧回弹)
            _motion = gameObject.GetComponent<UISpringMotion>();
            if (_motion == null)
            {
                _motion = gameObject.AddComponent<UISpringMotion>();
            }
            _motion.SetTheme(_theme);

            // 3. 按钮点击触发
            _button = gameObject.GetComponent<Button>();
            if (_button == null)
            {
                _button = gameObject.AddComponent<Button>();
            }
            _button.targetGraphic = _bgGraphic;
            _button.transition = Selectable.Transition.None;
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => _onSelect?.Invoke(_index));

            // 4. 左侧快捷键角标 [1]~[7] 放大到 44×44 墨底白字
            var badgeGo = new GameObject("HotkeyBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            badgeGo.transform.SetParent(transform, false);
            var badgeRect = badgeGo.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0f, 0.5f);
            badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(12f, 0f);
            badgeRect.sizeDelta = new Vector2(44f, 44f);

            var badgeShape = badgeGo.GetComponent<UIInkShapeGraphic>();
            badgeShape.Shape = UIInkShape.RoundedRect;
            badgeShape.CornerRadius = ComicStyle.CornerSharp;
            badgeShape.BorderWidth = ComicStyle.BorderThin;
            badgeShape.BorderColor = ComicStyle.Ink;
            badgeShape.color = ComicStyle.Ink;
            badgeShape.HardShadow = false;
            badgeShape.Halftone = false;
            badgeShape.raycastTarget = false;

            var badgeTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            badgeTextGo.transform.SetParent(badgeGo.transform, false);
            var btr = badgeTextGo.GetComponent<RectTransform>();
            btr.anchorMin = Vector2.zero;
            btr.anchorMax = Vector2.one;
            btr.sizeDelta = Vector2.zero;
            _indexBadgeText = badgeTextGo.GetComponent<TextMeshProUGUI>();
            _indexBadgeText.alignment = TextAlignmentOptions.Center;
            _indexBadgeText.fontSize = 24f;
            _indexBadgeText.fontStyle = FontStyles.Bold;
            _indexBadgeText.color = Color.white;
            _indexBadgeText.raycastTarget = false;
            _indexBadgeText.font = _theme.ResolveFont(true, (_index + 1).ToString());

            // 5. 图标底框与图标
            var iconBgGo = new GameObject("IconBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            iconBgGo.transform.SetParent(transform, false);
            var iconBgRect = iconBgGo.GetComponent<RectTransform>();
            iconBgRect.anchorMin = new Vector2(0f, 0.5f);
            iconBgRect.anchorMax = new Vector2(0f, 0.5f);
            iconBgRect.pivot = new Vector2(0f, 0.5f);
            iconBgRect.anchoredPosition = new Vector2(66f, 0f);
            iconBgRect.sizeDelta = new Vector2(76f, 76f);

            var iconBgShape = iconBgGo.GetComponent<UIInkShapeGraphic>();
            iconBgShape.Shape = UIInkShape.RoundedRect;
            iconBgShape.CornerRadius = ComicStyle.CornerSharp;
            iconBgShape.BorderWidth = ComicStyle.BorderThin;
            iconBgShape.BorderColor = ComicStyle.Ink;
            // 武器图标是纯白剪影，底框必须用墨色，否则白图标贴在纸白底上等于隐形/白方块
            iconBgShape.color = ComicStyle.Ink;
            iconBgShape.HardShadow = false;
            iconBgShape.Halftone = false;
            iconBgShape.raycastTarget = false;

            var iconImgGo = new GameObject("IconImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            iconImgGo.transform.SetParent(iconBgGo.transform, false);
            var iconImgRect = iconImgGo.GetComponent<RectTransform>();
            iconImgRect.anchorMin = new Vector2(0.06f, 0.06f);
            iconImgRect.anchorMax = new Vector2(0.94f, 0.94f);
            iconImgRect.sizeDelta = Vector2.zero;
            _iconImage = iconImgGo.GetComponent<Image>();
            _iconImage.preserveAspect = true;
            _iconImage.raycastTarget = false;

            // 6. 中部文字与属性容器
            var infoGo = new GameObject("InfoContainer", typeof(RectTransform));
            infoGo.transform.SetParent(transform, false);
            var infoRect = infoGo.GetComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0f, 0f);
            infoRect.anchorMax = new Vector2(1f, 1f);
            infoRect.offsetMin = new Vector2(152f, 8f);
            infoRect.offsetMax = new Vector2(-100f, -8f);

            // 6.1 标题行（中文 26px 庆科黄油体 + 英文 14px + 定位标签）
            var titleRow = new GameObject("TitleRow", typeof(RectTransform));
            titleRow.transform.SetParent(infoGo.transform, false);
            var titleRowRt = titleRow.GetComponent<RectTransform>();
            titleRowRt.anchorMin = new Vector2(0f, 1f);
            titleRowRt.anchorMax = new Vector2(1f, 1f);
            titleRowRt.pivot = new Vector2(0f, 1f);
            titleRowRt.anchoredPosition = new Vector2(0f, 0f);
            titleRowRt.sizeDelta = new Vector2(0f, 36f);

            var cnNameGo = new GameObject("CnName", typeof(RectTransform), typeof(TextMeshProUGUI));
            cnNameGo.transform.SetParent(titleRow.transform, false);
            var cnNameRt = cnNameGo.GetComponent<RectTransform>();
            cnNameRt.anchorMin = new Vector2(0f, 0.5f);
            cnNameRt.anchorMax = new Vector2(0f, 0.5f);
            cnNameRt.pivot = new Vector2(0f, 0.5f);
            cnNameRt.anchoredPosition = new Vector2(0f, 0f);
            cnNameRt.sizeDelta = new Vector2(118f, 32f);
            _titleText = cnNameGo.GetComponent<TextMeshProUGUI>();
            _titleText.fontSize = 24f;
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.alignment = TextAlignmentOptions.Left;
            _titleText.color = ComicStyle.Ink;
            _titleText.raycastTarget = false;

            var enNameGo = new GameObject("EnName", typeof(RectTransform), typeof(TextMeshProUGUI));
            enNameGo.transform.SetParent(titleRow.transform, false);
            var enNameRt = enNameGo.GetComponent<RectTransform>();
            enNameRt.anchorMin = new Vector2(0f, 0.5f);
            enNameRt.anchorMax = new Vector2(0f, 0.5f);
            enNameRt.pivot = new Vector2(0f, 0.5f);
            enNameRt.anchoredPosition = new Vector2(122f, -3f);
            enNameRt.sizeDelta = new Vector2(112f, 20f);
            _enTitleText = enNameGo.GetComponent<TextMeshProUGUI>();
            _enTitleText.fontSize = 11.5f;
            _enTitleText.fontStyle = FontStyles.Bold;
            _enTitleText.alignment = TextAlignmentOptions.Left;
            _enTitleText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.65f);
            _enTitleText.raycastTarget = false;

            var roleGo = new GameObject("RoleTag", typeof(RectTransform), typeof(TextMeshProUGUI));
            roleGo.transform.SetParent(titleRow.transform, false);
            var roleRt = roleGo.GetComponent<RectTransform>();
            roleRt.anchorMin = new Vector2(1f, 0.5f);
            roleRt.anchorMax = new Vector2(1f, 0.5f);
            roleRt.pivot = new Vector2(1f, 0.5f);
            roleRt.anchoredPosition = new Vector2(0f, 0f);
            roleRt.sizeDelta = new Vector2(88f, 22f);
            _roleTagText = roleGo.GetComponent<TextMeshProUGUI>();
            _roleTagText.fontSize = 11.5f;
            _roleTagText.fontStyle = FontStyles.Bold;
            _roleTagText.alignment = TextAlignmentOptions.Right;
            _roleTagText.color = ComicStyle.Ink;
            _roleTagText.raycastTarget = false;

            // 6.2 5 段粗块属性条（每段 4px 墨边，段内填充队伍色）
            var statBarRow = new GameObject("StatBarRow", typeof(RectTransform));
            statBarRow.transform.SetParent(infoGo.transform, false);
            var statBarRowRt = statBarRow.GetComponent<RectTransform>();
            statBarRowRt.anchorMin = new Vector2(0f, 0.5f);
            statBarRowRt.anchorMax = new Vector2(1f, 0.5f);
            statBarRowRt.pivot = new Vector2(0f, 0.5f);
            statBarRowRt.anchoredPosition = new Vector2(0f, -4f);
            statBarRowRt.sizeDelta = new Vector2(0f, 28f);

            var blocksGo = new GameObject("ChunkyBlocks", typeof(RectTransform));
            blocksGo.transform.SetParent(statBarRow.transform, false);
            var blocksRt = blocksGo.GetComponent<RectTransform>();
            blocksRt.anchorMin = new Vector2(0f, 0.5f);
            blocksRt.anchorMax = new Vector2(0f, 0.5f);
            blocksRt.pivot = new Vector2(0f, 0.5f);
            blocksRt.anchoredPosition = new Vector2(0f, 0f);
            blocksRt.sizeDelta = new Vector2(136f, 20f);

            float blockWidth = 23f;
            float blockGap = 4f;
            for (int b = 0; b < 5; b++)
            {
                var blockGo = new GameObject($"Block_{b}", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
                blockGo.transform.SetParent(blocksGo.transform, false);
                var brt = blockGo.GetComponent<RectTransform>();
                brt.anchorMin = new Vector2(0f, 0.5f);
                brt.anchorMax = new Vector2(0f, 0.5f);
                brt.pivot = new Vector2(0f, 0.5f);
                brt.anchoredPosition = new Vector2(b * (blockWidth + blockGap), 0f);
                brt.sizeDelta = new Vector2(blockWidth, 18f);

                var bshape = blockGo.GetComponent<UIInkShapeGraphic>();
                bshape.Shape = UIInkShape.RoundedRect;
                bshape.CornerRadius = 3f;
                bshape.BorderWidth = ComicStyle.BorderThin;
                bshape.BorderColor = ComicStyle.Ink;
                bshape.color = ComicStyle.PaperAlt;
                bshape.HardShadow = false;
                bshape.Halftone = false;
                bshape.raycastTarget = false;

                _statSegments[b] = bshape;
            }

            // 属性文字（伤/距/频/耗）
            var statsGo = new GameObject("StatsText", typeof(RectTransform), typeof(TextMeshProUGUI));
            statsGo.transform.SetParent(statBarRow.transform, false);
            var sr = statsGo.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 0.5f);
            sr.anchorMax = new Vector2(1f, 0.5f);
            sr.pivot = new Vector2(0f, 0.5f);
            sr.anchoredPosition = new Vector2(144f, 0f);
            sr.sizeDelta = new Vector2(-144f, 26f);
            _statsText = statsGo.GetComponent<TextMeshProUGUI>();
            _statsText.fontSize = 12f;
            _statsText.fontStyle = FontStyles.Normal;
            _statsText.alignment = TextAlignmentOptions.Left;
            _statsText.color = ComicStyle.Ink;
            _statsText.raycastTarget = false;

            // 6.3 大招信息行
            var specialGo = new GameObject("SpecialText", typeof(RectTransform), typeof(TextMeshProUGUI));
            specialGo.transform.SetParent(infoGo.transform, false);
            var spr = specialGo.GetComponent<RectTransform>();
            spr.anchorMin = new Vector2(0f, 0f);
            spr.anchorMax = new Vector2(1f, 0f);
            spr.pivot = new Vector2(0f, 0f);
            spr.anchoredPosition = new Vector2(0f, 2f);
            spr.sizeDelta = new Vector2(0f, 24f);
            _specialText = specialGo.GetComponent<TextMeshProUGUI>();
            _specialText.fontSize = 12f;
            _specialText.fontStyle = FontStyles.Normal;
            _specialText.alignment = TextAlignmentOptions.Left;
            _specialText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.75f);
            _specialText.raycastTarget = false;

            // 7. 右侧「已选定」ComicBadge 徽章
            _equippedBadge = new GameObject("EquippedBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic), typeof(ComicBadge));
            _equippedBadge.transform.SetParent(transform, false);
            var eqRect = _equippedBadge.GetComponent<RectTransform>();
            eqRect.anchorMin = new Vector2(1f, 0.5f);
            eqRect.anchorMax = new Vector2(1f, 0.5f);
            eqRect.pivot = new Vector2(1f, 0.5f);
            eqRect.anchoredPosition = new Vector2(-14f, 0f);
            eqRect.sizeDelta = new Vector2(84f, 36f);

            _equippedBadgeBg = _equippedBadge.GetComponent<UIInkShapeGraphic>();
            _equippedBadgeBg.Shape = UIInkShape.RoundedRect;
            _equippedBadgeBg.CornerRadius = ComicStyle.CornerSharp;
            _equippedBadgeBg.BorderWidth = ComicStyle.BorderThin;
            _equippedBadgeBg.BorderColor = ComicStyle.Ink;
            _equippedBadgeBg.color = ComicStyle.Paper;
            _equippedBadgeBg.HardShadow = true;
            _equippedBadgeBg.ShadowOffset = new Vector2(3f, -3f);
            _equippedBadgeBg.ShadowColor = ComicStyle.Ink;
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
            _equippedBadgeText.fontSize = 15f;
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

            // 图标：存在时显示并上色，为空时隐藏 Image 组件杜绝白底空块
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

            string enName = WeaponSelectMath.GetWeaponEnglishName(_kind);
            string cnName = WeaponSelectMath.GetWeaponChineseName(_kind);
            string role = WeaponSelectMath.GetWeaponRoleTag(_kind);

            // 统一通过 theme.ResolveFont 绑定 OFL CJK 字体，彻底修掉方块字 □
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
                string text = $"伤{dmg} 距{rng} 频{rate} 耗{ink}";
                _statsText.font = _theme.ResolveFont(false, text);
                _statsText.text = text;
            }

            string ult = WeaponSelectMath.FormatSpecial(_config, _kind);
            if (_specialText != null)
            {
                string text = $"大招: {ult}";
                _specialText.font = _theme.ResolveFont(false, text);
                _specialText.text = text;
            }

            RefreshStatSegments();
        }

        private void RefreshStatSegments()
        {
            Color teamColor = _theme.GetTeamColor(_team);
            for (int b = 0; b < _statSegments.Length; b++)
            {
                if (_statSegments[b] == null) continue;
                bool filled = b < _weaponRating;
                _statSegments[b].color = filled ? teamColor : ComicStyle.PaperAlt;
                _statSegments[b].Refresh();
            }
        }

        private void RefreshSelectedVisuals()
        {
            if (_bgGraphic != null)
            {
                // 选中态：黄底 + 6px 墨边 + 硬阴影
                _bgGraphic.color = ComicStyle.Yellow;
                _bgGraphic.BorderWidth = ComicStyle.Border;
                _bgGraphic.BorderColor = ComicStyle.Ink;
                _bgGraphic.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
                _bgGraphic.Halftone = false;
                _bgGraphic.Refresh();
            }

            // 倾斜 -1.5°，营造漫画贴纸活泼张力
            if (_rectTransform != null)
            {
                _rectTransform.localRotation = Quaternion.Euler(0f, 0f, -1.5f);
            }

            if (_titleText != null) _titleText.color = ComicStyle.Ink;
            if (_enTitleText != null) _enTitleText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.7f);
            if (_roleTagText != null) _roleTagText.color = ComicStyle.Ink;
            if (_statsText != null) _statsText.color = ComicStyle.Ink;
            if (_specialText != null) _specialText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.85f);
        }

        private void RefreshUnselectedVisuals()
        {
            if (_bgGraphic != null)
            {
                // 未选态：纸面白底 + 6px 墨边 + 硬阴影
                _bgGraphic.color = ComicStyle.Paper;
                _bgGraphic.BorderWidth = ComicStyle.Border;
                _bgGraphic.BorderColor = ComicStyle.Ink;
                _bgGraphic.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
                _bgGraphic.Halftone = false;
                _bgGraphic.Refresh();
            }

            if (_rectTransform != null)
            {
                _rectTransform.localRotation = Quaternion.identity;
            }

            if (_titleText != null) _titleText.color = ComicStyle.Ink;
            if (_enTitleText != null) _enTitleText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.65f);
            if (_roleTagText != null) _roleTagText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.75f);
            if (_statsText != null) _statsText.color = ComicStyle.Ink;
            if (_specialText != null) _specialText.color = ComicStyle.WithAlpha(ComicStyle.Ink, 0.75f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_isSelected && _bgGraphic != null)
            {
                // 悬停：硬阴影缩减一半 (3px) + 22% 半调网点浮现
                _bgGraphic.ShadowOffset = new Vector2(ComicStyle.HoverShadowOffset, -ComicStyle.HoverShadowOffset);
                _bgGraphic.Halftone = true;
                _bgGraphic.HalftoneAlpha = ComicStyle.HalftoneHoverAlpha;
                _bgGraphic.Refresh();
            }

            if (_motion != null)
            {
                _motion.SetHovered(true);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_isSelected && _bgGraphic != null)
            {
                _bgGraphic.ShadowOffset = new Vector2(ComicStyle.ShadowOffset, -ComicStyle.ShadowOffset);
                _bgGraphic.Halftone = false;
                _bgGraphic.Refresh();
            }

            if (_motion != null)
            {
                _motion.SetHovered(false);
            }
        }
    }
}
