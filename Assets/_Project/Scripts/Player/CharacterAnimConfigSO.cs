using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 角色程序化动画调参入口（人⇄乌贼形态手势、游动姿态、触须行波、表演性点缀）。
    /// 默认值全部来自 INKWAVE 逆向源码（inkwave-aah-research/src/game/character.js L383-386/1342-1358/3550-3611），
    /// 未绑定该资产时 CharacterVisualController 会在运行时回退到等值默认实例。
    /// </summary>
    [CreateAssetMenu(fileName = "CharacterAnimConfig", menuName = "P1/Character Anim Config")]
    public class CharacterAnimConfigSO : ScriptableObject
    {
        [Header("形态手势 (Form Gesture)")]
        [Tooltip("驱动形态标量 f 的阻尼弹簧固有频率 (rad/s)。ω=18 ζ=0.35 时约 0.4s 过冲回稳，对齐 Inkwave 0.42s 手势")]
        public float formSpringOmega = 18f;
        [Tooltip("阻尼比：<1 过冲（浮出拉伸/潜入回稳摆动），≥1 临界无过冲")]
        [Range(0.05f, 2f)]
        public float formSpringZeta = 0.35f;
        [Tooltip("f 下限（负值区 = 浮出时的拉伸过冲区）")]
        public float formAmountMin = -0.35f;
        [Tooltip("f 上限（>1 区 = 潜入到位后的回稳摆动区）")]
        public float formAmountMax = 1.35f;
        [Tooltip("f 超过该值后人形态根节点隐藏（kid 已压扁进墨面）")]
        public float kidHideFormAmount = 0.98f;
        [Tooltip("f 低于该值后乌贼形态根节点显示（从墨里钻出）")]
        public float squidShowFormAmount = 0.02f;
        [Tooltip("潜入时 f 达到该值立即隐藏武器")]
        public float weaponHideFormAmount = 0.35f;
        [Tooltip("浮出时 f 回落到该值才显示武器（与上一项形成迟滞，防边界闪烁）")]
        public float weaponShowFormAmount = 0.30f;

        [Header("形态手势曲线（横轴 f：-0.35 浮出过冲区 / 0 人形 / 1 乌贼 / >1 潜入回稳区）")]
        [Tooltip("人形态纵向缩放：潜入压扁 1→0.85→0.34→0.16，浮出过冲拉伸到 ~1.3")]
        public AnimationCurve kidScaleY = new AnimationCurve(
            new Keyframe(-0.25f, 1.30f), new Keyframe(-0.12f, 0.97f), new Keyframe(0f, 1f),
            new Keyframe(0.12f, 0.85f), new Keyframe(0.45f, 0.34f), new Keyframe(0.75f, 0.16f),
            new Keyframe(1f, 0.15f), new Keyframe(1.2f, 0.14f));

        [Tooltip("人形态横向缩放：压扁时膨胀到 ~1.4，拉伸时收窄到 ~0.72")]
        public AnimationCurve kidScaleXZ = new AnimationCurve(
            new Keyframe(-0.25f, 0.72f), new Keyframe(-0.12f, 1.02f), new Keyframe(0f, 1f),
            new Keyframe(0.12f, 1.08f), new Keyframe(0.45f, 1.40f), new Keyframe(0.75f, 1.22f),
            new Keyframe(1f, 1.20f), new Keyframe(1.2f, 1.20f));

        [Tooltip("乌贼纵向缩放：从墨里钻出 0.3→0.72→过冲 1.28→回稳 1.0；>1 区为回稳摆动")]
        public AnimationCurve squidScaleY = new AnimationCurve(
            new Keyframe(0f, 0.30f), new Keyframe(0.15f, 0.72f), new Keyframe(0.30f, 1.28f),
            new Keyframe(0.45f, 0.95f), new Keyframe(0.70f, 1.06f), new Keyframe(1f, 1f),
            new Keyframe(1.12f, 1.05f), new Keyframe(1.25f, 0.97f), new Keyframe(1.35f, 1f));

        [Tooltip("乌贼横向缩放：钻出时略鼓、回稳摆动")]
        public AnimationCurve squidScaleXZ = new AnimationCurve(
            new Keyframe(0f, 0.90f), new Keyframe(0.30f, 1.05f), new Keyframe(0.45f, 0.92f),
            new Keyframe(1f, 1f), new Keyframe(1.12f, 0.96f), new Keyframe(1.25f, 1.02f),
            new Keyframe(1.35f, 1f));

        [Header("游动姿态 (Swim Posture，对齐 Inkwave character.js L3550-3560)")]
        [Tooltip("动画用全速归一化速度 (m/s)：与 PlayerConfig.swimMoveSpeed 一致")]
        public float swimRefSpeed = 11.8f;
        [Tooltip("身体起伏波频率下限 (rad/s)，慢速蠕动")]
        public float swimUndulationFreqSlow = 5f;
        [Tooltip("身体起伏波频率上限 (rad/s)，全速 14 对齐 Inkwave")]
        public float swimUndulationFreqFast = 14f;
        [Tooltip("起伏俯仰幅度（度，全速时 ±3.4°）")]
        public float swimUndulationPitchDeg = 3.4f;
        [Tooltip("转向侧倾系数：roll(rad) = -yawRate·factor，0.08 为 Inkwave 原值")]
        public float swimBankFactor = 0.08f;
        [Tooltip("转向侧倾上限（度）。Inkwave ±0.5rad≈28.6°，这里取保守 ±20°")]
        public float swimBankMaxDeg = 20f;
        [Tooltip("头部点摆幅度（度，Inkwave 0.1rad≈5.7°）")]
        public float swimSwayDeg = 5f;
        [Tooltip("头部点摆频率下限 (rad/s)")]
        public float swimSwayFreqSlow = 4f;
        [Tooltip("头部点摆频率上限 (rad/s)")]
        public float swimSwayFreqFast = 11f;
        [Tooltip("全速纵向拉伸上限（保体积：横向自动 1/√）。Inkwave 0.2")]
        [Range(0f, 0.5f)]
        public float swimStretchMax = 0.20f;

        [Header("触须行波 (Tentacle Wave)")]
        [Tooltip("摆频下限 (rad/s)，近静止时 12 对齐 Inkwave wigFreq 12+8·sv")]
        public float tentacleFreqSlow = 12f;
        [Tooltip("摆频上限 (rad/s)，全速 20")]
        public float tentacleFreqFast = 20f;
        [Tooltip("近静止摆幅（度）：隐匿时轻微飘动")]
        public float tentacleAmpIdleDeg = 3f;
        [Tooltip("全速摆幅（度）：Inkwave 读数上限 ~0.04 折算骨骼拍水 ±14°")]
        public float tentacleAmpFastDeg = 14f;
        [Tooltip("相邻触须相位差（rad，模拟根→尖行波传播）")]
        public float tentaclePhaseStep = 0.9f;
        [Tooltip("侧向飘摆系数（相对拍水摆幅）")]
        [Range(0f, 1f)]
        public float tentacleSideSwayFactor = 0.5f;

        [Header("贴地下沉与表演点缀")]
        [Tooltip("乌贼展开时身体沉入墨面的深度 (米)，半潜观感")]
        public float squidSubmergeDepth = 0.10f;
        [Tooltip("浮出甩臂幅度（度）：双臂弹开亮相")]
        public float emergeArmFlingDeg = 35f;
        [Tooltip("浮出甩臂时长（秒）")]
        public float emergeArmFlingDuration = 0.28f;
        [Tooltip("浮出果冻拉伸冲量（起跳拉伸同款弹簧）")]
        public float emergeJellyKick = 2.2f;
        [Tooltip("潜入甩臂幅度（度）：自由臂高举如跳水")]
        public float diveArmFlingDeg = 55f;
        [Tooltip("潜入甩臂时长（秒）")]
        public float diveArmFlingDuration = 0.22f;
        [Tooltip("潜入果冻压扁冲量（ anticipation 下蹲）")]
        public float diveJellyKick = -1.5f;
        [Tooltip("形态切换时墨焰/发辫的惯性甩动幅度（度）")]
        public float formHairKickDeg = 14f;

        [Header("人形步态 (Kid Gait，P33 真骨骼：左右腿独立相位 + 骨盆贴地 + 脊柱分配，默认值与 KidGaitSolver 一致)")]
        [Tooltip("步频下限（触地次数/秒）")]
        public float kidCadenceMin = 1.6f;
        [Tooltip("步频上限（触地次数/秒）：长腿真实跑姿步频")]
        public float kidCadenceMax = 9.0f;
        [Tooltip("骨盆额外起伏幅度（米）：慢走 / 快跑")]
        public float kidBobSlow = 0.006f;
        public float kidBobFast = 0.012f;
        [Tooltip("左右摇滚（度）：慢走 / 快跑")]
        public float kidRockSlowDeg = 2.0f;
        public float kidRockFastDeg = 4.5f;
        [Tooltip("触地挤压深度（竖向缩放比例）")]
        public float kidSquashDepth = 0.030f;
        [Tooltip("触地横向膨胀比例")]
        public float kidSquashBulge = 0.018f;
        [Tooltip("摆臂幅度（度）：慢走 / 快跑")]
        public float kidArmSwingSlowDeg = 12f;
        public float kidArmSwingFastDeg = 38f;
        [Tooltip("持枪时摆臂/摆肘幅度缩放：双手/单手都握着武器，手臂大幅前后甩会让手滑出握把")]
        [Range(0f, 1f)]
        public float kidArmedArmSwingScale = 0.22f;
        [Tooltip("大腿摆幅（度）：慢走 / 快跑")]
        public float kidLegSwingSlowDeg = 14f;
        public float kidLegSwingFastDeg = 42f;
        [Tooltip("摆动相膝弯峰值（度）：慢走 / 快跑")]
        public float kidKneeBendSlowDeg = 22f;
        public float kidKneeBendFastDeg = 65f;
        [Tooltip("摆动相脚尖上翘峰值（度）：脚离地时脚尖抬起、落地前回落")]
        public float kidToeUpDeg = 10f;
        [Tooltip("支撑末段蹬地压脚尖峰值（度）：蹬地瞬间脚尖下压")]
        public float kidToeOffDeg = 14f;
        [Tooltip("髋枢轴到踝的腿长（米）：CharacterMetrics.ThighLength + ShinLength")]
        public float kidLegLength = 0.433f;
        [Tooltip("骨盆贴地下沉封顶（米）")]
        public float kidHipDropMaxMeters = 0.035f;

        [Header("脊柱链与前倾分配 (P33：对齐 INKWAVE runPose 25/45/30 分配)")]
        [Tooltip("慢走前倾（度）")]
        public float kidLeanSlowDeg = 4f;
        [Tooltip("快跑前倾（度）")]
        public float kidLeanFastDeg = 13f;
        [Tooltip("骨盆分担前倾比例")]
        public float kidHipLeanShare = 0.25f;
        [Tooltip("腰胸分担前倾比例")]
        public float kidSpineLeanShare = 0.45f;
        [Tooltip("胸腔分担前倾比例")]
        public float kidChestLeanShare = 0.30f;
        [Tooltip("动态层（加速度+瞄准+落地）腰胸分配比例")]
        public float kidSpineDynamicShare = 0.45f;
        [Tooltip("动态层胸腔分配比例")]
        public float kidChestDynamicShare = 0.35f;
        [Tooltip("头部反向稳定俯仰补偿比例：奔跑/瞄准/冲刺时脸保持朝向准星")]
        [Range(0f, 1f)]
        public float headStabilizePitchShare = 0.70f;

        [Header("二段臂屈肘 (P33)")]
        [Tooltip("基础屈肘（度）：站立持枪轻弯")]
        public float kidElbowBendBaseDeg = 22f;
        [Tooltip("快跑屈肘（度）：跑动摆臂收拢")]
        public float kidElbowBendFastDeg = 62f;
        [Tooltip("前摆臂额外屈肘峰值（度）")]
        public float kidElbowSwingGainDeg = 16f;

        [Header("触须发弹簧链 (P33：6 束 × 根/梢双枢轴弹簧)")]
        [Tooltip("根段弹簧固有频率 (rad/s)")]
        public float hairRootOmega = 16f;
        [Tooltip("根段弹簧阻尼比")]
        public float hairRootZeta = 0.38f;
        [Tooltip("梢段弹簧固有频率 (rad/s)")]
        public float hairMidOmega = 12f;
        [Tooltip("梢段弹簧阻尼比")]
        public float hairMidZeta = 0.30f;
        [Tooltip("梢段鞭梢放大倍率")]
        public float hairMidWhip = 1.35f;
        [Tooltip("加速度对头发拉力增益")]
        public float hairAccelGain = 0.40f;
        [Tooltip("待机错相行波幅度（度）")]
        public float hairIdleWaveDeg = 3.5f;

        [Header("横移与骨盆解耦")]
        [Tooltip("横移半侧转上限（度）：身体朝移动方向偏转，让横移时腿摆方向与位移一致")]
        public float strafeYawMaxDeg = 28f;
        [Tooltip("横移时前后摆腿/摆臂幅度保留比例")]
        [Range(0.2f, 1f)]
        public float strafeSwingFalloff = 0.55f;
        [Tooltip("后退时摆腿幅度保留比例")]
        [Range(0.1f, 1f)]
        public float backwardSwingScale = 0.5f;
        [Tooltip("半侧转平滑时间（秒）")]
        public float strafeYawSmoothTime = 0.09f;
        [Tooltip("Hips 承担骨盆贴地下沉的比例")]
        [Range(0f, 1f)]
        public float hipsBobShare = 1.0f;
        [Tooltip("Hips 反向滚转抵消胸口侧倾的比例")]
        [Range(0f, 1f)]
        public float hipsCounterRollShare = 1.0f;
        [Tooltip("Hips 反向俯仰抵消胸口前倾/后仰的比例")]
        [Range(0f, 1f)]
        public float hipsCounterPitchShare = 1.0f;
        [Tooltip("骨盆竖向起伏低通时间（秒，≥45FPS 档）：贴地下沉在 4.5m/s 以上是 6 步/秒方波，直接写回会让整个上半身跟着抖")]
        [Range(0.01f, 0.3f)]
        public float hipsBobSmoothTime = 0.07f;
        [Tooltip("骨盆竖向起伏低通时间（秒，≤18FPS 档）：掉帧时把起伏压成缓波，防止低帧率采样混叠成「头一直上下跳」")]
        [Range(0.05f, 0.6f)]
        public float hipsBobSmoothTimeLowFps = 0.28f;

        // ---- 历史兼容别名属性（便于旧单测平滑迁移）----
        public float gaitCadenceMax { get => kidCadenceMax; set => kidCadenceMax = value; }
        public float gaitLegSwingFastDeg { get => kidLegSwingFastDeg; set => kidLegSwingFastDeg = value; }
        public float gaitKneeBendFastDeg { get => kidKneeBendFastDeg; set => kidKneeBendFastDeg = value; }
        public float gaitHipDropMaxMeters { get => kidHipDropMaxMeters; set => kidHipDropMaxMeters = value; }
        public float gaitBobSlow => kidBobSlow;
        public float gaitSquashDepth => kidSquashDepth;
        public float gaitElbowBendBaseDeg => kidElbowBendBaseDeg;
        public float gaitElbowBendFastDeg => kidElbowBendFastDeg;
        public float gaitElbowSwingGainDeg => kidElbowSwingGainDeg;
        public float faceStabilizePitchShare => headStabilizePitchShare;

        [Header("接触阴影 (Contact Shadow，脚下贴地暗斑)")]
        [Tooltip("是否生成程序化接触阴影：平行光 52° 仰角下投影落在身后 0.8m，脚正下方没有压暗会读作悬浮")]
        public bool contactShadowEnabled = true;
        [Tooltip("接触阴影半径（米）：约等于蛋体宽度的一半，略大一点让脚下暗斑从蛋体边缘露出")]
        public float contactShadowRadius = 0.55f;
        [Tooltip("贴地时阴影最大不透明度：只压暗地面，不抢平行光投影")]
        [Range(0f, 1f)]
        public float contactShadowOpacity = 0.48f;
        [Tooltip("离地多高（米）后阴影完全消失：滞空时阴影留在地面并随高度淡出")]
        public float contactShadowMaxHeight = 1.1f;
        [Tooltip("阴影贴地偏移（米）：沿地面法线抬高，防止与墨面 z-fighting")]
        public float contactShadowGroundOffset = 0.008f;

        [Header("滞空表演 (Air Pose，参考 Inkwave _poseAir 四权重分解)")]
        [Tooltip("起跳瞬间果冻拉伸冲量（旧 3.2，加大强化弹射感）")]
        public float airLaunchStretchImpulse = 4.5f;
        [Tooltip("跳跃顶点悬停的微团身挤压量（竖向缩放减少）")]
        public float airApexSoften = 0.02f;
        [Tooltip("快速下落的拉伸量（竖向缩放增加）")]
        public float airFallStretch = 0.035f;
        [Tooltip("下落后期伸腿够地的后摆角（度，正 = 腿向后下伸直）")]
        public float airReachThighDeg = 18f;
        [Tooltip("跑步起跳前后腿劈开角（度）")]
        public float airRunSplitThighDeg = 26f;
        [Tooltip("跑步起跳判定速度阈值（m/s）：超过才做前后腿劈开")]
        public float airRunSplitMinSpeed = 3f;
        [Tooltip("落地压扁冲量下限（果冻速度冲量）")]
        public float landImpulseMin = 1.2f;
        [Tooltip("落地压扁冲量上限（按下落速度 14m/s 归一）")]
        public float landImpulseMax = 6.5f;
        [Tooltip("落地屈膝保持时长（秒）：压扁后保持片刻再回弹，砸地感")]
        public float landHoldTime = 0.10f;
        [Tooltip("落地屈膝保持角度（度）")]
        public float landHoldKneeDeg = 30f;
        [Tooltip("落地双臂下甩幅度（度，按落地强度缩放）")]
        public float landArmDropDeg = 12f;
        [Tooltip("落地胸口前倾冲量（度，按落地强度缩放）")]
        public float landChestPitchDeg = 6f;
        [Tooltip("重落地触发眨眼的速度阈值（m/s）")]
        public float landHardBlinkSpeed = 8f;
        [Tooltip("落地墨花 VFX 的最低落地速度（m/s）：低于该值的贴地抖动不出墨花")]
        public float landVfxMinFallSpeed = 3.5f;
        [Tooltip("落地墨花规模（SpawnInkSplat 半径参数）")]
        public float landVfxSize = 0.45f;

        [Header("大招 Slam 起跳表演 (Tidal Slam Leap，对齐 Inkwave _poseLeap / _poseSlam)")]
        [Tooltip("前空翻总角度（度）：0 = 关闭。360 = 升空期间转满一圈，落地回到正向")]
        public float slamFlipDegrees = 360f;
        [Tooltip("蹬伸段：双腿向后下伸直角（度）")]
        public float slamLaunchThighDeg = 14f;
        [Tooltip("蹬伸段：脚尖下压角（度）")]
        public float slamLaunchFootDeg = 26f;
        [Tooltip("蹬伸段：上身后仰角（度，负 = 后仰）")]
        public float slamLaunchSpineDeg = -10f;
        [Tooltip("团身段：双腿收向胸口角（度）")]
        public float slamTuckThighDeg = 42f;
        [Tooltip("团身段：屈膝角（度）")]
        public float slamTuckKneeDeg = 78f;
        [Tooltip("团身段：脊柱前弯角（度）")]
        public float slamTuckSpineDeg = 30f;
        [Tooltip("顶点段：举枪过顶时胸腔后仰角（度）")]
        public float slamHangChestDeg = -12f;
        [Tooltip("顶点段：双腿分开角（度，左右各一半）")]
        public float slamHangLegSplitDeg = 12f;
        [Tooltip("俯冲段：双腿伸直向下的过伸角（度）")]
        public float slamDiveThighDeg = 8f;
        [Tooltip("落地段：髋部下沉量（米）—— INKWAVE 用 0.25，蛋形角色按腿长缩到 0.18")]
        public float slamImpactHipDropMeters = 0.18f;
        [Tooltip("落地段：脊柱前压角（度）")]
        public float slamImpactSpineDeg = 34f;
        [Tooltip("落地段：屈膝角（度）")]
        public float slamImpactKneeDeg = 62f;
        [Tooltip("落地段：蹲姿保持时长（秒）")]
        public float slamImpactHoldSeconds = 0.18f;
        [Tooltip("落地段：蹲姿回到常态的时长（秒）")]
        public float slamImpactRecoverSeconds = 0.26f;

        [Header("大招 Storm 投掷表演 (Ink Tempest Throw，对齐 Inkwave _poseThrow)")]
        [Tooltip("引臂段：躯干后仰角（度，负 = 后仰蓄势）")]
        public float stormCockSpineDeg = -14f;
        [Tooltip("引臂段：躯干侧倾角（度，向持枪侧压）")]
        public float stormCockRollDeg = 10f;
        [Tooltip("引臂段：武器抬起角（度，负 = 抬过肩）")]
        public float stormCockWeaponPitchDeg = -38f;
        [Tooltip("引臂段：武器抬起位移（米）")]
        public float stormCockWeaponLiftMeters = 0.10f;
        [Tooltip("鞭甩段：躯干前压角（度）")]
        public float stormWhipSpineDeg = 18f;
        [Tooltip("鞭甩段：躯干反向侧倾角（度）")]
        public float stormWhipRollDeg = -12f;
        [Tooltip("鞭甩段：武器前甩角（度，正 = 甩向身前地面）")]
        public float stormWhipWeaponPitchDeg = 30f;
        [Tooltip("鞭甩段：挥臂摆幅加成倍率（叠在步态摆臂上）")]
        public float stormWhipArmSwingBoost = 2.2f;
        [Tooltip("随挥段：躯干前倾角（度）")]
        public float stormFollowSpineDeg = 8f;
        [Tooltip("随挥段：武器收势角（度）")]
        public float stormFollowWeaponPitchDeg = 16f;
        [Tooltip("随挥段：武器下沉位移（米）")]
        public float stormFollowWeaponLiftMeters = -0.06f;

        [Header("惯性倾斜 (Lean Springs，参考 Inkwave character.js L1642-1657)")]
        [Tooltip("加速度量程钳制（m/s²）：起步/刹车帧差分远超此值，钳制后弹簧才有平滑余量")]
        public float leanAccelClamp = 24f;
        [Tooltip("纵向加速度→前倾度数系数（度 / m/s²）：加速前倾、刹车后仰")]
        public float leanPitchGain = 0.35f;
        [Tooltip("纵向惯性倾斜上限（度）")]
        public float leanPitchMaxDeg = 9f;
        [Tooltip("纵向弹簧频率（Hz）")]
        public float leanPitchHz = 2.2f;
        [Tooltip("纵向弹簧阻尼比（<1 有过冲回弹）")]
        public float leanPitchZeta = 0.40f;
        [Tooltip("横向加速度→侧倾度数系数：侧移/急转压弯")]
        public float leanRollGain = 0.30f;
        [Tooltip("横向惯性倾斜上限（度）")]
        public float leanRollMaxDeg = 8f;
        [Tooltip("横向弹簧频率（Hz）")]
        public float leanRollHz = 2.0f;
        [Tooltip("横向弹簧阻尼比")]
        public float leanRollZeta = 0.48f;
        [Tooltip("骨盆惯性滞后最大位移（米）：起步身体落后、刹车冲过头再弹回")]
        public float hipsLagMaxMeters = 0.05f;
        [Tooltip("加速度→滞后位移系数（米 / m/s²）")]
        public float hipsLagGain = 0.010f;
        [Tooltip("滞后弹簧频率（Hz）")]
        public float hipsLagHz = 2.3f;
        [Tooltip("滞后弹簧阻尼比")]
        public float hipsLagZeta = 0.6f;

        [Header("待机生命感 (Idle Life，参考 Inkwave _breathe / 重心转移 / fidget)")]
        [Tooltip("呼吸频率（次/秒），每实例随机 ±12% 防整队同步起伏")]
        public float idleBreathHz = 0.33f;
        [Tooltip("呼吸竖向挤压幅度（缩放比例）")]
        public float idleBreathSquash = 0.015f;
        [Tooltip("呼吸身体起伏（米）")]
        public float idleBreathBob = 0.003f;
        [Tooltip("呼吸胸口俯仰（度）：胸腔随呼吸开合")]
        public float idleBreathPitchDeg = 2f;
        [Tooltip("重心转移间隔下限（秒）")]
        public float idleShiftIntervalMin = 3.2f;
        [Tooltip("重心转移间隔上限（秒）")]
        public float idleShiftIntervalMax = 7.2f;
        [Tooltip("重心转移侧倾（度）")]
        public float idleShiftRockDeg = 1.5f;
        [Tooltip("重心转移横向位移（米）")]
        public float idleShiftSwayMeters = 0.015f;
        [Tooltip("待机微动作间隔下限（秒）")]
        public float idleFidgetIntervalMin = 4f;
        [Tooltip("待机微动作间隔上限（秒）")]
        public float idleFidgetIntervalMax = 9f;

        [Header("受击顿挫 (Hit Flinch，P36：子弹命中的方向性身体反应；白闪/震屏之外的第三层反馈)")]
        [Tooltip("受击后仰最大俯仰角（度，满伤害时）：正面中弹上身向后顿")]
        public float flinchPitchMaxDeg = 9f;
        [Tooltip("受击侧让最大滚转角（度，满伤害时）：侧向中弹身体朝受击方向让")]
        public float flinchRollMaxDeg = 7f;
        [Tooltip("受击弹簧频率（Hz）：越大越脆，过小会读成慢动作后仰")]
        public float flinchSpringHz = 6.5f;
        [Tooltip("受击弹簧阻尼比（<1 带一次回摆过冲）")]
        [Range(0.05f, 1.5f)]
        public float flinchSpringZeta = 0.32f;
        [Tooltip("达到满幅度所需的伤害量（HP）：小子弹小顿挫、重武器大顿挫")]
        public float flinchFullDamage = 45f;
        [Tooltip("受击果冻压扁冲量（负 = 身体向下砸一下，复用起跳/落地弹簧通道）")]
        public float flinchJellyKick = -1.4f;
        [Tooltip("单次伤害超过该值触发一次眨眼（吃痛表情）")]
        public float flinchBlinkDamage = 20f;

        [Header("视线灵动 (Eye Look，P36：眼球在深色眼罩内整体微移；待机游目 + 瞄准注视)")]
        [Tooltip("眼球最大偏移（米）：眼罩横向比眼球宽约 12mm，偏移须留边不穿帮")]
        public float eyeLookMaxOffset = 0.004f;
        [Tooltip("待机游目间隔下限（秒）")]
        public float eyeSaccadeIntervalMin = 1.2f;
        [Tooltip("待机游目间隔上限（秒）")]
        public float eyeSaccadeIntervalMax = 3.0f;
        [Tooltip("瞄准俯仰映射到眼球竖向偏移的增益（米/度）：抬头瞄准眼球上移")]
        public float eyeLookAimGain = 0.00012f;
        [Tooltip("眼球偏移平滑时间（秒）：扫视快到位、不瞬移")]
        public float eyeLookSmoothTime = 0.07f;

        [Header("滚筒持握 (Roller Wield，参考 Inkwave _poseFlick 四段 / wRoll 拖地 / 滚轴自转)")]
        [Tooltip("待机持握俯仰（度，正 = 主鼓压低斜提；P39 对齐 INKWAVE carry 的压低剪影）")]
        public float rollerCarryPitchDeg = 28f;
        [Tooltip("待机持握偏航（度，正 = 滚筒朝右前方斜提，让出正前方与准星视野）")]
        public float rollerCarryYawDeg = 10f;
        [Tooltip("待机持握滚转（度，负 = 滚轴斜放，两手自然贴身握杆）")]
        public float rollerCarryRollDeg = -8f;
        [Tooltip("挥击抡起角（度，负 = 举过肩；前摇期从持握角抡到此角）")]
        public float rollerFlickLiftPitchDeg = -115f;
        [Tooltip("砸地角（度，正 = 滚筒压向身前地面；默认视角造型角，落点贴地由运行时闭环修正）")]
        public float rollerFlickSlamPitchDeg = 5f;
        [Tooltip("砸地时武器挂点下压量（米）")]
        public float rollerFlickSlamDrop = 0.03f;
        [Tooltip("砸下段时长（秒）：释放墨滴的同时从抡起角加速砸到砸地角")]
        public float rollerFlickWhipDuration = 0.09f;
        [Tooltip("随挥保持时长（秒）：砸地姿态停一拍")]
        public float rollerFlickFollowDuration = 0.10f;
        [Tooltip("收势时长（秒）：从砸地姿态回到持握姿态")]
        public float rollerFlickRecoverDuration = 0.30f;
        [Tooltip("蓄力（前摇）胸口后仰（度）")]
        public float rollerFlickCoilBodyPitchDeg = -12f;
        [Tooltip("甩出瞬间胸口前压（度）")]
        public float rollerFlickWhipBodyPitchDeg = 18f;
        [Tooltip("砸地瞬间果冻压扁冲量（复用跳跃/落地弹簧，越大砸地感越沉）")]
        public float rollerSlamSquashImpulse = 2.8f;
        [Tooltip("拖地姿态俯角（度，正 = 滚筒压向地面；默认视角造型角，贴地由运行时闭环修正）")]
        public float rollerRollPitchDeg = 40f;
        [Tooltip("拖地姿态挂点下压量（米）")]
        public float rollerRollDrop = 0.02f;
        [Tooltip("拖地姿态挂点前推量（米）")]
        public float rollerRollForward = 0.14f;
        [Tooltip("拖地身体前倾（度）")]
        public float rollerRollBodyPitchDeg = 18f;
        [Tooltip("拖地重心下沉（竖向缩放比例）")]
        [Range(0f, 0.3f)]
        public float rollerRollSink = 0.06f;
        [Tooltip("拖地姿态权重进入速率（阻尼 λ，INKWAVE 11 ≈ 0.25s 到 90%）")]
        public float rollerRollWeightInRate = 11f;
        [Tooltip("拖地姿态权重退出速率（阻尼 λ，INKWAVE 6 ≈ 0.4s 回落）")]
        public float rollerRollWeightOutRate = 6f;
        [Tooltip("挥击砸地瞬间滚轴甩转冲量（rad/s，INKWAVE +34）")]
        public float rollerDrumFlickSpin = 34f;
        [Tooltip("滚轴惯性衰减系数（1/s，INKWAVE 2.2：约 0.45s 衰减到 37%）")]
        public float rollerDrumCoastDecay = 2.2f;
        [Tooltip("贴地闭环进入速率（1/s）：按住拖地/砸地落点逼近地面时的俯仰修正响应")]
        public float rollerGroundContactInRate = 16f;
        [Tooltip("贴地闭环退出速率（1/s）：松手/起跳/换枪时释放贴地修正")]
        public float rollerGroundContactOutRate = 22f;
        [Tooltip("滚轴外缘目标压入地面深度（米）：≈1/8 鼓径才读作压进墨面，0 会像悬停擦过")]
        public float rollerGroundPenetration = 0.026f;
        [Tooltip("拖地入地瞬间果冻压扁冲量（0 = 关闭），让角色有'把滚筒按下去'的重量感")]
        public float rollerRollPlantSquashImpulse = 0.6f;
        [Tooltip("滚筒接地阴影开关：鼓体底缘被圆柱侧面挡住，没有贴地暗斑时即使轴心贴地也会读作悬浮")]
        public bool rollerContactShadowEnabled = true;
        [Tooltip("滚筒接地阴影宽度（米，沿鼓轴方向）")]
        public float rollerContactShadowWidth = 0.80f;
        [Tooltip("滚筒接地阴影深度（米，沿前进方向）")]
        public float rollerContactShadowDepth = 0.42f;
        [Tooltip("滚筒接地阴影不透明度（0~1）：与贴地权重相乘，只在鼓体真正压地时出现")]
        [Range(0f, 1f)]
        public float rollerContactShadowOpacity = 0.52f;

        [Header("武器持握/动作 (Weapon Wield P35)")]
        [Tooltip("双枪翻滚/架枪、加特林扫射等二值状态的姿态权重平滑速率（1/s）")]
        public float weaponWieldSmoothRate = 18f;
        [Tooltip("持枪待机微摆幅度（度）：站定时武器绕握把的轻微晃动")]
        public float weaponIdleSwayDeg = 0.6f;
        [Tooltip("持枪待机微摆频率（Hz）")]
        public float weaponIdleSwayHz = 0.4f;
        [Tooltip("蓄力武器后拉距离（米，满蓄力）")]
        public float weaponChargePullback = 0.05f;
        [Tooltip("蓄力武器下压距离（米，满蓄力）")]
        public float weaponChargeDrop = 0.015f;
        [Tooltip("蓄力时胸口后仰（度，满蓄力）")]
        public float weaponChargeBodyPitchDeg = 4f;
        [Tooltip("蓄力抖动幅度（度，满蓄力）")]
        public float weaponChargeTremorDeg = 0.18f;
        [Tooltip("泼桶前摇桶身后仰角（度）")]
        public float slosherCoilPitchDeg = 14f;
        [Tooltip("泼桶甩出桶身前倾角（度）")]
        public float slosherThrowPitchDeg = 32f;
        [Tooltip("泼桶甩出胸口前压（度）")]
        public float slosherThrowBodyPitchDeg = 14f;
        [Tooltip("泼桶甩出动作时长（秒）")]
        public float slosherThrowDuration = 0.24f;
        [Tooltip("爆破枪开火后坐枪口上抬（度）")]
        public float blasterKickPitchDeg = 10f;
        [Tooltip("爆破枪开火后坐距离（米）")]
        public float blasterKickBackMeters = 0.07f;
        [Tooltip("双枪翻滚身体侧倾（度）")]
        public float dualiesDodgeBodyRollDeg = 16f;
        [Tooltip("双枪翻滚重心下沉（比例）")]
        [Range(0f, 0.2f)]
        public float dualiesDodgeSink = 0.05f;
        [Tooltip("双枪翻滚整圈转角（度）：360 = 完整前滚翻，0 = 关闭转体（只剩旧侧倾）")]
        [Range(0f, 720f)]
        public float dualiesDodgeTumbleDegrees = 360f;
        [Tooltip("双枪翻滚快出缓停指数：越大起步转体越爆发（INKWAVE _poseDodge = 2.4）")]
        [Range(0.5f, 5f)]
        public float dualiesDodgeTumbleEase = 2.4f;
        [Tooltip("双枪翻滚中段整体抬升（米）：sin(π·u) 跳弧，起跳/落地都贴地")]
        [Range(0f, 0.6f)]
        public float dualiesDodgeTumbleLift = 0.30f;
        [Tooltip("双枪翻滚蜷身纵向压缩量（0 = 不压缩，0.07 ≈ INKWAVE SQY 压 7%）")]
        [Range(0f, 0.3f)]
        public float dualiesDodgeTuckSquash = 0.07f;
        [Tooltip("双枪翻滚蜷身脊柱/胸前卷角（度，满蜷身）")]
        [Range(0f, 60f)]
        public float dualiesDodgeTuckSpineDeg = 22f;
        [Tooltip("双枪翻滚蜷身屈膝角（度，满蜷身）")]
        [Range(0f, 120f)]
        public float dualiesDodgeTuckKneeDeg = 55f;
        [Tooltip("双枪架枪重心下沉（比例）")]
        [Range(0f, 0.2f)]
        public float dualiesTurretSink = 0.06f;
        [Tooltip("双枪架枪胸口前压（度）")]
        public float dualiesTurretBodyPitchDeg = 5f;
        [Tooltip("加特林蓄力下压距离（米，满蓄力）")]
        public float splatlingChargeDropMeters = 0.025f;
        [Tooltip("加特林蓄力胸口前压（度，满蓄力）")]
        public float splatlingChargeBodyPitchDeg = 3f;
        [Tooltip("加特林枪管最低转速（度/秒）")]
        public float splatlingSpinMinDegPerSec = 280f;
        [Tooltip("加特林枪管最高转速（度/秒）")]
        public float splatlingSpinMaxDegPerSec = 1400f;
        [Tooltip("加特林枪管松开后的惯性衰减率（1/s）")]
        public float splatlingSpinCoastRate = 5f;
        [Tooltip("加特林扫射时机身抖动幅度（度）")]
        public float splatlingStreamShakeDeg = 0.5f;
        [Tooltip("小枪持续射击胸口前压（度）")]
        public float shooterFireBodyPitchDeg = 2f;
        [Tooltip("小枪持续射击枪身回拉（米）")]
        public float shooterFirePullbackMeters = 0.01f;

        [Header("储液罐 (Tank Fill，对齐 INKWAVE character.js _updateTank L3339-3351 / damp L1272 / 事件冲量 L1009/2100/1656)")]
        [Tooltip("显示墨量向真实墨量的指数平滑速率（1/s）：INKWAVE damp rate 8 ≈ 125ms 时间常数，扣墨后液面缓降不瞬跳")]
        public float tankInkChaseRate = 8f;
        [Tooltip("液面 bob 弹簧固有频率（rad/s）：INKWAVE 2.4Hz×2π≈15.1，跳跃/落地/扣墨时液面上下晃")]
        public float tankLevelBobOmega = 15.1f;
        [Tooltip("液面 bob 弹簧阻尼比：INKWAVE 0.18，欠阻尼让液面回摆 1~2 次")]
        [Range(0.05f, 1.5f)]
        public float tankLevelBobZeta = 0.18f;
        [Tooltip("bob 弹簧值→液位百分比的增益（INKWAVE tl×0.06）")]
        public float tankLevelBobGain = 0.06f;
        [Tooltip("液位 bob 占满位的上限（比例，INKWAVE ±0.08）")]
        [Range(0f, 0.3f)]
        public float tankLevelBobMax = 0.08f;
        [Tooltip("晃动倾斜弹簧固有频率（rad/s）：INKWAVE 1.6Hz×2π≈10.05，急加速/急停/转向时液面朝反侧倾")]
        public float tankSloshOmega = 10.05f;
        [Tooltip("晃动倾斜弹簧阻尼比：INKWAVE 0.14，最欠阻尼的一组，液面持续荡漾")]
        [Range(0.05f, 1.5f)]
        public float tankSloshZeta = 0.14f;
        [Tooltip("局部加速度→晃动目标角的增益（rad/(m/s²)，INKWAVE 0.004）")]
        public float tankSloshAccelGain = 0.004f;
        [Tooltip("晃动目标角钳制上限（rad，INKWAVE ±0.1）")]
        public float tankSloshTargetMax = 0.1f;
        [Tooltip("液面倾斜渲染钳制上限（rad，INKWAVE ±0.06 ≈ 3.4°，受罐盖净空约束）")]
        public float tankTiltMaxRad = 0.06f;
        [Tooltip("晃动时液面径向挤压系数（INKWAVE slosh×0.03，保体积感）")]
        public float tankSloshSqueeze = 0.03f;
        [Tooltip("扣墨瞬间液面下沉冲量增益（冲量 = −Δ墨量×该值）：100 时扣 20% 墨液位下沉约 7%，肉眼可辨的「咕咚」")]
        public float tankConsumeKickGain = 100f;
        [Tooltip("起跳液面 bob 冲量（INKWAVE +2.0，character.js L1009）")]
        public float tankEventKickJump = 2f;
        [Tooltip("落地液面 bob 冲量（INKWAVE +2.2，character.js L2100）")]
        public float tankEventKickLand = 2.2f;
        [Tooltip("急停液面 bob 冲量（INKWAVE 急刹 +1.8，character.js L1656）")]
        public float tankEventKickBrake = 1.8f;
        [Tooltip("急停判定阈值（前向减速度 m/s²，低于该值不算急停）")]
        public float tankBrakeAccelMin = 14f;
        [Tooltip("低墨告警触发阈值（墨量比例，<该值液面开始呼吸脉动）")]
        public float tankLowInkFraction = 0.15f;
        [Tooltip("低墨告警解除阈值（>该值停止，与触发值构成迟滞防边界闪烁）")]
        public float tankLowInkExitFraction = 0.2f;
        [Tooltip("低墨呼吸脉动频率（Hz）：与 bob 弹簧同频，视觉语言统一")]
        public float tankLowInkPulseHz = 2.4f;
        [Tooltip("低墨呼吸脉动幅度（液位比例 ±）")]
        public float tankLowInkPulseAmp = 0.03f;
        [Tooltip("液位显示下限（比例，INKWAVE 0.004）")]
        public float tankLevelFloor = 0.004f;
        [Tooltip("液面隐藏阈值：墨量低于该比例直接隐藏液面（INKWAVE 0.005，空罐读作深色背板）")]
        public float tankEmptyHideAt = 0.005f;

        /// <summary>运行时实际使用的速度归一化分母：优先玩家配置的潜墨速度。</summary>
        public float ResolveSwimRefSpeed(PlayerConfigSO playerConfig)
        {
            return playerConfig != null && playerConfig.swimMoveSpeed > 0.1f
                ? playerConfig.swimMoveSpeed
                : swimRefSpeed;
        }
    }
}
