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
    /// 单张武器卡片视图（P8 选枪系统）：
    /// - 包含武器图标、中英文名称、战术标签、四项关键数值（伤害/射程/射速/耗墨）及大招；
    /// - 选中态：队伍墨色填充 + 墨汁晃动 + 果冻弹簧缩放 + 装备勋章；
    /// - 未选态：暗黑深紫面板 + 细边框，鼠标悬停时微弹。
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
        private TextMeshProUGUI _statsText;
        private TextMeshProUGUI _specialText;
        private GameObject _equippedBadge;
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

            BuildUIHierarchy();
            ApplyData(icon);
            SetSelected(false, animate: false);
        }

        public void SetTeam(Team team)
        {
            _team = team;
            if (_isSelected)
            {
                RefreshSelectedVisuals();
            }
        }

        public void SetSelected(bool selected, bool animate = true)
        {
            bool wasSelected = _isSelected;
            _isSelected = selected;

            if (selected)
            {
                RefreshSelectedVisuals();
                if (animate && _motion != null)
                {
                    _motion.Punch(0.08f);
                    if (_bgGraphic != null)
                    {
                        _bgGraphic.PlayWobble(0.08f, 0.38f);
                    }
                }
            }
            else
            {
                RefreshUnselectedVisuals();
                if (wasSelected && animate && _motion != null)
                {
                    _motion.SnapHome();
                }
            }
        }

        private void BuildUIHierarchy()
        {
            _rectTransform = GetComponent<RectTransform>();
            _rectTransform.sizeDelta = new Vector2(440f, 108f);

            // 1. 卡片背景 (UIInkShapeGraphic)
            _bgGraphic = gameObject.GetComponent<UIInkShapeGraphic>();
            if (_bgGraphic == null)
            {
                _bgGraphic = gameObject.AddComponent<UIInkShapeGraphic>();
            }
            _bgGraphic.Shape = UIInkShape.RoundedRect;
            _bgGraphic.CornerRadius = 18f;
            _bgGraphic.BorderWidth = 5f;
            _bgGraphic.BorderColor = _theme.outlineColor;
            _bgGraphic.color = new Color(0.11f, 0.10f, 0.16f, 0.94f);
            _bgGraphic.raycastTarget = true;

            // 2. 弹簧交互控制器
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
            _button.transition = Selectable.Transition.None;
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => _onSelect?.Invoke(_index));

            // 4. 左侧快捷键角标容器 (e.g. [1])
            var badgeGo = new GameObject("HotkeyBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            badgeGo.transform.SetParent(transform, false);
            var badgeRect = badgeGo.GetComponent<RectTransform>();
            badgeRect.anchorMin = new Vector2(0f, 0.5f);
            badgeRect.anchorMax = new Vector2(0f, 0.5f);
            badgeRect.pivot = new Vector2(0f, 0.5f);
            badgeRect.anchoredPosition = new Vector2(10f, 0f);
            badgeRect.sizeDelta = new Vector2(24f, 24f);

            var badgeShape = badgeGo.GetComponent<UIInkShapeGraphic>();
            badgeShape.Shape = UIInkShape.RoundedRect;
            badgeShape.CornerRadius = 6f;
            badgeShape.BorderWidth = 2.5f;
            badgeShape.BorderColor = _theme.outlineColor;
            badgeShape.color = new Color(0.18f, 0.17f, 0.25f, 0.95f);
            badgeShape.raycastTarget = false;

            var badgeTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            badgeTextGo.transform.SetParent(badgeGo.transform, false);
            var btr = badgeTextGo.GetComponent<RectTransform>();
            btr.anchorMin = Vector2.zero;
            btr.anchorMax = Vector2.one;
            btr.sizeDelta = Vector2.zero;
            _indexBadgeText = badgeTextGo.GetComponent<TextMeshProUGUI>();
            _indexBadgeText.alignment = TextAlignmentOptions.Center;
            _indexBadgeText.fontSize = 14f;
            _indexBadgeText.fontStyle = FontStyles.Bold;
            _indexBadgeText.color = _theme.readyGold;
            _indexBadgeText.raycastTarget = false;

            // 5. 图标背景与图标
            var iconBgGo = new GameObject("IconBg", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            iconBgGo.transform.SetParent(transform, false);
            var iconBgRect = iconBgGo.GetComponent<RectTransform>();
            iconBgRect.anchorMin = new Vector2(0f, 0.5f);
            iconBgRect.anchorMax = new Vector2(0f, 0.5f);
            iconBgRect.pivot = new Vector2(0f, 0.5f);
            iconBgRect.anchoredPosition = new Vector2(40f, 0f);
            iconBgRect.sizeDelta = new Vector2(62f, 62f);

            var iconBgShape = iconBgGo.GetComponent<UIInkShapeGraphic>();
            iconBgShape.Shape = UIInkShape.RoundedRect;
            iconBgShape.CornerRadius = 12f;
            iconBgShape.BorderWidth = 3f;
            iconBgShape.BorderColor = _theme.outlineColor;
            iconBgShape.color = new Color(0.06f, 0.05f, 0.09f, 0.85f);
            iconBgShape.raycastTarget = false;

            var iconImgGo = new GameObject("IconImage", typeof(RectTransform), typeof(Image));
            iconImgGo.transform.SetParent(iconBgGo.transform, false);
            var iconImgRect = iconImgGo.GetComponent<RectTransform>();
            iconImgRect.anchorMin = new Vector2(0.08f, 0.08f);
            iconImgRect.anchorMax = new Vector2(0.92f, 0.92f);
            iconImgRect.sizeDelta = Vector2.zero;
            _iconImage = iconImgGo.GetComponent<Image>();
            _iconImage.preserveAspect = true;
            _iconImage.raycastTarget = false;

            // 6. 中部文字信息容器
            var infoGo = new GameObject("InfoContainer", typeof(RectTransform));
            infoGo.transform.SetParent(transform, false);
            var infoRect = infoGo.GetComponent<RectTransform>();
            infoRect.anchorMin = new Vector2(0f, 0f);
            infoRect.anchorMax = new Vector2(1f, 1f);
            infoRect.offsetMin = new Vector2(110f, 6f);
            infoRect.offsetMax = new Vector2(-76f, -6f);

            // 6.1 标题行 (Title)
            var titleGo = new GameObject("TitleText", typeof(RectTransform), typeof(TextMeshProUGUI));
            titleGo.transform.SetParent(infoGo.transform, false);
            var tr = titleGo.GetComponent<RectTransform>();
            tr.anchorMin = new Vector2(0f, 1f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0f, 1f);
            tr.anchoredPosition = new Vector2(0f, 0f);
            tr.sizeDelta = new Vector2(0f, 30f);
            _titleText = titleGo.GetComponent<TextMeshProUGUI>();
            _titleText.font = _theme.GetFont(true);
            _titleText.fontSize = 19f;
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.alignment = TextAlignmentOptions.Left;
            _titleText.raycastTarget = false;

            // 6.2 属性行 (Stats)
            var statsGo = new GameObject("StatsText", typeof(RectTransform), typeof(TextMeshProUGUI));
            statsGo.transform.SetParent(infoGo.transform, false);
            var sr = statsGo.GetComponent<RectTransform>();
            sr.anchorMin = new Vector2(0f, 0.5f);
            sr.anchorMax = new Vector2(1f, 0.5f);
            sr.pivot = new Vector2(0f, 0.5f);
            sr.anchoredPosition = new Vector2(0f, -2f);
            sr.sizeDelta = new Vector2(0f, 24f);
            _statsText = statsGo.GetComponent<TextMeshProUGUI>();
            _statsText.font = _theme.GetFont(false);
            _statsText.fontSize = 13.5f;
            _statsText.fontStyle = FontStyles.Normal;
            _statsText.alignment = TextAlignmentOptions.Left;
            _statsText.raycastTarget = false;

            // 6.3 大招行 (Special)
            var specialGo = new GameObject("SpecialText", typeof(RectTransform), typeof(TextMeshProUGUI));
            specialGo.transform.SetParent(infoGo.transform, false);
            var spr = specialGo.GetComponent<RectTransform>();
            spr.anchorMin = new Vector2(0f, 0f);
            spr.anchorMax = new Vector2(1f, 0f);
            spr.pivot = new Vector2(0f, 0f);
            spr.anchoredPosition = new Vector2(0f, 2f);
            spr.sizeDelta = new Vector2(0f, 22f);
            _specialText = specialGo.GetComponent<TextMeshProUGUI>();
            _specialText.font = _theme.GetFont(false);
            _specialText.fontSize = 12.5f;
            _specialText.fontStyle = FontStyles.Normal;
            _specialText.alignment = TextAlignmentOptions.Left;
            _specialText.raycastTarget = false;

            // 7. 右侧「已装备」徽章
            _equippedBadge = new GameObject("EquippedBadge", typeof(RectTransform), typeof(CanvasRenderer), typeof(UIInkShapeGraphic));
            _equippedBadge.transform.SetParent(transform, false);
            var eqRect = _equippedBadge.GetComponent<RectTransform>();
            eqRect.anchorMin = new Vector2(1f, 0.5f);
            eqRect.anchorMax = new Vector2(1f, 0.5f);
            eqRect.pivot = new Vector2(1f, 0.5f);
            eqRect.anchoredPosition = new Vector2(-10f, 0f);
            eqRect.sizeDelta = new Vector2(62f, 32f);

            _equippedBadgeBg = _equippedBadge.GetComponent<UIInkShapeGraphic>();
            _equippedBadgeBg.Shape = UIInkShape.RoundedRect;
            _equippedBadgeBg.CornerRadius = 8f;
            _equippedBadgeBg.BorderWidth = 3f;
            _equippedBadgeBg.BorderColor = _theme.outlineColor;
            _equippedBadgeBg.color = _theme.readyGold;
            _equippedBadgeBg.raycastTarget = false;

            var eqTextGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            eqTextGo.transform.SetParent(_equippedBadge.transform, false);
            var eqtr = eqTextGo.GetComponent<RectTransform>();
            eqtr.anchorMin = Vector2.zero;
            eqtr.anchorMax = Vector2.one;
            eqtr.sizeDelta = Vector2.zero;
            _equippedBadgeText = eqTextGo.GetComponent<TextMeshProUGUI>();
            _equippedBadgeText.font = _theme.GetFont(false);
            _equippedBadgeText.alignment = TextAlignmentOptions.Center;
            _equippedBadgeText.fontSize = 12f;
            _equippedBadgeText.fontStyle = FontStyles.Bold;
            _equippedBadgeText.color = _theme.outlineColor;
            _equippedBadgeText.text = "已选定";
            _equippedBadgeText.raycastTarget = false;
        }

        private void ApplyData(Sprite icon)
        {
            if (_indexBadgeText != null)
            {
                _indexBadgeText.text = (_index + 1).ToString();
            }

            if (_iconImage != null && icon != null)
            {
                _iconImage.sprite = icon;
                _iconImage.color = Color.white;
            }

            string enName = WeaponSelectMath.GetWeaponEnglishName(_kind);
            string cnName = WeaponSelectMath.GetWeaponChineseName(_kind);
            string role = WeaponSelectMath.GetWeaponRoleTag(_kind);

            if (_titleText != null)
            {
                _titleText.text = $"{enName} <size=14><color=#E0E4F0>{cnName}</color></size> <size=12><color=#FFD54F>【{role}】</color></size>";
            }

            string dmg = WeaponSelectMath.FormatDamage(_config, _kind);
            string rng = WeaponSelectMath.FormatRange(_config, _kind);
            string rate = WeaponSelectMath.FormatFireRate(_config, _kind);
            string ink = WeaponSelectMath.FormatInkCost(_config, _kind);

            if (_statsText != null)
            {
                _statsText.text = $"伤:{dmg} · 距:{rng} · 频:{rate} · 耗:{ink}";
            }

            string ult = WeaponSelectMath.FormatSpecial(_config, _kind);
            if (_specialText != null)
            {
                _specialText.text = $"大招: <color=#9EE8FF>{ult}</color>";
            }
        }

        private void RefreshSelectedVisuals()
        {
            if (_bgGraphic != null)
            {
                Color teamColor = _theme.GetTeamColor(_team);
                _bgGraphic.color = teamColor;
                _bgGraphic.BorderWidth = 6.5f;
                _bgGraphic.BorderColor = _theme.outlineColor;
                _bgGraphic.Refresh();
            }

            if (_equippedBadge != null)
            {
                _equippedBadge.SetActive(true);
            }

            if (_titleText != null)
            {
                _titleText.color = Color.white;
            }

            if (_statsText != null)
            {
                _statsText.color = new Color(0.98f, 0.98f, 1f, 1f);
            }
        }

        private void RefreshUnselectedVisuals()
        {
            if (_bgGraphic != null)
            {
                _bgGraphic.color = new Color(0.10f, 0.09f, 0.16f, 0.94f);
                _bgGraphic.BorderWidth = 4.5f;
                _bgGraphic.BorderColor = new Color(0.18f, 0.16f, 0.26f, 1f);
                _bgGraphic.Refresh();
            }

            if (_equippedBadge != null)
            {
                _equippedBadge.SetActive(false);
            }

            if (_titleText != null)
            {
                _titleText.color = _theme.textPrimary;
            }

            if (_statsText != null)
            {
                _statsText.color = _theme.textMuted;
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_motion != null)
            {
                _motion.SetHovered(true);
            }
            if (_bgGraphic != null)
            {
                _bgGraphic.PlayWobble(0.04f, 0.22f);
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
