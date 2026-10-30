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

            // 背景底色与形状：存活队伍色，阵亡态红底
            if (backgroundBlob != null)
            {
                backgroundBlob.color = isAlive ? teamColor : ComicStyle.Red;
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

            // 阵亡 X 标（黑 X）与兜底
            if (deathIcon != null)
            {
                deathIcon.gameObject.SetActive(!isAlive);
                if (!isAlive)
                {
                    if (deathIcon.sprite != null)
                    {
                        deathIcon.enabled = true;
                        deathIcon.color = ComicStyle.Ink;
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

            // 阵亡进度环与角标（5px 加粗环）
            if (progressRing != null)
            {
                if (!isAlive && remaining > 0f)
                {
                    progressRing.gameObject.SetActive(true);
                    float fill = Mathf.Clamp01(1f - (remaining / Mathf.Max(0.1f, duration)));
                    progressRing.Progress = fill;
                    progressRing.ProgressColor = ComicStyle.Yellow;
                }
                else
                {
                    progressRing.gameObject.SetActive(false);
                    progressRing.Progress = 0f;
                }
            }

            // 倒计时文字角标
            if (countdownBadge != null)
            {
                countdownBadge.gameObject.SetActive(!isAlive && remaining > 0f);
            }

            if (countdownText != null)
            {
                if (!isAlive)
                {
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

            // 大招金光环
            if (specialReadyRing != null)
            {
                float targetIntensity = isSpecialReady ? 1f : 0f;
                specialReadyRing.Intensity = Mathf.MoveTowards(specialReadyRing.Intensity, targetIntensity, Time.unscaledDeltaTime * 6f);
                if (isSpecialReady)
                {
                    specialReadyRing.color = resolvedTheme != null ? resolvedTheme.readyGold : GoldRingColor;
                }
            }

            // 「YOU」角标显示
            if (youBadge != null)
            {
                youBadge.SetActive(isLocalPlayer);
            }
        }

        private void OnRespawned()
        {
            if (springMotion != null)
            {
                springMotion.PlayIn(0f);
            }
        }

        private void OnKilled()
        {
            if (springMotion != null)
            {
                springMotion.Punch(0.08f);
            }
        }

        private void OnSpecialReadyTriggered()
        {
            if (springMotion != null)
            {
                springMotion.Punch(0.08f);
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
                if (youBadge != null) youBadge.SetActive(isLocalPlayer);
            }
        }
    }
}
