using P1.Ink;
using P1.Player;
using P1.Weapons;
using UnityEngine;

namespace P1.Core
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "P1/Game Config")]
    public class GameConfigSO : ScriptableObject
    {
        [Header("子配置引用")]
        public InkGridConfigSO inkGridConfig;
        public PlayerConfigSO playerConfig;
        public WeaponConfigSO weaponConfig;

        [Header("比赛时长与规则")]
        [Tooltip("设计默认时长（秒）。正式对局由首页「时长」段选覆盖（GameSettingsService，2/3/5 分钟）；" +
                 "仅当设置服务不可用时作为 MatchManager 的回退值。")]
        public float matchDurationSeconds = 120f;
        public float finalWarningSeconds = 30f;

        [Header("击杀奖励（把交火接进主循环）")]
        [Tooltip("击杀敌方时击杀者立刻获得的大招能量点：70 ≈ 1/3 个大招（各武器门槛 170~195）。≤0 回退规范默认，见 KillRewardMath")]
        public float killRewardSpecialPoints = 70f;
        [Tooltip("被击杀方因阵亡损失的大招能量比例（与 WeaponController 阵亡减半同口径）；损失部分全额转给击杀者，0 = 关闭转移")]
        [Range(0f, 1f)]
        public float killRewardEnergyTransferFraction = 0.5f;
        [Tooltip("击杀敌方时击杀者立刻回复的墨水量（满罐 100）。≤0 回退规范默认，见 KillRewardMath")]
        public float killRewardInk = 25f;

        [Header("第三人称相机 (TPS, P9)")]
        [Tooltip("人形状态下相机枢轴相对脚底的高度（米）。按 1.25m 蛋形角色设定为 1.45m，略高于头顶，准星不被自己挡住")]
        public float tpsPivotHeight = 1.45f;
        [Tooltip("潜墨状态下相机枢轴高度（米）。按 1.25m 蛋形角色设定为 1.05m，角色压扁贴地，镜头随之降低")]
        public float tpsPivotHeightSwimming = 1.05f;
        [Tooltip("枢轴跟随的平滑时间（秒）。越小越跟手，越大越稳")]
        public float tpsPivotSmoothTime = 0.06f;
        [Tooltip("人形状态相机吊臂长度（米）")]
        public float tpsDistance = 4.5f;
        [Tooltip("潜墨状态相机吊臂长度（米）")]
        public float tpsDistanceSwimming = 4.1f;
        [Tooltip("贴墙时允许收进的最小吊臂长度（米），不得为 0 以免镜头钻进角色身体")]
        public float tpsMinDistance = 0.55f;
        [Tooltip("第三人称垂直视野（度）。52° 在 16:9 下约等于 82° 水平视野，对齐 Inkwave")]
        public float tpsFov = 52f;
        [Tooltip("默认俯仰角（度）：正数=低头俯视，负数=抬头仰视。6° 对应 Inkwave 默认 -0.1rad")]
        public float tpsPitchDefault = 6f;
        [Tooltip("最大抬头角（度，负值）")]
        public float tpsPitchMin = -60f;
        [Tooltip("最大低头角（度，正值）")]
        public float tpsPitchMax = 66f;
        [Tooltip("相机防穿墙球体探针半径（米）")]
        public float tpsCollisionRadius = 0.32f;
        [Tooltip("吊臂被墙强行压到很近时向右肩偏移的最大距离（米），避免角色背影挡住准星")]
        public float tpsShoulderOffset = 0.55f;
        [Tooltip("吊臂短于该距离（米）时开始启用右肩偏移")]
        public float tpsShoulderCloseDistance = 2.8f;
        [Tooltip("镜头离脚下真实地面的最小高度（米）")]
        public float tpsGroundClearance = 0.25f;
        [Tooltip("屏幕中心瞄准射线的最大探测距离（米）")]
        public float tpsAimRayDistance = 70f;
        [Tooltip("第三人称下震屏位移的额外缩放（0~1）。4.5m 吊臂下 0.42m 原始震幅过强，乘 0.45 更舒适")]
        [Range(0f, 1f)]
        public float tpsShakeScale = 0.45f;
        [Tooltip("蓄力狙满蓄力时的 FOV 收窄量（度）")]
        public float tpsChargerZoomFov = 14f;
        [Tooltip("蓄力狙满蓄力时的吊臂拉近量（米）")]
        public float tpsChargerZoomDistance = 0.6f;
        [Tooltip("手雷瞄准时 FOV 收窄量（度）")]
        public float tpsSubAimFovKick = 4f;
        [Tooltip("手雷瞄准时吊臂拉远量（米），便于看清远端落点")]
        public float tpsSubAimDistanceKick = 0.35f;
        [Tooltip("FOV 随速度拉伸上限（度）：达到潜墨全速时叠加，增强速度感；0 = 关闭（保守小幅度）")]
        public float tpsSpeedFovKick = 3.5f;
        [Tooltip("FOV 速度拉伸起始速度 (m/s)：低于该速度不叠加")]
        public float tpsSpeedFovStartSpeed = 6f;

        [Header("开赛扫场镜头 (P41)：高空巡场 → 定格到玩家身后")]
        [Tooltip("高空巡场机位高度（米）。25m 在 40×56m 场地上能一屏装下整块场地")]
        public float matchIntroOverviewHeight = 25f;
        [Tooltip("高空机位从场地中心朝敌方半场后退的距离（米）：越大越能看清我方半场纵深")]
        public float matchIntroOverviewBackDistance = 19f;
        [Tooltip("高空机位相对场地中轴的横向偏移（米）：斜角构图比正对中轴更有电影感")]
        public float matchIntroOverviewSideOffset = 9f;
        [Tooltip("扫场段绕场地中心横向划过的角度（度）：镜头在高空缓慢平移，读作「扫一遍场地」")]
        public float matchIntroSweepDegrees = 34f;
        [Tooltip("巡场段 FOV 相对跟随机位的加宽量（度）：更广才能装下整块场地")]
        public float matchIntroFovBonus = 12f;
        [Tooltip("扫场段占整条路径的比例（0~1）：之后是俯冲到玩家身后的冲刺段")]
        [Range(0.05f, 0.95f)]
        public float matchIntroDiveStart01 = 0.5f;
        [Tooltip("俯冲段贝塞尔抬升（米）：让镜头从场地上方掠过，而不是贴着地面穿过去")]
        public float matchIntroArcLift = 7f;

        [Header("大招：Tidal Slam 镜头演出（仅本地玩家）")]
        [Tooltip("起跳时相机枢轴额外抬升（米）：角色视觉体升空 4.2m，枢轴抬高 + 吊臂外拉才能把人留在画面内")]
        public float slamCameraPivotRise = 2f;
        [Tooltip("起跳时吊臂额外外拉（米）：拉开后脚下 5.2m 落点预警圈才读得出范围")]
        public float slamCameraDistanceBonus = 3f;
        [Tooltip("起跳时 FOV 加宽（度）")]
        public float slamCameraFovBonus = 4f;
        [Tooltip("起跳展开耗时（秒）：缓出到广角机位")]
        public float slamCameraBlendOutSeconds = 0.22f;
        [Tooltip("下砸阶段收回高机位的耗时（秒）：跟随角色俯冲，越短越强")]
        public float slamCameraFallBlendSeconds = 0.12f;
        [Tooltip("落地后回到默认机位的耗时（秒）")]
        public float slamCameraRecoverSeconds = 0.4f;
        [Tooltip("落地瞬间快速推近量（米）：在吊臂基础上再向枢轴推近")]
        public float slamCameraImpactPush = 0.9f;
        [Tooltip("落地瞬间 FOV 收紧量（度）：与推近叠加制造冲击")]
        public float slamCameraImpactFov = 2.5f;
        [Tooltip("落地冲击弹簧角频率（rad/s）：越小推近越持久")]
        public float slamCameraImpactSpringOmega = 14f;
        [Tooltip("落地冲击弹簧阻尼比：1=无回弹，<1 会有轻微回弹抖动")]
        [Range(0.3f, 1f)]
        public float slamCameraImpactDamping = 0.65f;

        [Header("俯视倾斜相机参数（旧版，保留序列化兼容，P9 起不再使用）")]
        [Tooltip("旧版俯视参数：是否启用透视模式")]
        public bool usePerspective = true;
        [Tooltip("旧版俯视参数：透视视野")]
        public float cameraFov = 40f;
        [Tooltip("旧版俯视参数：正交半高")]
        public float cameraOrthoSize = 4.4f;
        [Tooltip("旧版俯视参数：相机相对玩家的偏移")]
        public Vector3 cameraOffset = new Vector3(0f, 9.2f, -8.2f);
        [Tooltip("旧版俯视参数：瞄准前移上限")]
        public float cameraLookAheadMax = 1.5f;
        [Tooltip("旧版俯视参数：跟随平滑时间")]
        public float cameraSmoothTime = 0.10f;

        [Header("屏幕震颤 (P6-A)")]
        [Tooltip("Trauma=1 时的最大水平抖幅（米）。0.42 在俯视 9m 高度下强劲但不晕眩")]
        public float shakeMaxOffset = 0.42f;
        [Tooltip("Trauma=1 时的最大镜头 Roll 角（度）。仅 1.3° 用于制造冲击感，不影响瞄准")]
        public float shakeMaxRollDegrees = 1.3f;
        [Tooltip("Trauma 每秒自然衰减量。2.4 = 满额震颤约 0.42s 完全平息")]
        public float shakeDecayPerSecond = 2.4f;
        [Tooltip("高频震颤的角频率 (rad/s)。层叠正弦驱动，避免逐帧随机噪声造成的嗡嗡静电感")]
        public float shakeFrequency = 38f;
        [Tooltip("爆炸/大招震屏的距离衰减半径（米）：半径内按距离平方衰减，半径外不震")]
        public float shakeExplosionRadius = 14f;

        /// <summary>
        /// 默认最低越界高度：霓虹滑板场的下沉碗池池底为 -2.1m，属于合法玩法区；
        /// 阈值必须低于池底（-6m 留 3.9m 余量），否则角色下碗池会被误判坠落拉回出生点。
        /// </summary>
        public const float DefaultOutOfBoundsMinY = -6f;

        [Header("越界回收与防坠落 (P16-B)")]
        [Tooltip("越界检测轮询周期（秒）")]
        public float outOfBoundsCheckIntervalSeconds = 0.25f;
        [Tooltip("X 方向允许超出边界的缓冲距离（米）")]
        public float outOfBoundsMarginX = 0.3f;
        [Tooltip("Z 方向允许超出边界的缓冲距离（米）")]
        public float outOfBoundsMarginZ = 0.3f;
        [Tooltip("最低允许高度（米），低于此高度立即判定越界回收；须低于滑板场碗池底 -2.1m")]
        public float outOfBoundsMinY = DefaultOutOfBoundsMinY;
    }
}
