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
        [Tooltip("无墨/干地乌贼形态移速（对齐 Inkwave PLAYER.squidDrySpeed = 2.9）：没涂墨的地面也能按住潜行键保持乌贼形态，但没有潜墨加成（不加速、不隐匿）")]
        public float squidDryMoveSpeed = 2.9f;
        [Tooltip("转身速率（Slerp 系数，越大越跟手）：人形移动朝向 / 开火朝向 / 乌贼移动朝向共用")]
        public float rotationLerpSpeed = 22f;
        [Tooltip("开火/投弹结束后继续面向瞄准方向的保持时长（秒）。对齐 INKWAVE actor.js fireFacing 0.3~0.5：\n" +
                 "按住扳机期间身体始终朝准星，松手后保持该朝向再切回移动朝向，避免一停火就甩头")]
        public float aimFacingHoldSeconds = 0.4f;

        [Header("跳跃与重力物理")]
        [Tooltip("跳跃高度 (m)。1.35m 可顺畅跃上 1.2m 中央高台或翻越掩体")]
        public float jumpHeight = 1.35f;
        [Tooltip("下落重力加速度 (m/s²)。24m/s² 带来干脆利落的下落手感，杜绝漂浮感")]
        public float gravity = 24.0f;
        [Tooltip("空中水平位移控制比例 (0~1)")]
        public float airControl = 0.95f;

        [Header("移动惯性 (P29 S 曲线加减速，参考 Inkwave runAccel 70 / runDecel 58)")]
        [Tooltip("地面起步加速度 (m/s²)：0→6m/s 约 0.1s，既跟手又有重量")]
        public float runAccel = 60f;
        [Tooltip("地面刹车减速度 (m/s²)：松手后约 0.12s / 0.36m 滑停，不再一帧硬停")]
        public float runDecel = 48f;
        [Tooltip("反向输入急刹减速度 (m/s²)：反向时先踩地制动再反向（Inkwave reverseDecel 78）")]
        public float reverseDecel = 70f;
        [Tooltip("空中加速 (m/s²)：空中保留方向控制但不瞬变")]
        public float airAccel = 20f;
        [Tooltip("空中阻力/刹车 (m/s²)，对齐 Inkwave airDecel = 4")]
        public float airDecel = 6f;
        [Tooltip("速度方向转向速率 (度/秒)：转弯画弧不掉速（Inkwave turnRate 15 rad/s ≈ 860°/s）")]
        public float turnRateDegPerSec = 860f;

        [Header("跳跃宽容与重力曲线 (P29，对齐 Inkwave jumpBuffer/coyote/fallGravity)")]
        [Tooltip("跳跃缓冲时长 (秒)：落地前按下跳跃不丢键，落地立即起跳（Inkwave 0.13）")]
        public float jumpBufferTime = 0.12f;
        [Tooltip("土狼时间 (秒)：走出边缘后仍可起跳（Inkwave 0.12）")]
        public float coyoteTime = 0.12f;
        [Tooltip("下落重力倍率：下落比上升更沉，落地更干脆（Inkwave fallGravityMul 1.2）")]
        public float fallGravityMultiplier = 1.2f;
        [Tooltip("顶点重力倍率：顶点附近轻微滞空，给瞄准/观察时间（Inkwave apexGravityMul 0.82）")]
        public float apexGravityMultiplier = 0.85f;
        [Tooltip("顶点判定速度带 (m/s)：|vy| 小于该值视为顶点附近")]
        public float apexVelocityBand = 1.6f;

        [Header("贴地吸附 (P29)")]
        [Tooltip("接地时向下吸附速度 (m/s)：旧 -2.5 在下台阶/12.8° 坡时 40% 时间腾空；-6 牢牢贴地")]
        public float groundSnapSpeed = 6f;
        [Tooltip("CharacterController.skinWidth（运行时写入）：控制器静止时会以该值悬离地面，0.006 让脚底保持毫米级接触")]
        [Range(0.004f, 0.04f)]
        public float controllerSkinWidth = 0.006f;

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

        [Header("超速惯性滑行与急转弯甩墨 (Swim-Exit Glide)")]
        [Tooltip("超速惯性衰减 (m/s²)：出水/出敌墨瞬间保留滑行动量，按该减速度自然衰减（对齐 Inkwave swimDecel = 42，约 1.5m 滑行）")]
        public float swimExitGlideDecel = 42f;
        [Tooltip("高速潜墨急转弯甩墨的最小游速 (m/s)")]
        public float swimCarveMinSpeed = 4.5f;
        [Tooltip("高速潜墨急转弯甩墨的最小角速度 (rad/s，对齐 Inkwave fxHooks carve 阈值 3.2)")]
        public float swimCarveTurnRate = 3.2f;
        [Tooltip("急转弯甩墨冷却 (秒)")]
        public float swimCarveCooldown = 0.22f;

        [Header("出水过渡与开火缓冲 (Fire Buffer)")]
        [Tooltip("潜墨钻出人形后的快速过渡时长 (秒)，对齐 Inkwave emergeDelay = 0.07")]
        public float emergeTransitionTime = 0.07f;
        [Tooltip("过渡期内开火按键的缓冲时长 (秒)，对齐 Inkwave fireBuffer = 0.16；缓冲期内绝不吞键")]
        public float fireBufferTime = 0.16f;

        [Header("死亡与复活")]
        public float respawnDelay = 4.0f;
        [Tooltip("复活后的出生保护时长（秒）。期间免疫伤害与敌方墨水灼烧；开局站位与重开比赛不享受，0 = 关闭")]
        public float respawnInvulnerabilitySeconds = 3.0f;
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

        [Header("击杀反馈 P6-B（对齐 Inkwave 打击感）")]
        [Tooltip("本地玩家击杀敌方时的额外 Camera Trauma（受无障碍震屏滑杆门控）")]
        public float killTrauma = 0.35f;
        [Tooltip("本地玩家击杀敌方时的 FOV 微踢角度（读作「命中确认」的镜头冲击，0 = 关闭）")]
        public float killFovPunchDegrees = 2.2f;
        [Tooltip("击杀屏幕边缘闪光的基础时长（秒），连杀时强度逐层加码")]
        public float killFlashSeconds = 0.30f;
        [Tooltip("连杀判定窗口（秒）：窗口内再次击杀则连杀数 +1，边缘闪光与击杀琶音逐层加码")]
        public float killStreakWindowSeconds = 4.0f;
        [Tooltip("命中墨花的伤害基准：该伤害值时墨花达到最大规模（对齐 Inkwave amount/60）")]
        public float hitSplashDamageRef = 60f;
        [Tooltip("受击闪白时长（秒）")]
        public float hitFlashSeconds = 0.09f;

        [Header("瞄准姿态 (Aim Pose)")]
        [Tooltip("武器模型承担相机俯仰的权重：1 = 完全跟随准星，枪口指向瞄准点")]
        public float aimPoseWeaponPitchWeight = 1f;
        [Tooltip("胸腔分担相机俯仰的权重：0.18 = 只轻微前倾，身体保持直立（对齐 Inkwave）")]
        public float aimPoseChestPitchWeight = 0.18f;
        [Tooltip("瞄准姿态平滑时间（秒）：太小会跟手但抖，太大会有延迟感")]
        public float aimPoseSmoothTime = 0.07f;
    }
}
