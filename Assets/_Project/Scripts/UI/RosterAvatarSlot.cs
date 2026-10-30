using P1.Core;
using P1.Player;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 4v4 阵容条单个队员头像槽位（P4-B）。
    ///
    /// 涵盖：
    /// 1. 存活态：底色为队色，乌贼图标饱满纯白；
    /// 2. 阵亡态：底色变灰紫黑 (#4A4458)，乌贼半透明收缩，显现骷髅/X 标，顺时针进度环显示复活倒计时，右下角标显示剩余秒数；
    /// 3. 复活瞬间：弹簧果冻弹入放大 (Pop/Punch) + 墨滴晃动；
    /// 4. 大招满能：外围旋转金色光环 (UIRotatingRingGraphic)；
    /// 5. 本地玩家：正下方小白三角指示箭头。
    /// </summary>
    [AddComponentMenu("P1/UI/Roster Avatar Slot")]
    public class RosterAvatarSlot : MonoBehaviour
    {
        [Header("核心组件引用")]
        [SerializeField] private UIInkShapeGraphic backgroundBlob;
        [SerializeField] private Image squidIcon;
        [SerializeField] private Image deathIcon;
        [SerializeField] private UIProgressRingGraphic progressRing;
        [SerializeField] private UIRotatingRingGraphic specialReadyRing;

        [Tooltip("几何风就绪角标（右上角纯黄方块）")]
        [SerializeField] private UIInkShapeGraphic specialReadyPip;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private RectTransform countdownBadge;
        [SerializeField] private Image youIndicatorArrow;
        [SerializeField] private UISpringMotion springMotion;

        [Header("主题与队伍")]
        [SerializeField] private UIThemeSO theme;
        [SerializeField] private Team team = Team.TeamA;
        [SerializeField] private bool isLocalPlayer;

        [Header("漫画风兜底组件")]
        [SerializeField] private GameObject fallbackSquid;
        [SerializeField] private GameObject fallbackDeath;
        [SerializeField] private GameObject youBadge;

        private CombatantHealth _boundCombatant;
        private WeaponController _boundWeapon;
        private bool _wasAlive = true;
        private bool _wasSpecialReady;
        private bool _hasBoundTarget;
        private int _lastDisplayedSecs = -1;

        public Team SlotTeam => team;
        public bool IsLocalPlayer => isLocalPlayer;
        public CombatantHealth BoundCombatant => _boundCombatant;

        private static readonly Color DeadColor = ComicStyle.Red;
        private static readonly Color GoldRingColor = new Color(1f, 0.835f, 0.29f, 1f);

        private void Awake()
        {
            if (springMotion == null) springMotion = GetComponent<UISpringMotion>();
            ResolveFallbacks();
        }

        public void SetFallbacks(GameObject fbSquid, GameObject fbDeath, GameObject you)
        {
            fallbackSquid = fbSquid;
            fallbackDeath = fbDeath;
            youBadge = you;
        }

        private void ResolveFallbacks()
        {
            if (fallbackSquid == null)
            {
                var t = transform.Find("SquidFallback");
                if (t != null) fallbackSquid = t.gameObject;
            }
            if (fallbackDeath == null)
            {
                var t = transform.Find("DeathFallback");
                if (t != null) fallbackDeath = t.gameObject;
            }
            if (youBadge == null)
            {
                var t = transform.Find("YouIndicator");
                if (t != null) youBadge = t.gameObject;
            }
        }

        public void BindCombatant(CombatantHealth combatant, bool isPlayer = false, UIThemeSO themeAsset = null)
        {
            _boundCombatant = combatant;
            isLocalPlayer = isPlayer;
            if (themeAsset != null) theme = themeAsset;

            if (combatant != null)
            {
                team = combatant.Team;
                _boundWeapon = combatant.GetComponent<WeaponController>();
                _wasAlive = combatant.IsAlive;
                _wasSpecialReady = _boundWeapon != null && _boundWeapon.IsSpecialReady;
                _hasBoundTarget = true;
            }
            else
            {
                _hasBoundTarget = false;
                _boundWeapon = null;
            }

            if (youIndicatorArrow != null)
            {
                youIndicatorArrow.gameObject.SetActive(isLocalPlayer);
            }
            if (youBadge != null)
            {
                youBadge.SetActive(isLocalPlayer);
            }

            RefreshVisualsImmediate();
        }

        public void ConfigureComponents(
            UIInkShapeGraphic bg,
            Image squid,
            Image death,
            UIProgressRingGraphic ring,
            UIRotatingRingGraphic specialRing,
            TMP_Text countText,
            RectTransform countBadge,
            Image youArrow,
            UISpringMotion motion,
            UIThemeSO themeAsset,
            Team initialTeam = Team.TeamA)
        {
            backgroundBlob = bg;
            squidIcon = squid;
            deathIcon = death;
            progressRing = ring;
            specialReadyRing = specialRing;
            countdownText = countText;
            countdownBadge = countBadge;
            youIndicatorArrow = youArrow;
            springMotion = motion;
            theme = themeAsset;
            team = initialTeam;
        }

        private void LateUpdate()
        {
            if (!_hasBoundTarget || _boundCombatant == null)
            {
                // 无绑定目标时淡化空位
                return;
            }

            UpdateState();
        }

        public void UpdateState()
        {
            if (_boundCombatant == null) return;

            bool isAlive = _boundCombatant.IsAlive;
            float remaining = _boundCombatant.RespawnRemaining;
            float duration = 4.0f; // 默认复活时长

            bool isSpecialReady = isAlive && _boundWeapon != null && _boundWeapon.IsSpecialReady;

            // 1. 复活检测：死亡 -> 存活
            if (!_wasAlive && isAlive)
            {
                OnRespawned();
            }
            // 2. 阵亡瞬间：存活 -> 死亡
            else if (_wasAlive && !isAlive)
            {
                OnKilled();
            }

            // 3. 大招就绪检测
            if (!_wasSpecialReady && isSpecialReady)
            {
                OnSpecialReadyTriggered();
            }

            _wasAlive = isAlive;
            _wasSpecialReady = isSpecialReady;

            // 4. 驱动视觉
            UpdateVisuals(isAlive, remaining, duration, isSpecialReady);
        }

        private void UpdateVisuals(bool isAlive, float remaining, float duration, bool isSpecialReady)
        {
            UIThemeSO resolvedTheme = UIThemeSO.Resolve(theme);
            Color teamColor = resolvedTheme.GetTeamColor(team);

            // 背景底色与形状：存活为队色纯色块，阵亡为黑底纯色块
            if (backgroundBlob != null)
            {
                backgroundBlob.color = isAlive ? teamColor : ComicStyle.Ink;
            }

            // 乌贼图标三态与兜底（绝不留白块）
            if (squidIcon != null)
            {
                if (isAlive)
                {
                    squidIcon.gameObject.SetActive(true);
                    if (squidIcon.sprite != null)
                    {
                        squidIcon.enabled = true;
                        squidIcon.color = Color.white;
                        squidIcon.transform.localScale = Vector3.one;
                        if (fallbackSquid != null) fallbackSquid.SetActive(false);
                    }
                    else
                    {
                        squidIcon.enabled = false;
                        if (fallbackSquid != null) fallbackSquid.SetActive(true);
                    }
                }
                else
                {
                    squidIcon.gameObject.SetActive(false);
                    if (fallbackSquid != null) fallbackSquid.SetActive(false);
                }
            }

            // 阵亡 X 标（纯 Red 红 X）与兜底
            if (deathIcon != null)
            {
                deathIcon.gameObject.SetActive(!isAlive);
                if (!isAlive)
                {
                    if (deathIcon.sprite != null)
                    {
                        deathIcon.enabled = true;
                        deathIcon.color = ComicStyle.Red;
                        if (fallbackDeath != null) fallbackDeath.SetActive(false);
                    }
                    else
                    {
                        deathIcon.enabled = false;
                        if (fallbackDeath != null) fallbackDeath.SetActive(true);
                    }
                }
                else
                {
                    if (fallbackDeath != null) fallbackDeath.SetActive(false);
                }
            }

            // 阵亡进度环与角标（5px 加粗环，红字红环）
            if (progressRing != null)
            {
                if (!isAlive && remaining > 0f)
                {
                    progressRing.gameObject.SetActive(true);
                    float fill = Mathf.Clamp01(1f - (remaining / Mathf.Max(0.1f, duration)));
                    progressRing.Progress = fill;
                    progressRing.ProgressColor = ComicStyle.Red;
                }
                else
                {
                    progressRing.gameObject.SetActive(false);
                    progressRing.Progress = 0f;
                }
            }

            // 倒计时文字角标（红字）
            if (countdownBadge != null)
            {
                countdownBadge.gameObject.SetActive(!isAlive && remaining > 0f);
            }

            if (countdownText != null)
            {
                if (!isAlive)
                {
                    countdownText.color = ComicStyle.Red;
                    int secs = Mathf.CeilToInt(Mathf.Max(0f, remaining));
                    if (secs != _lastDisplayedSecs)
                    {
                        _lastDisplayedSecs = secs;
                        countdownText.text = secs > 0 ? secs.ToString() : string.Empty;
                    }
                }
                else if (_lastDisplayedSecs != -1)
                {
                    _lastDisplayedSecs = -1;
                }
            }

            // 大招就绪：几何风不再用旋转金环，改为右上角黄色方块硬切显示
            if (specialReadyRing != null) specialReadyRing.Intensity = 0f;
            if (specialReadyPip == null)
            {
                var pip = transform.Find("SpecialReadyPip");
                if (pip != null) specialReadyPip = pip.GetComponent<UIInkShapeGraphic>();
            }
            if (specialReadyPip != null && specialReadyPip.gameObject.activeSelf != isSpecialReady)
            {
                specialReadyPip.gameObject.SetActive(isSpecialReady);
            }

            // 「YOU」角标显示
            if (youBadge != null)
            {
                youBadge.SetActive(isLocalPlayer);
            }
        }

        private void OnRespawned()
        {
            // 几何大胆风：纯色硬切，去掉描边弹跳与果冻弹簧
            if (springMotion != null)
            {
                springMotion.SnapHome();
            }
        }

        private void OnKilled()
        {
            // 几何大胆风：纯色硬切，去掉描边弹跳
            if (springMotion != null)
            {
                springMotion.SnapHome();
            }
        }

        private void OnSpecialReadyTriggered()
        {
            // 几何大胆风：纯色硬切，去掉描边弹跳
            if (springMotion != null)
            {
                springMotion.SnapHome();
            }
        }

        public void RefreshVisualsImmediate()
        {
            ResolveFallbacks();
            if (_boundCombatant != null)
            {
                UpdateVisuals(
                    _boundCombatant.IsAlive,
                    _boundCombatant.RespawnRemaining,
                    4.0f,
                    _boundCombatant.IsAlive && _boundWeapon != null && _boundWeapon.IsSpecialReady);
            }
            else
            {
                UIThemeSO resolvedTheme = UIThemeSO.Resolve(theme);
                if (backgroundBlob != null) backgroundBlob.color = resolvedTheme.GetTeamColor(team);
                if (squidIcon != null)
                {
                    if (squidIcon.sprite != null)
                    {
                        squidIcon.enabled = true;
                        squidIcon.color = Color.white;
                        if (fallbackSquid != null) fallbackSquid.SetActive(false);
                    }
                    else
                    {
                        squidIcon.enabled = false;
                        if (fallbackSquid != null) fallbackSquid.SetActive(true);
                    }
                }
                if (deathIcon != null) deathIcon.gameObject.SetActive(false);
                if (fallbackDeath != null) fallbackDeath.SetActive(false);
                if (progressRing != null) progressRing.gameObject.SetActive(false);
                if (countdownBadge != null) countdownBadge.gameObject.SetActive(false);
                if (specialReadyRing != null) specialReadyRing.Intensity = 0f;
                if (specialReadyPip != null) specialReadyPip.gameObject.SetActive(false);
                if (youBadge != null) youBadge.SetActive(isLocalPlayer);
            }
        }
    }
}
