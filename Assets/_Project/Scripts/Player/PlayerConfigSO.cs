using UnityEngine;

namespace P1.Player
{
    [CreateAssetMenu(fileName = "PlayerConfig", menuName = "P1/Player Config")]
    public class PlayerConfigSO : ScriptableObject
    {
        [Header("移动速度 (m/s)")]
        [Tooltip("人形态跑速（对齐 Inkwave PLAYER.runSpeed = 6.0）")]
        public float normalMoveSpeed = 6.0f;
        public float firingMoveSpeed = 3.8f;
        [Tooltip("踩敌方墨水的挣扎移速（对齐 Inkwave PLAYER.enemyInkSpeed = 1.9）")]
        public float enemyInkMoveSpeed = 1.9f;
        [Tooltip("己方墨水中潜行移速（对齐 Inkwave PLAYER.swimSpeed = 11.8）")]
        public float swimMoveSpeed = 11.8f;
        public float rotationLerpSpeed = 22f;

        [Header("跳跃与重力物理")]
        [Tooltip("跳跃高度 (m)。1.35m 可顺畅跃上 1.2m 中央高台或翻越掩体")]
        public float jumpHeight = 1.35f;
        [Tooltip("下落重力加速度 (m/s²)。24m/s² 带来干脆利落的下落手感，杜绝漂浮感")]
        public float gravity = 24.0f;
        [Tooltip("空中水平位移控制比例 (0~1)")]
        public float airControl = 0.95f;

        [Header("墙面潜行攀爬")]
        [Tooltip("贴着己方墨墙潜行上爬的速度 (m/s)")]
        public float wallClimbSpeed = 4.5f;
        [Tooltip("攀爬时持续贴向墙面的推力 (m/s)，保证不脱离墙面")]
        public float wallStickSpeed = 1.3f;

        [Header("墨水槽与回复")]
        public float maxInk = 100f;
        [Tooltip("潜墨回墨速率 (点/秒)。42 点/秒 = 空罐约 2.4 秒回满（对齐 Inkwave inkRefillSwim）")]
        public float swimInkRefillRate = 42f;
        public float passiveInkRefillRate = 4f;
        public float inkRefillDelayAfterShot = 0.25f;

        [Header("生命值与敌墨伤害")]
        public float maxHealth = 100f;
        [Tooltip("潜墨回血速率 (点/秒)（对齐 Inkwave regenRateSwim = 60）")]
        public float swimHealthRegenRate = 60f;
        [Tooltip("踩敌方墨水的持续掉血速率 (点/秒)（对齐 Inkwave enemyInkDps = 20）")]
        public float enemyInkDamagePerSec = 20f;
        [Tooltip("敌墨 DOT 保护底线：单靠踩墨绝不掉到该血量以下、绝不致死（对齐 Inkwave hp - enemyInkDamageCap = 60）")]
        public float enemyInkMinHealthFloor = 60f;
        [Tooltip("离开敌墨后「受击槽」（踩墨惩罚强度 0~1）每秒恢复量。2.0 = 约 0.5 秒完全摆脱敌墨惩罚状态")]
        public float enemyInkRecoveryRatePerSec = 2.0f;

        [Header("潜墨隐匿 (Stealth)")]
        [Tooltip("潜行时水平速度低于该值 (m/s) 即进入隐匿：对敌方完全隐形，己方半透明")]
        public float stealthSpeedThreshold = 1.5f;
        [Tooltip("隐匿时己方/自身视角的角色透明度 (0~1)")]
        public float stealthAllyAlpha = 0.35f;
        [Tooltip("高速潜墨产生水纹涟漪的最低速度 (m/s)")]
        public float swimRippleMinSpeed = 6.5f;
        [Tooltip("高速潜墨涟漪触发间隔 (秒)")]
        public float swimRippleInterval = 0.16f;

        [Header("出水过渡与开火缓冲 (Fire Buffer)")]
        [Tooltip("潜墨钻出人形后的快速过渡时长 (秒)，对齐 Inkwave emergeDelay = 0.07")]
        public float emergeTransitionTime = 0.07f;
        [Tooltip("过渡期内开火按键的缓冲时长 (秒)，对齐 Inkwave fireBuffer = 0.16；缓冲期内绝不吞键")]
        public float fireBufferTime = 0.16f;

        [Header("死亡与复活")]
        public float respawnDelay = 4.0f;
        public float deathExplosionSplatRadius = 1.8f;

        [Header("击杀打击反馈 / Game Feel (P6-A)")]
        [Tooltip("击杀敌方瞬间的时间冻结真实时长（秒）。0.05s ≈ 3 帧，既有入肉感又不阻断操作")]
        public float hitstopHoldSeconds = 0.05f;
        [Tooltip("冻结期间的时间尺度。0.2 = 保留 20% 速度，仍能看到命中定格的过程")]
        public float hitstopTimeScale = 0.2f;
        [Tooltip("定格结束后用临界阻尼弹簧恢复 1.0 的时长（秒）")]
        public float hitstopRecoverSeconds = 0.1f;
        [Tooltip("连击判定的时间窗口（秒）：窗口内再次命中敌人则连击数 +1，颜色向队伍色加温、音高微升")]
        public float hitComboWindowSeconds = 0.55f;
        [Tooltip("自身受击时的最低 Camera Trauma（对齐 Inkwave 0.3~1.0 区间）")]
        public float localHitTraumaMin = 0.3f;
        [Tooltip("自身受击时的最高 Camera Trauma（满额 100 伤害时取该值）")]
        public float localHitTraumaMax = 1.0f;
    }
}
