using UnityEngine;

namespace P1.AI
{
    [CreateAssetMenu(fileName = "BotConfig", menuName = "P1/Bot Config")]
    public class BotConfigSO : ScriptableObject
    {
        [Header("感知与交战")]
        public float sightRange = 8.5f;
        [Tooltip("最终交火距离的兜底值（武器配置缺失时使用）；正常由下方武器适配参数按射程推导")]
        public float keepDistance = 4.2f;
        [Tooltip("开火角度门槛的基准值（度）：实际门槛取它与目标屏幕张角的较大者")]
        public float aimToleranceDegrees = 9f;
        public float reactionTime = 0.22f;
        [Tooltip("瞄准慢漂移的基准幅度（度）：越小对枪越准，1.2° 接近熟练玩家")]
        public float aimWanderDegrees = 1.2f;
        [Tooltip("拉枪误差基准幅度（度）：乘以下方 aimAcquireOvershoot 得到发现目标瞬间的过冲量")]
        public float aimErrorDegrees = 5.5f;

        [Header("涂地行为")]
        public float paintAimRange = 6.0f;
        public float paintSweepDegrees = 26f;
        public float paintSweepSpeed = 1.6f;
        public float repathInterval = 0.55f;
        public float arriveDistance = 1.3f;
        [Tooltip("多久重新挑一次涂地目标（秒）；到达目标会立即重挑")]
        public float paintGoalRefresh = 7f;
        [Tooltip("每次挑涂地目标时随机采样的候选点数量")]
        public int paintGoalSamples = 20;
        [Tooltip("巡涂时每隔多久检查一次前方是否已经涂过（秒）")]
        public float paintAheadCheckInterval = 0.3f;
        [Tooltip("移动朝向的最大转速（度/秒）：路点切换/横向换向时不再瞬时抽搐")]
        public float moveHeadingTurnRateDegrees = 630f;

        [Header("卡死检测")]
        public float stuckCheckInterval = 1.0f;
        public float stuckMoveThreshold = 0.35f;

        [Header("墨水与技能")]
        public float refillInkThreshold = 18f;
        public float refillExitInk = 75f;
        public float bombMinInk = 60f;
        public float bombRangeMin = 3.2f;
        public float bombRangeMax = 7.0f;
        public float bombCooldown = 5.0f;
        [Range(0f, 1f)] public float bombUseChance = 0.55f;
        public int specialMinEnemies = 1;
        public float specialSearchRange = 5.5f;

        [Header("拟人化瞄准（二阶临界阻尼弹簧）")]
        [Tooltip("交战时的弹簧固有频率 ω（rad/s）：越大跟枪越紧，13 ≈ 熟练玩家")]
        public float aimSpringOmega = 13f;
        [Tooltip("交战时最大瞄准转速（度/秒）：限制甩枪速度，消掉瞬间锁头")]
        public float aimMaxTurnRateDegrees = 573f;
        [Tooltip("非交战（巡涂/转场）时的弹簧频率，更低更顺滑")]
        public float aimIdleSpringOmega = 8f;
        [Tooltip("非交战时最大瞄准转速（度/秒）")]
        public float aimIdleTurnRateDegrees = 344f;
        [Tooltip("发现新目标瞬间的初始偏角倍率（×aimErrorDegrees），模拟拉枪过冲后修正")]
        public float aimAcquireOvershoot = 2.4f;
        [Tooltip("瞄准误差缓慢漂移的频率（Hz 级）")]
        public float aimWanderSpeed = 1.7f;
        [Tooltip("已经开火时的角度门槛放大倍率：避免一帧抖动就断火")]
        public float aimToleranceGrowingScale = 1.6f;

        [Header("武器行为适配")]
        [Tooltip("滚筒贴脸距离：推进到该距离内持续推地碾压")]
        public float rollerEngageDistance = 0.9f;
        [Tooltip("滚筒开始按住扳机的距离：超出则先跑位")]
        public float rollerFireRange = 5.5f;
        [Tooltip("小枪理想交火距离 = maxRange × 该比例")]
        [Range(0.3f, 1f)] public float shooterEngageFraction = 0.7f;
        [Tooltip("蓄力狙理想交火距离 = chargeRangeMax × 该比例")]
        [Range(0.3f, 1f)] public float chargerEngageFraction = 0.8f;
        [Tooltip("蓄力狙交战时的松手蓄力阈值（0~1）：蓄满即松手")]
        [Range(0.5f, 1f)] public float chargerReleaseCharge = 0.95f;
        [Tooltip("蓄力狙巡涂时蓄到多少就松手画墨线")]
        [Range(0.3f, 1f)] public float chargerPaintReleaseCharge = 0.7f;
        [Tooltip("蓄力狙松手画线后的短暂停顿（秒）")]
        public float chargerPaintPause = 0.35f;
        [Tooltip("加特林交战时的松手蓄力阈值（0~1）：蓄满即松手释放弹幕")]
        [Range(0.5f, 1f)] public float splatlingReleaseCharge = 0.9f;
        [Tooltip("加特林巡涂时蓄到多少就松手喷一条墨带")]
        [Range(0.3f, 1f)] public float splatlingPaintReleaseCharge = 0.6f;
        [Tooltip("索敌半径在武器射程外额外留的余量（米）")]
        public float weaponSightMargin = 2f;

        [Header("墨水加权寻路")]
        [Tooltip("己方墨水格子的寻路代价倍率：<1 表示鼓励沿墨道高速切入")]
        public float ownInkPathCost = 0.5f;
        [Tooltip("中性地面格子的寻路代价倍率")]
        public float neutralPathCost = 1.0f;
        [Tooltip("敌方墨水格子的寻路代价倍率：>1 表示主动绕开墨陷阱")]
        public float enemyInkPathCost = 3.0f;

        [Header("残血战术撤退")]
        [Tooltip("血量低于该值时主动脱离交火")]
        public float retreatHealthThreshold = 40f;
        [Tooltip("回血到该值才结束撤退、重新接战")]
        public float retreatExitHealth = 75f;
        [Tooltip("只有威胁（最近敌人）在該半径内才会触发撤退")]
        public float retreatTriggerRange = 14f;
        public float retreatDurationMin = 2.2f;
        public float retreatDurationMax = 3.6f;
        [Tooltip("搜索后方己方墨水的半径（米）")]
        public float retreatSearchRadius = 9f;
        public float retreatRepathInterval = 0.9f;
        [Tooltip("撤退结束后多久内不再触发新的撤退（秒）：避免在 40 HP 附近反复横跳")]
        public float retreatReentryCooldown = 1.5f;
        [Tooltip("潜行敌人被发现的贴身距离：敌方在己方墨水中潜行时，超过该距离不可见")]
        public float stealthDetectRange = 2.5f;
    }
}
