using System;
using System.Collections.Generic;
using P1.Core;
using P1.Ink;
using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// INKWAVE 真骨骼角色（P33 squidkid）与武器模型运行时控制器。
    /// 骨骼树：KidRoot → Hips → BodySquash → Spine → Chest → Neck → Head；
    /// 双臂 肩→肘→手 两骨、双腿 髋→膝→踝 两骨；触须发 6 束 × 2 段弹簧链。
    /// 动画管线（全程序化、分层叠加，无剪辑动画）：
    /// 1. 相位步态（KidGaitSolver.SampleGait）：左右腿独立相位（摆动腿屈膝抬脚、支撑腿伸直踩地），
    ///    骨盆竖向按支撑腿摆角与腿长几何求解（支撑脚世界钉地）；
    /// 2. 脊柱链前倾（P33）：跑动/瞄准/落地的总俯仰按 髋25% / 腰45% / 胸30% 分配
    ///    （对齐 INKWAVE character.js runPose），头部按 pitch 反向稳定让五官始终可读；
    /// 3. 二段臂：肩摆 + 肘屈伸随步态反相；持枪位姿为装配期 authored 基准（缓存增量叠加，绝不覆盖）；
    /// 4. 触须发弹簧链：根段随视在重力（头加速度+俯仰）摆动，梢段二次滞后甩出鞭梢，
    ///    急转/跳跃/落地/变形触发 _hairKick 冲量；
    /// 5. 果冻弹簧（起跳拉伸/落地压扁/滞空仰俯）作用在 BodySquash 上身链，武器根逐帧反缩放抵消；
    /// 6. 惯性倾斜弹簧层（起步前倾/刹车后仰/侧移压弯 + 骨盆滞后）；
    /// 7. 待机生命感：呼吸（实例非同步）/ 重心转移 / 偶发小动作 + 眨眼；
    /// 8. 横移半侧转（骨盆朝移动方向偏转，腿摆平面跟向）；踩敌墨抖动；墨墙攀爬蠕动；
    /// 9. 人 ⇄ 乌贼弹性手势（弹簧 + SO 曲线，过冲即表演）；游动姿态（SwimPoseSolver）+ 触须行波；
    /// 10. 7 类主武器换装、双枪姿态与滚筒挥击/拖地/贴地闭环（P27/P31 管线不变，标定值已按新手位重推）。
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public class CharacterVisualController : MonoBehaviour
    {
        [Header("形态根节点")]
        [SerializeField] private GameObject kidRoot;
        [SerializeField] private GameObject squidRoot;
        [SerializeField] private GameObject weaponRoot;

        [Header("人形态骨骼节点")]
        [SerializeField] private Transform hipsTransform;
        [SerializeField] private Transform spineTransform;
        [SerializeField] private Transform chestTransform;
        [SerializeField] private Transform neckTransform;
        [SerializeField] private Transform headTransform;
        [SerializeField] private Transform leftHairPivot;
        [SerializeField] private Transform rightHairPivot;
        [SerializeField] private Transform leftArmTransform;
        [SerializeField] private Transform rightArmTransform;
        [SerializeField] private Transform tankLiquidFill;

        [Header("触须发弹簧链 (P33：6 束 × 根段/梢段双枢轴)")]
        [SerializeField] private Transform[] kidHairRootPivots;
        [SerializeField] private Transform[] kidHairMidPivots;
        [SerializeField] private Quaternion[] kidHairRootBaseRots;
        [SerializeField] private Quaternion[] kidHairMidBaseRots;

        [Header("人形态双腿 FK 链 (P22-A)")]
        [SerializeField] private Transform leftLegRoot;
        [SerializeField] private Transform rightLegRoot;
        [SerializeField] private Transform leftKneePivot;
        [SerializeField] private Transform rightKneePivot;
        [SerializeField] private Transform leftAnklePivot;
        [SerializeField] private Transform rightAnklePivot;

        [Header("人形态二段臂肘枢轴 (P32)")]
        [SerializeField] private Transform leftElbowPivot;
        [SerializeField] private Transform rightElbowPivot;

        [Header("乌贼形态节点")]
        [SerializeField] private Transform squidBodyTransform;
        [SerializeField] private Transform squidTentaclesRoot;

        [Header("上身挤压与五官节点")]
        [SerializeField] private Transform bodySquashTransform;
        [SerializeField] private Transform faceTransform;
        [SerializeField] private Transform leftEyeTransform;
        [SerializeField] private Transform rightEyeTransform;

        [Header("7 类主武器模型")]
        [SerializeField] private GameObject weaponShooter;
        [SerializeField] private GameObject weaponRoller;
        [SerializeField] private GameObject weaponCharger;
        [SerializeField] private GameObject weaponDualies;
        [SerializeField] private GameObject weaponSlosher;
        [SerializeField] private GameObject weaponBlaster;
        [SerializeField] private GameObject weaponSplatling;

        [Header("动态武器构件")]
        [SerializeField] private Transform rollerDrumTransform;
        [SerializeField] private Transform splatlingBarrelsTransform;
        [SerializeField] private Renderer[] chargerCoilRenderers;
        [SerializeField] private Material chargerCoilMaterial;

        [Header("枪口锚点")]
        [SerializeField] private Transform muzzleShooter;
        [SerializeField] private Transform muzzleRoller;
        [SerializeField] private Transform muzzleCharger;
        [SerializeField] private Transform muzzleDualiesRight;
        [SerializeField] private Transform muzzleDualiesLeft;
        [SerializeField] private Transform muzzleSlosher;
        [SerializeField] private Transform muzzleBlaster;
        [SerializeField] private Transform muzzleSplatling;

        private InkCharacterMotor _motor;
        private WeaponController _weaponController;
        private Team _team = Team.TeamA;
        private WeaponKind _currentWeaponKind = WeaponKind.Shooter;
        private Transform _activeMuzzle;

        // 默认姿态与缩放缓存（支持重新装配刷新，且防止每帧累乘/被覆盖）
        private Transform _lastCachedSquashTransform;
        private Vector3 _defaultBodySquashPos = Vector3.zero;
        private Vector3 _defaultBodySquashScale = Vector3.one;

        private Quaternion _defaultChestRot = Quaternion.identity;
        private Vector3 _defaultHeadLocalPos = Vector3.zero;
        private Quaternion _defaultLeftHairRot = Quaternion.identity;
        private Quaternion _defaultRightHairRot = Quaternion.identity;

        private Transform _lastCachedLeftArm;
        private Vector3 _defaultLeftArmPos = Vector3.zero;
        private Quaternion _defaultLeftArmRot = Quaternion.identity;
        private Quaternion _leftArmWeaponPoseRot = Quaternion.identity;
        private Quaternion _defaultRightArmRot = Quaternion.identity;

        // P34 按武器解算的持握姿态（装配器 BindArmHoldPoses 注入）：
        // 每把武器的肩/肘基准角度 + 左右肩枢轴位置，切枪时整套切换，
        // 保证握把永远在手上、左手托握/双持姿态不再靠位置 hack。
        private readonly Dictionary<WeaponKind, ArmHoldPose> _armHoldPoses =
            new Dictionary<WeaponKind, ArmHoldPose>();
        private Quaternion _rightArmPoseRot = Quaternion.identity;
        private Quaternion _rightElbowPoseRot = Quaternion.identity;
        private Quaternion _leftElbowPoseRot = Quaternion.identity;

        // P34 运行期握把追踪：当前武器真实的右手握把 / 左手托握点（每帧 IK 指过去）。
        // 字典只在装配期写入、场景保存后丢失，运行期必须按当前武器重新缓存，
        // 否则切枪后手臂会退回"笔直垂下"，武器则漂在胸前（v0.1.8 的实际表现）。
        private Transform _heldRightGrip;
        private Transform _heldLeftGrip;
        private Transform _heldLeftSupportModel;
        private Vector3 _heldLeftSupportLocal;
        private bool _heldHasLeftSupport;

        // 双腿 FK 基准姿态（装配期为 identity，缓存防写回累乘）
        private Quaternion _defaultLeftLegRot = Quaternion.identity;
        private Quaternion _defaultRightLegRot = Quaternion.identity;
        private Quaternion _defaultLeftKneeRot = Quaternion.identity;
        private Quaternion _defaultRightKneeRot = Quaternion.identity;
        private Quaternion _defaultLeftAnkleRot = Quaternion.identity;
        private Quaternion _defaultRightAnkleRot = Quaternion.identity;

        private Transform _lastCachedLeftEye;
        private Transform _lastCachedRightEye;
        private Vector3 _defaultLeftEyeScale = Vector3.one;
        private Vector3 _defaultRightEyeScale = Vector3.one;

        // P32 二段臂肘枢轴基准 + P33 头部稳定基准（装配期 authored，缓存防写回累乘）
        private Quaternion _defaultLeftElbowRot = Quaternion.identity;
        private Quaternion _defaultRightElbowRot = Quaternion.identity;
        private Transform _lastCachedFace;
        private Quaternion _defaultFaceRot = Quaternion.identity;

        // P33 脊柱链与武器反缩放基准
        private Quaternion _defaultSpineRot = Quaternion.identity;
        private Quaternion _defaultNeckRot = Quaternion.identity;
        private Quaternion _defaultHeadRot = Quaternion.identity;
        private Vector3 _defaultWeaponRootScale = Vector3.one;

        // P33 触须发弹簧链状态（与 kidHairRootPivots 等长；AnimSpring1D 是 struct，元素原地更新）
        private AnimSpring1D[] _hairRootPitchSprings;
        private AnimSpring1D[] _hairRootRollSprings;
        private AnimSpring1D[] _hairMidPitchSprings;
        private AnimSpring1D[] _hairMidRollSprings;

        private Vector3 _tankFillBasePos = Vector3.zero;
        private Vector3 _tankFillBaseScale = Vector3.one;
        private Vector3 _weaponRootBasePos = Vector3.zero;
        private bool _weaponEventsBound;

        // 运动采样与物理状态
        private Vector3 _prevWorldPos;
        private float _planarSpeed;
        private float _prevYaw;
        private float _verticalVel;
        private bool _wasGrounded = true;
        private float _lastAirVelY;
        private float _airTime;

        // 蛋形步态相位与果冻弹簧状态
        private float _gaitPhase;
        private float _jelly;
        private float _jellyVel;
        // 起跳瞬间的水平速度（>3 才做"跑步起跳"前后腿劈开）
        private float _jumpRunSpeed;

        // P26 横移半侧转平滑（身体朝移动方向偏转一小角，消横移滑步感）
        private float _strafeYawCurrent;
        private float _strafeYawVelocity;

        // P26 惯性倾斜弹簧层（加速度驱动：起步前倾/刹车后仰/侧移压弯）与骨盆滞后位移
        // 注意：AnimSpring1D 是 struct，字段不能标 readonly（readonly 上调 Step 会作用在防御性副本上）
        private AnimSpring1D _leanPitchSpring = new AnimSpring1D(0f);
        private AnimSpring1D _leanRollSpring = new AnimSpring1D(0f);
        private AnimSpring1D _hipsLagSpringX = new AnimSpring1D(0f);
        private AnimSpring1D _hipsLagSpringZ = new AnimSpring1D(0f);
        private Vector3 _prevLocalVel;
        private Vector3 _localAccel;

        // P26 Hips 默认位置缓存（步态骨盆下沉 + 惯性滞后写回基准；守卫防动画污染后被误当基准）
        private Transform _lastCachedHips;
        private Vector3 _defaultHipsPos;
        private Quaternion _defaultHipsRot = Quaternion.identity;

        // P29 脚步锁相：步态相位每跨过一次触地点发一次事件（左右交替），供脚步音效与视觉同步
        private int _lastStepIndex = int.MinValue;

        /// <summary>
        /// 步态触地事件（每只脚落地一次，左右交替）。参数 = 是否左脚。
        /// 供 MovementAudioDriver 与脚步 VFX 与视觉步频锁相（旧的距离累积触发作为无视觉控制器时的兜底）。
        /// </summary>
        public event Action<bool> FootstepLanded;

        // P26 落地表演窗口：屈膝保持 + 双臂下甩 + 胸口前倾冲量（线性衰减）
        private float _landTime;
        private float _landSquash01;
        private float _landArmKickDeg;
        private float _landPitchKickDeg;

        // P26 待机生命感：呼吸（实例非同步）/ 重心转移 / 微动作
        private float _breathPhase;
        private float _breathHzJitter = 1f;
        private float _shiftTimer;
        private float _shiftTarget;
        private AnimSpring1D _shiftSpring = new AnimSpring1D(0f);
        private float _fidgetTimer;
        private int _fidgetKind = -1;
        private float _fidgetTime;
        private float _fidgetDuration = 1f;
        private bool _fidgetJumpDone;
        private float _lastFireTime = -10f;

        // P27 滚筒持握/攻击动画状态：挥击时间线（单时钟：前摇抡起→砸地→随挥→收势，
        // IsFlicking 上升沿起表、下降沿=墨滴释放帧只触发冲量）、拖地姿态权重（入 11/出 6 阻尼）、
        // 滚轴角速度（拖地按地速无滑动滚动 + 停转惯性滑行）
        private bool _rollerFlickActive;
        private float _rollerFlickTime;
        private bool _prevFlicking;
        private float _rollerRollWeight;
        private float _drumSpinRadPerSec;
        // 滚筒姿态帧输出（UpdateAimPose / UpdateRecoilKick / UpdateBodyPose 消费；非滚筒武器恒为 0）
        private float _rollerPosePitchDeg;
        private float _rollerPoseYawDeg;
        private float _rollerPoseRollDeg;
        private float _rollerPoseDrop;
        private float _rollerPoseForward;
        private float _rollerBodyPitchDeg;
        private float _rollerSink01;

        // P31 滚筒贴地闭环：GroundContact01（姿态层声明"需要贴地"的时段）→ 平滑权重 →
        // 末段 LateUpdate 依据真实骨架与地面射线做俯仰修正。视角俯仰/身体前倾/坡度不再让滚轴悬空或陷地。
        private float _rollerPoseContact01;
        private float _rollerContactWeight;
        private readonly RaycastHit[] _rollerGroundHits = new RaycastHit[8];
        private bool _rollerModelBaseCaptured;
        private Vector3 _rollerModelBaseLocalPos;

        // P35 通用武器持握/动作层帧值（非滚筒武器；滚筒恒为 0，由 RollerWieldSolver 全权接管）。
        // 由 UpdateWeaponWieldPose 采样，在 UpdateAimPose / UpdateRecoilKick / UpdateBodyPose 合成写回。
        private float _wieldPitchDeg;
        private float _wieldYawDeg;
        private float _wieldRollDeg;
        private float _wieldDrop;
        private float _wieldForward;
        private float _wieldBodyPitchDeg;
        private float _wieldBodyRollDeg;
        private float _wieldSink01;
        private float _wieldTime;
        private float _shooterKick01;
        private float _dualiesKick01;
        private float _dualiesKickSign = 1f;
        private float _chargerReleaseTimer;
        private float _chargerReleaseStrength = 1f;
        private float _slosherThrowTimer;
        private float _blasterKickTimer;
        private float _dodgeWeight;
        private float _turretWeight;
        private float _streamingWeight;
        private float _splatlingSpinDegPerSec;
        private float _rollerSprayTimer;

        // 身体前倾与侧倾平滑状态（Lean Springs）
        private float _currentPitch;
        private float _pitchVelocity;
        private float _currentRoll;
        private float _rollVelocity;

        // 墨焰/触须发辫次级惯性摆动
        private float _currentHairPitch;
        private float _hairPitchVel;
        private float _currentHairRoll;
        private float _hairRollVel;

        // 开火后坐力弹簧（P28）：Z / Pitch / Yaw 使用同一角频率，冲量注入后快速出去、带轻微过冲回稳
        private AnimSpring1D _recoilZSpring = new AnimSpring1D(0f);
        private AnimSpring1D _recoilPitchSpring = new AnimSpring1D(0f);
        private AnimSpring1D _recoilYawSpring = new AnimSpring1D(0f);
        private int _lastRecoilFrame = -1;

        // P36 受击顿挫：订阅 CombatantHealth.DamageApplied，按攻击者方位注入
        // 后仰/侧让弹簧冲量（目标恒 0，只在事件里 Kick）。与白闪/震屏并行，不进武器姿态通道。
        private AnimSpring1D _flinchPitchSpring = new AnimSpring1D(0f);
        private AnimSpring1D _flinchRollSpring = new AnimSpring1D(0f);
        private CombatantHealth _health;

        // P36 视线灵动：眼球（含虹膜/瞳孔/高光的整只合并网格）在深色眼罩内整体微移。
        // 待机随机游目（saccade）+ 瞄准俯仰注视；只写 localPosition，与眨眼的 localScale 互不干扰。
        private Vector3 _defaultLeftEyePos;
        private Vector3 _defaultRightEyePos;
        private Vector2 _eyeLookCurrent;
        private Vector2 _eyeLookVelocity;
        private Vector2 _eyeSaccadeTarget;
        private float _eyeSaccadeTimer = 1.5f;

        // 瞄准俯仰姿态：相机俯仰（正 = 低头）→ 武器挂点承担全部仰角，胸腔只分担一小部分
        private float _aimPitchTarget;
        private float _aimPitchCurrent;
        private float _aimPitchVelocity;
        private readonly Dictionary<GameObject, Quaternion> _weaponModelBaseRots = new Dictionary<GameObject, Quaternion>();

        // 实例独立随机眨眼
        private float _nextBlinkTime = 2.0f;
        private float _blinkStartTime = -1f;

        // 乌贼潜行形态手势：弹簧驱动的形态标量 f (0 = Kid, 1 = Squid，过冲区 [-0.35, 1.35])。
        // 快速翻转目标时弹簧状态天然连续，杜绝跳变（INKWAVE 用双向关键帧时间轴 + 按当前缩放反查时间防 pop，
        // 弹簧模型一次配置双向复用，过冲区即表演区：f<0 浮出拉伸、f>1 潜入回稳摆动）。
        // 注意：不能标记 readonly —— 对 readonly struct 字段调用 Step() 会作用在防御性副本上，状态永远不推进
        private AnimSpring1D _formSpring = new AnimSpring1D(0f);
        private float _formAmount;
        private bool _isCurrentlySwimming;
        private bool _weaponVisuallyHidden;

        // 逐根拖尾触须（装配期绑定；必须序列化，否则进 Play 场景克隆后引用丢失、逐根行波失效）
        [SerializeField] private Transform[] _squidTentacles;
        [SerializeField] private Quaternion[] _squidTentacleBaseRots;
        private Quaternion _squidTentaclesRootBaseRot = Quaternion.identity;

        // 形态切换表演点缀（甩臂 / 果冻冲量 / 墨焰甩动，线性衰减，零 GC）：
        // 潜入 = 自由臂高举如跳水 + 轻微下蹲压扁；浮出 = 双臂弹开亮相 + 起跳式拉伸
        private float _flingArmDeg;
        private float _flingArmRate;
        private float _flingHairPitchDeg;
        private float _flingHairRollDeg;
        private float _flingHairRate;

        // 游动侧倾平滑（与 Lean 同款 SmoothDamp 语义，防角速度尖峰直接上身）
        private float _swimBankCurrent;
        private float _swimBankVelocity;

        // 全形态共享的运动采样（SampleMotion 每帧计算，潜行期同样可用）
        private Vector3 _localVel;
        private float _yawRate;
        private bool _groundedNow = true;
        private bool _teleportedThisFrame;

        [SerializeField] private CharacterAnimConfigSO _animConfig;
        private static CharacterAnimConfigSO _defaultAnimConfig;

        // P30 接触阴影（运行时生成，挂在角色根节点下，不受身体挤压/缩放影响）
        private CharacterContactShadow _contactShadow;

        // P30 视觉贴地补偿：KidRoot/SquidRoot authored 局部位置按 skinWidth 下移
        // （不能动 VisualRoot 本身：WeaponController 的大招升空/取消会直接写它的 localPosition）
        private bool _rootGroundOffsetCaptured;
        private Vector3 _defaultKidRootPos;
        private Vector3 _defaultSquidRootPos;

        private Vector3 _squidBodyDefaultPos = Vector3.zero;
        private Vector3 _defaultSquidBodyScale = Vector3.one;
        private Quaternion _defaultSquidBodyRot = Quaternion.identity;
        private Vector3 _defaultKidRootScale = Vector3.one;
        private Vector3 _defaultSquidRootScale = Vector3.one;

        // 武器缓存表
        private readonly Dictionary<WeaponKind, GameObject> _weaponMap = new Dictionary<WeaponKind, GameObject>();
        private readonly Dictionary<WeaponKind, Transform> _muzzleMap = new Dictionary<WeaponKind, Transform>();

        private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");
        private MaterialPropertyBlock _chargerCoilPropBlock;

        public GameObject KidRoot => kidRoot;
        public GameObject SquidRoot => squidRoot;
        public GameObject WeaponRoot => weaponRoot;
        public Transform ActiveMuzzle => _activeMuzzle != null ? _activeMuzzle : transform;
        public WeaponKind CurrentWeaponKind => _currentWeaponKind;

        /// <summary>供编辑器展示图/测试读取挤压节点。</summary>
        public Transform BodySquashTransform => bodySquashTransform;

        /// <summary>供编辑器展示图/测试读取触须发根枢轴（P32 头发惯性摆荡绑定校验）。</summary>
        public Transform LeftHairPivot => leftHairPivot;
        public Transform RightHairPivot => rightHairPivot;

        /// <summary>供测试读取二段臂肘枢轴绑定（P32）。</summary>
        public Transform LeftElbowPivot => leftElbowPivot;
        public Transform RightElbowPivot => rightElbowPivot;

        /// <summary>供编辑器/测试读取 P33 骨骼节点。</summary>
        public Transform HipsTransform => hipsTransform;
        public Transform ChestTransform => chestTransform;
        public Transform SpineTransform => spineTransform;
        public Transform NeckTransform => neckTransform;

        /// <summary>
        /// 保留原有装配器 30 参数签名与顺序完全一致。
        /// </summary>
        public void BindHierarchy(
            GameObject kid,
            GameObject squid,
            GameObject weapon,
            Transform hips,
            Transform chest,
            Transform head,
            Transform leftHair,
            Transform rightHair,
            Transform leftArm,
            Transform rightArm,
            Transform tankFill,
            Transform squidBody,
            Transform squidTentacles,
            GameObject shooter,
            GameObject roller,
            GameObject charger,
            GameObject dualies,
            GameObject slosher,
            GameObject blaster,
            GameObject splatling,
            Transform drum,
            Transform barrels,
            Renderer[] coils,
            Transform mShooter,
            Transform mRoller,
            Transform mCharger,
            Transform mDualiesR,
            Transform mDualiesL,
            Transform mSlosher,
            Transform mBlaster,
            Transform mSplatling)
        {
            kidRoot = kid;
            squidRoot = squid;
            weaponRoot = weapon;
            hipsTransform = hips;
            chestTransform = chest;
            headTransform = head;
            leftHairPivot = leftHair;
            rightHairPivot = rightHair;
            leftArmTransform = leftArm;
            rightArmTransform = rightArm;
            tankLiquidFill = tankFill;
            squidBodyTransform = squidBody;
            squidTentaclesRoot = squidTentacles;

            weaponShooter = shooter;
            weaponRoller = roller;
            weaponCharger = charger;
            weaponDualies = dualies;
            weaponSlosher = slosher;
            weaponBlaster = blaster;
            weaponSplatling = splatling;

            rollerDrumTransform = drum;
            splatlingBarrelsTransform = barrels;
            chargerCoilRenderers = coils;

            muzzleShooter = mShooter;
            muzzleRoller = mRoller;
            muzzleCharger = mCharger;
            muzzleDualiesRight = mDualiesR;
            muzzleDualiesLeft = mDualiesL;
            muzzleSlosher = mSlosher;
            muzzleBlaster = mBlaster;
            muzzleSplatling = mSplatling;

            CacheDefaultTransforms();
            BuildWeaponMaps();
        }

        /// <summary>由 CharacterVisualAssembler 在装配完成后调用，绑定蛋形专属节点。</summary>
        public void BindEggRig(Transform bodySquash, Transform face, Transform leftEye, Transform rightEye)
        {
            bodySquashTransform = bodySquash;
            faceTransform = face;
            leftEyeTransform = leftEye;
            rightEyeTransform = rightEye;

            CacheDefaultTransforms();
        }

        /// <summary>
        /// 由 CharacterVisualAssembler 绑定双腿 FK 链（P22-A）。
        /// 参数允许为 null（旧场景 / 无腿兜底），写回处逐节点判空。
        /// </summary>
        public void BindLegRig(
            Transform hips,
            Transform leftLeg,
            Transform rightLeg,
            Transform leftKnee,
            Transform rightKnee,
            Transform leftAnkle,
            Transform rightAnkle)
        {
            hipsTransform = hips;
            leftLegRoot = leftLeg;
            rightLegRoot = rightLeg;
            leftKneePivot = leftKnee;
            rightKneePivot = rightKnee;
            leftAnklePivot = leftAnkle;
            rightAnklePivot = rightAnkle;

            CacheDefaultTransforms();
        }

        /// <summary>注入动画调参配置（可为 null，运行时回退到 INKWAVE 数值的共享默认实例）。</summary>
        public void BindAnimationConfig(CharacterAnimConfigSO config)
        {
            _animConfig = config;
        }

        /// <summary>
        /// 由 CharacterVisualAssembler 绑定二段臂肘枢轴（P32）。
        /// 参数允许为 null（旧场景 / 单段臂兜底），写回处逐节点判空。
        /// </summary>
        public void BindArmRig(Transform leftElbow, Transform rightElbow)
        {
            leftElbowPivot = leftElbow;
            rightElbowPivot = rightElbow;

            CacheDefaultTransforms();
        }

        /// <summary>
        /// 由 CharacterVisualAssembler 注入每把武器的手臂持握姿态（P34）。
        /// 表为空时控制器退回旧行为（固定 authored 角度 + 左手位置 hack），保持向后兼容。
        /// </summary>
        public void BindArmHoldPoses(Dictionary<WeaponKind, ArmHoldPose> poses)
        {
            _armHoldPoses.Clear();
            if (poses != null)
            {
                foreach (var kvp in poses)
                {
                    _armHoldPoses[kvp.Key] = kvp.Value;
                }
            }

            _rightArmPoseRot = _defaultRightArmRot;
            _rightElbowPoseRot = _defaultRightElbowRot;
            _leftElbowPoseRot = _defaultLeftElbowRot;
        }

        /// <summary>
        /// 按当前骨架与武器模型重建 7 套持握姿态（装配期与运行期共用 <see cref="WeaponHoldRig"/>）。
        /// 场景里烘焙的角度不能作为唯一来源：字典不序列化，独立包/重载场景后必须能自洽重算。
        /// </summary>
        private void RebuildArmHoldPoses()
        {
            if (weaponRoot == null) return;

            Dictionary<WeaponKind, ArmHoldPose> poses = WeaponHoldRig.Build(
                weaponRoot.transform,
                leftArmTransform, rightArmTransform, leftElbowPivot, rightElbowPivot,
                weaponShooter, weaponRoller, weaponCharger, weaponDualies,
                weaponSlosher, weaponBlaster, weaponSplatling,
                WeaponHoldRig.ChestChainY);
            BindArmHoldPoses(poses);
            CacheRollerModelBasePosition();
        }

        /// <summary>
        /// P35 滚筒贴地保险的基准位置：握把锚定（WeaponHoldRig.Build）后滚筒模型的 localPosition。
        /// 贴地闭环每帧先把模型复位到此基准，再叠加俯仰/高度修正，避免逐帧累积漂移。
        /// </summary>
        private void CacheRollerModelBasePosition()
        {
            _rollerModelBaseCaptured = false;
            if (_weaponMap != null &&
                _weaponMap.TryGetValue(WeaponKind.Roller, out GameObject roller) &&
                roller != null)
            {
                _rollerModelBaseLocalPos = roller.transform.localPosition;
                _rollerModelBaseCaptured = true;
            }
        }

        /// <summary>
        /// 缓存当前武器可被 IK 追踪的握把：右手扳机握把 + （双枪）左手枪握把 / （滚筒）杆上托握点。
        /// 每把武器只做一次层级查找，逐帧只读 Transform.position。
        /// </summary>
        private void CacheHeldGripTargets(WeaponKind kind)
        {
            _heldRightGrip = null;
            _heldLeftGrip = null;
            _heldLeftSupportModel = null;
            _heldLeftSupportLocal = Vector3.zero;
            _heldHasLeftSupport = WeaponHoldTargets.HasLeftWeaponSupport(kind);

            if (_weaponMap.TryGetValue(kind, out GameObject modelGo) && modelGo != null)
            {
                Transform model = modelGo.transform;
                if (kind == WeaponKind.Dualies)
                {
                    Transform rightPistol = WeaponHoldRig.FindChildByName(model, "RightPistol");
                    Transform leftPistol = WeaponHoldRig.FindChildByName(model, "LeftPistol");
                    _heldRightGrip = rightPistol != null
                        ? WeaponHoldRig.FindChildByName(rightPistol, "Grip")
                        : null;
                    _heldLeftGrip = leftPistol != null
                        ? WeaponHoldRig.FindChildByName(leftPistol, "Grip")
                        : null;
                }
                else
                {
                    _heldRightGrip = WeaponHoldRig.FindChildByName(
                        model, WeaponHoldTargets.RightGripName(kind));

                    if (kind == WeaponKind.Roller)
                    {
                        _heldLeftSupportModel = model;
                        _heldLeftSupportLocal = WeaponHoldTargets.LeftSupportLocal(kind);
                    }
                    else if (kind == WeaponKind.Splatling)
                    {
                        _heldLeftGrip = WeaponHoldRig.FindChildByName(model, "ForwardVerticalGrip");
                    }
                }
            }

            if (!_heldHasLeftSupport)
            {
                _heldLeftGrip = null;
                _heldLeftSupportModel = null;
            }
        }

        /// <summary>
        /// 末段手持 IK：所有身体/武器姿态写完后，把两只手重新指到武器**当前**的握把位置。
        /// 瞄准俯仰、后坐力、滚筒挥击/拖地都会让握把离开静态解算点，只靠切枪时的固定角度
        /// 会出现"枪动了手没动"；逐帧两骨 IK 让手始终贴在握把上，肘部随身体自然弯折。
        /// 零分配，每角色每帧最多 2 次解算。
        /// </summary>
        private void ApplyHeldHandTargets()
        {
            if (_formAmount >= 0.99f || _weaponVisuallyHidden) return;
            if (chestTransform == null || rightArmTransform == null) return;

            if (_heldRightGrip != null &&
                WeaponHoldRig.SolveRightHand(
                    chestTransform, rightArmTransform, _heldRightGrip.position,
                    out Quaternion rightShoulder, out Quaternion rightElbow))
            {
                rightArmTransform.localRotation = rightShoulder;
                if (rightElbowPivot != null) rightElbowPivot.localRotation = rightElbow;
            }

            if (!_heldHasLeftSupport || leftArmTransform == null) return;

            Vector3 leftWorld;
            if (_heldLeftGrip != null)
            {
                leftWorld = _heldLeftGrip.position;
            }
            else if (_heldLeftSupportModel != null)
            {
                leftWorld = _heldLeftSupportModel.TransformPoint(_heldLeftSupportLocal);
            }
            else
            {
                return;
            }

            if (WeaponHoldRig.SolveLeftHand(
                    chestTransform, leftArmTransform, leftWorld,
                    out Quaternion leftShoulder, out Quaternion leftElbow))
            {
                leftArmTransform.localRotation = leftShoulder;
                if (leftElbowPivot != null) leftElbowPivot.localRotation = leftElbow;
            }
        }

        /// <summary>
        /// 由 CharacterVisualAssembler 绑定 P33 骨骼链（脊柱三层 + 头骨 + 触须发两节弹簧链）。
        /// spine/neck/head 允许为 null（旧蛋形场景兜底：控制器退化为"胸腔整转 + 双 pivot 摆动"）。
        /// </summary>
        public void BindKidRig(
            Transform spine,
            Transform neck,
            Transform head,
            Transform face,
            Transform[] hairRootPivots,
            Transform[] hairMidPivots)
        {
            if (spine != null) spineTransform = spine;
            if (neck != null) neckTransform = neck;
            if (head != null) headTransform = head;
            if (face != null) faceTransform = face;
            kidHairRootPivots = hairRootPivots;
            kidHairMidPivots = hairMidPivots;
            if (hairRootPivots != null && hairRootPivots.Length > 0)
            {
                if (kidHairRootBaseRots == null || kidHairRootBaseRots.Length != hairRootPivots.Length)
                {
                    kidHairRootBaseRots = new Quaternion[hairRootPivots.Length];
                }
                for (int i = 0; i < hairRootPivots.Length; i++)
                {
                    kidHairRootBaseRots[i] = hairRootPivots[i] != null ? hairRootPivots[i].localRotation : Quaternion.identity;
                }
            }
            if (hairMidPivots != null && hairMidPivots.Length > 0)
            {
                if (kidHairMidBaseRots == null || kidHairMidBaseRots.Length != hairMidPivots.Length)
                {
                    kidHairMidBaseRots = new Quaternion[hairMidPivots.Length];
                }
                for (int i = 0; i < hairMidPivots.Length; i++)
                {
                    kidHairMidBaseRots[i] = hairMidPivots[i] != null ? hairMidPivots[i].localRotation : Quaternion.identity;
                }
            }
            EnsureHairChainSprings();
            CacheDefaultTransforms();
        }

        /// <summary>触须发弹簧链状态数组惰性对齐（换装配/克隆后长度变化时重建）。</summary>
        private void EnsureHairChainSprings()
        {
            int count = kidHairRootPivots != null ? kidHairRootPivots.Length : 0;
            if (_hairRootPitchSprings != null && _hairRootPitchSprings.Length == count) return;

            _hairRootPitchSprings = new AnimSpring1D[count];
            _hairRootRollSprings = new AnimSpring1D[count];
            _hairMidPitchSprings = new AnimSpring1D[Mathf.Max(0, kidHairMidPivots != null ? kidHairMidPivots.Length : 0)];
            _hairMidRollSprings = new AnimSpring1D[_hairMidPitchSprings.Length];
            for (int i = 0; i < count; i++)
            {
                _hairRootPitchSprings[i] = new AnimSpring1D(0f);
                _hairRootRollSprings[i] = new AnimSpring1D(0f);
            }
            for (int i = 0; i < _hairMidPitchSprings.Length; i++)
            {
                _hairMidPitchSprings[i] = new AnimSpring1D(0f);
                _hairMidRollSprings[i] = new AnimSpring1D(0f);
            }
        }

        /// <summary>实际生效的动画配置：绑定资产优先，兜底共享默认实例（无资产也能跑，测试/独立包安全）。</summary>
        public CharacterAnimConfigSO AnimConfig
        {
            get
            {
                if (_animConfig != null) return _animConfig;
                if (_defaultAnimConfig == null)
                {
                    _defaultAnimConfig = ScriptableObject.CreateInstance<CharacterAnimConfigSO>();
                }
                return _defaultAnimConfig;
            }
        }

        /// <summary>由 CharacterVisualAssembler 绑定 4 根拖尾触须，驱动逐根行波（无蒙皮骨骼的相位差近似）。</summary>
        public void BindSquidTentacles(Transform[] tentacles)
        {
            _squidTentacles = tentacles;
            if (tentacles == null)
            {
                _squidTentacleBaseRots = null;
                return;
            }
            if (_squidTentacleBaseRots == null || _squidTentacleBaseRots.Length != tentacles.Length)
            {
                _squidTentacleBaseRots = new Quaternion[tentacles.Length];
            }
            for (int i = 0; i < tentacles.Length; i++)
            {
                _squidTentacleBaseRots[i] = tentacles[i] != null ? tentacles[i].localRotation : Quaternion.identity;
            }
        }

        private void Awake()
        {
            _nextBlinkTime = Time.time + UnityEngine.Random.Range(1.5f, 4.0f);
            _blinkStartTime = -1f;

            // P26 待机生命感实例非同步：呼吸相位/频率抖动、重心转移与微动作计时错开，防止整队同步起伏
            _breathPhase = UnityEngine.Random.value;
            _breathHzJitter = UnityEngine.Random.Range(0.88f, 1.15f);
            _shiftTimer = UnityEngine.Random.Range(1f, 3f);
            _fidgetTimer = UnityEngine.Random.Range(2f, 5f);

            CacheDefaultTransforms();
            BuildWeaponMaps();
            EnsureHairChainSprings();

            // P34 持握姿态运行期重建：_armHoldPoses 是运行期字典（不参与场景序列化），
            // 只在装配器里注入过一次，场景重载/独立包启动后必然为空 —— 旧版因此整套退化回
            // "右臂 identity 笔直垂下、武器漂在胸前"。这里按当前骨架与武器握把重新解算一次，
            // 之后 EquipWeaponKind 就能按武器套用姿态（不依赖场景里烘焙的角度）。
            RebuildArmHoldPoses();

            // P30-C 运行时武器网格合并兜底：场景烘焙角色若装配期合并被缓存瞬时失败跳过，
            // 会保持逐件渲染（每把武器 6~16 个 DrawCall）。这里补一次，静态缓存跨实例共享、
            // MergeWeaponRoot 幂等，全场 8 个角色只构建一次网格（详见 MergeWeaponsRuntime 注释）。
            CharacterVisualAssembler.MergeWeaponsRuntime(
                weaponShooter, weaponRoller, weaponCharger, weaponDualies,
                weaponSlosher, weaponBlaster, weaponSplatling,
                rollerDrumTransform, splatlingBarrelsTransform, chargerCoilRenderers);

            // 触须基准姿态重捕获：Awake 早于任何动画写入，此时场景里保存的就是 authored 姿态；
            // 序列化的绑定引用在 Play 克隆后保留，这里重建基准旋转，避免用残留动画姿态当基准。
            if (_squidTentacles != null && _squidTentacles.Length > 0)
            {
                BindSquidTentacles(_squidTentacles);
            }

            if (_motor == null) _motor = GetComponentInParent<InkCharacterMotor>();
            if (_weaponController == null) _weaponController = GetComponentInParent<WeaponController>();

            // P30 接触阴影：平行光投影落在身后，脚正下方补一块贴地暗斑（运行时生成，不写场景 YAML）
            EnsureContactShadow();

            // P30 视觉贴地：控制器静止时根节点悬空 skinWidth，身体/武器整体下移同一距离
            ApplyVisualGroundOffset();

            // 编辑器装配阶段订阅过的事件不会跨 Domain Reload 保留；运行时必须在 Awake 重新订阅，
            // 否则开火后坐力与运行时换枪（EquipWeaponAt）都不会驱动外观。
            SubscribeWeaponEvents();

            // 场景里保存的左手姿态可能是上一版装配结果：运行时按当前武器重新套用一次姿态与激活状态。
            // 武器种类优先取 WeaponController 的配置（玩家可能带着选枪结果进入对局），
            // 避免启动首帧拿着默认 Shooter 姿态、等切枪事件才纠正。
            if (_weaponController != null && _weaponController.Config != null)
            {
                _currentWeaponKind = _weaponController.Config.weaponKind;
            }
            if (_weaponMap.Count > 0)
            {
                EquipWeaponKind(_currentWeaponKind);
            }
        }

        /// <summary>
        /// P21 运行时角色刚体部件合并：放在 Start 而不是 Awake——
        /// CombatantHealth.Awake 会收集 bodyRenderers，开局 ResetImmediately 还会把它们整套 enable 一次；
        /// 在 Awake 合并会被后者的 enable 覆盖成「源部件 + 合并网格」双份渲染。
        /// Start 时所有 Awake/初始化已完成，再合并并同步 bodyRenderers，源部件保持禁用。
        /// </summary>
        private void Start()
        {
            var bodyMerge = CharacterVisualAssembler.MergeBodyRuntime(new CharacterVisualAssembler.BodyRigRefs
            {
                Spine = spineTransform,
                Chest = chestTransform,
                Head = headTransform,
                Face = faceTransform,
                LeftEye = leftEyeTransform,
                RightEye = rightEyeTransform,
                Backpack = chestTransform != null ? chestTransform.Find("InkTankBackpack") : null,
                TankFill = tankLiquidFill,
                LeftArm = leftArmTransform,
                RightArm = rightArmTransform,
                LeftElbow = leftElbowPivot,
                RightElbow = rightElbowPivot,
                LeftAnkle = leftAnklePivot,
                RightAnkle = rightAnklePivot,
                HairRootPivots = kidHairRootPivots,
                SquidBody = squidBodyTransform,
                SquidTentacles = _squidTentacles
            });

            if (bodyMerge != null && bodyMerge.Merged.Count > 0)
            {
                var health = GetComponentInParent<CombatantHealth>();
                if (health != null)
                {
                    health.ApplyMergedBodyRenderers(bodyMerge.Sources, bodyMerge.Merged);
                }
            }
        }

        /// <summary>
        /// P30 接触阴影：运行时创建一次，挂在角色根节点（visualRoot 的父节点）下。
        /// 编辑模式不创建，避免把运行时生成的网格/材质引用写进场景 YAML。
        /// </summary>
        private void EnsureContactShadow()
        {
            if (!Application.isPlaying || _contactShadow != null) return;

            Transform actorRoot = transform.parent != null ? transform.parent : transform;
            _contactShadow = CharacterContactShadow.Create(actorRoot);
            if (_contactShadow != null)
            {
                _contactShadow.Bind(_motor, this);
            }
        }

        /// <summary>
        /// P30 视觉贴地补偿：CharacterController 静止时胶囊底悬在地面上方 skinWidth（实测 root.y == skinWidth），
        /// 蛋仔腿仅 0.22m，这几毫米到 1.6cm 的间隙足以读成"悬浮"。把 KidRoot/SquidRoot 整体下移同一距离，
        /// 脚底真正压在地面；只改视觉子节点，不动碰撞体，坡道/台阶物理不变。
        /// </summary>
        private void ApplyVisualGroundOffset()
        {
            if (!Application.isPlaying || _motor == null) return;

            if (!_rootGroundOffsetCaptured)
            {
                _defaultKidRootPos = kidRoot != null ? kidRoot.transform.localPosition : Vector3.zero;
                _defaultSquidRootPos = squidRoot != null ? squidRoot.transform.localPosition : Vector3.zero;
                _rootGroundOffsetCaptured = true;
            }

            PlayerConfigSO playerConfig = _motor.Config;
            float sink = Mathf.Clamp(playerConfig != null ? playerConfig.controllerSkinWidth : 0.006f, 0.004f, 0.04f);
            Vector3 down = Vector3.down * sink;
            if (kidRoot != null) kidRoot.transform.localPosition = _defaultKidRootPos + down;
            if (squidRoot != null) squidRoot.transform.localPosition = _defaultSquidRootPos + down;
        }

        private void OnEnable()
        {
            SubscribeWeaponEvents();
            if (_health == null)
            {
                _health = GetComponentInParent<CombatantHealth>();
            }
            CombatantHealth.DamageApplied += HandleDamageApplied;
        }

        private void OnDisable()
        {
            UnsubscribeWeaponEvents();
            CombatantHealth.DamageApplied -= HandleDamageApplied;
        }

        /// <summary>
        /// P36 受击顿挫：子弹命中时按「攻击者 → 我」的方向把上身朝受击方向顿一下
        /// （正面中弹后仰、侧面中弹侧让），伤害越大幅度越大；大伤害附一次吃痛眨眼。
        /// 白闪/震屏/墨花之外的第三层命中反馈，纯增量叠加在动态俯仰/滚转通道上。
        /// </summary>
        private void HandleDamageApplied(CombatantHealth victim, CombatantHealth attacker, float damage, bool killed)
        {
            if (victim == null || victim != _health || !isActiveAndEnabled) return;
            if (damage <= 0f) return;

            CharacterAnimConfigSO cfg = AnimConfig;
            // 小子弹也要可读：0.35 起步，满伤害 1.0
            float strength = Mathf.Lerp(0.35f, 1f,
                Mathf.Clamp01(damage / Mathf.Max(1f, cfg.flinchFullDamage)));

            // 击退方向（攻击者 → 我）转到本地空间：z>0 向后仰、x>0 向右让
            Vector3 localDir = Vector3.forward;
            if (attacker != null)
            {
                Vector3 worldDir = transform.position - attacker.transform.position;
                worldDir.y = 0f;
                if (worldDir.sqrMagnitude > 0.0001f)
                {
                    localDir = transform.InverseTransformDirection(worldDir.normalized);
                }
            }

            float omega = 2f * Mathf.PI * Mathf.Max(1f, cfg.flinchSpringHz);
            _flinchPitchSpring.Kick(AnimSpring1D.ImpulseForPeak(
                -localDir.z * cfg.flinchPitchMaxDeg * strength, omega, cfg.flinchSpringZeta));
            _flinchRollSpring.Kick(AnimSpring1D.ImpulseForPeak(
                -localDir.x * cfg.flinchRollMaxDeg * strength, omega, cfg.flinchSpringZeta));
            // 身体向下砸一下（负果冻冲量），给命中一点重量感
            _jellyVel += cfg.flinchJellyKick * strength;
            if (damage >= cfg.flinchBlinkDamage)
            {
                TriggerBlink();
            }
        }

        private void SubscribeWeaponEvents()
        {
            if (_weaponController == null || _weaponEventsBound)
                return;

            _weaponController.OnWeaponEquipped += HandleWeaponEquipped;
            _weaponController.OnFired += HandleWeaponFired;
            _weaponEventsBound = true;
        }

        private void UnsubscribeWeaponEvents()
        {
            if (_weaponController == null || !_weaponEventsBound)
                return;

            _weaponController.OnWeaponEquipped -= HandleWeaponEquipped;
            _weaponController.OnFired -= HandleWeaponFired;
            _weaponEventsBound = false;
        }

        private void CacheDefaultTransforms()
        {
            // 蛋形挤压节点：优先使用专门的 bodySquashTransform；若未绑定则安全回退到 chestTransform（人形旧场景兜底）
            Transform squash = bodySquashTransform != null ? bodySquashTransform : chestTransform;
            if (squash != null && squash != _lastCachedSquashTransform)
            {
                _lastCachedSquashTransform = squash;
                _defaultBodySquashPos = squash.localPosition;
                _defaultBodySquashScale = squash.localScale;
            }

            if (chestTransform != null)
            {
                _defaultChestRot = chestTransform.localRotation;
            }
            // P33 脊柱链 authored 基准（与 chest 同策略：绑定/Initialize 期缓存，动画只写增量）
            if (spineTransform != null) _defaultSpineRot = spineTransform.localRotation;
            if (neckTransform != null) _defaultNeckRot = neckTransform.localRotation;
            if (headTransform != null) _defaultHeadRot = headTransform.localRotation;
            if (headTransform != null) _defaultHeadLocalPos = headTransform.localPosition;
            if (weaponRoot != null) _defaultWeaponRootScale = weaponRoot.transform.localScale;
            if (headTransform != null)
            {
                _defaultHeadLocalPos = headTransform.localPosition;
            }
            if (leftHairPivot != null)
            {
                _defaultLeftHairRot = leftHairPivot.localRotation;
            }
            if (rightHairPivot != null)
            {
                _defaultRightHairRot = rightHairPivot.localRotation;
            }
            if (tankLiquidFill != null)
            {
                _tankFillBasePos = tankLiquidFill.localPosition;
                _tankFillBaseScale = tankLiquidFill.localScale;
            }

            if (squidBodyTransform != null)
            {
                _squidBodyDefaultPos = squidBodyTransform.localPosition;
                _defaultSquidBodyScale = squidBodyTransform.localScale;
                _defaultSquidBodyRot = squidBodyTransform.localRotation;
            }
            if (kidRoot != null)
            {
                _defaultKidRootScale = kidRoot.transform.localScale;
            }
            if (squidRoot != null)
            {
                _defaultSquidRootScale = squidRoot.transform.localScale;
            }
            if (squidTentaclesRoot != null)
            {
                _squidTentaclesRootBaseRot = squidTentaclesRoot.localRotation;
            }

            // 缓存装配器赋予的初始手臂位姿：仅在首次绑定或挂载目标变更时缓存，避免后续 EquipWeaponKind 相对位移污染基准
            if (leftArmTransform != null && leftArmTransform != _lastCachedLeftArm)
            {
                _lastCachedLeftArm = leftArmTransform;
                _defaultLeftArmPos = leftArmTransform.localPosition;
                _defaultLeftArmRot = leftArmTransform.localRotation;
                _leftArmWeaponPoseRot = _defaultLeftArmRot;
            }
            if (rightArmTransform != null)
            {
                _defaultRightArmRot = rightArmTransform.localRotation;
            }

            // 双腿 FK 链基准姿态
            if (leftLegRoot != null) _defaultLeftLegRot = leftLegRoot.localRotation;
            if (rightLegRoot != null) _defaultRightLegRot = rightLegRoot.localRotation;
            if (leftKneePivot != null) _defaultLeftKneeRot = leftKneePivot.localRotation;
            if (rightKneePivot != null) _defaultRightKneeRot = rightKneePivot.localRotation;
            if (leftAnklePivot != null) _defaultLeftAnkleRot = leftAnklePivot.localRotation;
            if (rightAnklePivot != null) _defaultRightAnkleRot = rightAnklePivot.localRotation;

            // P32 二段臂肘枢轴基准姿态（装配期 identity）
            if (leftElbowPivot != null) _defaultLeftElbowRot = leftElbowPivot.localRotation;
            if (rightElbowPivot != null) _defaultRightElbowRot = rightElbowPivot.localRotation;

            // P32 面部稳定基准（Face 装配期 identity；守卫防动画污染后被误当基准）
            if (faceTransform != null && faceTransform != _lastCachedFace)
            {
                _lastCachedFace = faceTransform;
                _defaultFaceRot = faceTransform.localRotation;
            }

            // P26 Hips 默认位置：步态 bob 全身化（hipsBobShare）与惯性滞后的写回基准。
            // 必须用守卫（同 squash 节点）：动画写回后再次 Cache 会把污染值当基准
            if (hipsTransform != null && hipsTransform != _lastCachedHips)
            {
                _lastCachedHips = hipsTransform;
                _defaultHipsPos = hipsTransform.localPosition;
                _defaultHipsRot = hipsTransform.localRotation;
            }

            // 缓存眼睛默认尺寸与位置，防止眨眼/视线循环写回累乘漂移
            if (leftEyeTransform != null && leftEyeTransform != _lastCachedLeftEye)
            {
                _lastCachedLeftEye = leftEyeTransform;
                _defaultLeftEyeScale = leftEyeTransform.localScale;
                _defaultLeftEyePos = leftEyeTransform.localPosition;
            }
            if (rightEyeTransform != null && rightEyeTransform != _lastCachedRightEye)
            {
                _lastCachedRightEye = rightEyeTransform;
                _defaultRightEyeScale = rightEyeTransform.localScale;
                _defaultRightEyePos = rightEyeTransform.localPosition;
            }

            if (weaponRoot != null)
            {
                _weaponRootBasePos = weaponRoot.transform.localPosition;
            }
            _prevWorldPos = transform.position;
            _prevYaw = transform.eulerAngles.y;
        }

        private void BuildWeaponMaps()
        {
            _weaponMap.Clear();
            _muzzleMap.Clear();
            _weaponModelBaseRots.Clear();

            if (weaponShooter != null) _weaponMap[WeaponKind.Shooter] = weaponShooter;
            if (weaponRoller != null) _weaponMap[WeaponKind.Roller] = weaponRoller;
            if (weaponCharger != null) _weaponMap[WeaponKind.Charger] = weaponCharger;
            if (weaponDualies != null) _weaponMap[WeaponKind.Dualies] = weaponDualies;
            if (weaponSlosher != null) _weaponMap[WeaponKind.Slosher] = weaponSlosher;
            if (weaponBlaster != null) _weaponMap[WeaponKind.Blaster] = weaponBlaster;
            if (weaponSplatling != null) _weaponMap[WeaponKind.Splatling] = weaponSplatling;

            if (muzzleShooter != null) _muzzleMap[WeaponKind.Shooter] = muzzleShooter;
            if (muzzleRoller != null) _muzzleMap[WeaponKind.Roller] = muzzleRoller;
            if (muzzleCharger != null) _muzzleMap[WeaponKind.Charger] = muzzleCharger;
            if (muzzleDualiesRight != null) _muzzleMap[WeaponKind.Dualies] = muzzleDualiesRight;
            if (muzzleSlosher != null) _muzzleMap[WeaponKind.Slosher] = muzzleSlosher;
            if (muzzleBlaster != null) _muzzleMap[WeaponKind.Blaster] = muzzleBlaster;
            if (muzzleSplatling != null) _muzzleMap[WeaponKind.Splatling] = muzzleSplatling;

            // 武器模型基准旋转：必须在任何动画写回之前捕获（Awake/BindHierarchy 阶段场景姿态即 authored）。
            // 旧版在 UpdateAimPose 里懒缓存，一旦模型已带瞄准俯仰再切枪/重绑，就会把俯仰角当成基准，
            // 造成"手追到旧角度、枪却偏一截"——这里一次性锁定，后续只叠加实时俯仰。
            foreach (var kvp in _weaponMap)
            {
                if (kvp.Value != null)
                {
                    _weaponModelBaseRots[kvp.Value] = kvp.Value.transform.localRotation;
                }
            }
        }

        public void Initialize(InkCharacterMotor motor, WeaponController weapon, Team team)
        {
            if (_weaponController != null && _weaponController != weapon)
            {
                UnsubscribeWeaponEvents();
            }

            _motor = motor;
            _weaponController = weapon;
            _team = team;

            SubscribeWeaponEvents();
            CacheDefaultTransforms();
            ApplyVisualGroundOffset();

            // 运行期/装配期都保证持握姿态表可用（场景重载后字典为空，必须重算）
            RebuildArmHoldPoses();

            if (_weaponController != null)
            {
                if (_weaponController.Config != null)
                {
                    EquipWeaponKind(_weaponController.Config.weaponKind);
                }
            }
            else
            {
                EquipWeaponKind(WeaponKind.Shooter);
            }
        }

        private void OnDestroy()
        {
            UnsubscribeWeaponEvents();
        }

        private void HandleWeaponEquipped(WeaponConfigSO cfg)
        {
            if (cfg != null)
            {
                EquipWeaponKind(cfg.weaponKind);
            }
        }

        private void HandleWeaponFired()
        {
            WeaponConfigSO config = _weaponController != null ? _weaponController.Config : null;

            // P27 滚筒：一次挥击 9 颗墨滴、推地每帧各触发一次 OnFired，后坐力若照常施加会被
            // 反复重置（表现为推地时武器持续抖动下沉）——改由专属砸地动画承担全部冲击感。
            // 这里只兜底 flickWindup=0 的极端配置（无 IsFlicking 边沿可用，直接从砸下段起播时间线）。
            if (config != null && config.weaponKind == WeaponKind.Roller)
            {
                if (!_rollerFlickActive && _rollerRollWeight < 0.2f)
                {
                    _rollerFlickActive = true;
                    _rollerFlickTime = 0f;
                }
                _lastFireTime = Time.time;
                return;
            }

            // P28 齐射去重：泼桶一次 8 滴 / 滚筒一次 9 滴会同帧触发多次 OnFired，
            // 一次投掷/挥击只应读作一个动作。快速武器跨帧开火时仍然每发都注入冲量。
            if (Time.frameCount == _lastRecoilFrame)
            {
                _lastFireTime = Time.time;
                return;
            }
            _lastRecoilFrame = Time.frameCount;

            // P35 每帧最多一次的动作触发：泼桶 8 滴/加特林补帧齐射只读作一次动作。
            TriggerWieldAction(config != null ? config.weaponKind : _currentWeaponKind);

            // 触发后坐力冲量：幅度按当前武器手感配置（P16），蓄力狙按蓄力刻度缩放（P28），
            // 并加入轻微随机 Yaw，否则连发时每一发都沿完全相同的轨迹回弹，看起来像机械臂。
            WeaponFeelParams feel = _weaponController != null
                ? _weaponController.CurrentFeel
                : WeaponFeelProfile.Resolve(config);
            float strength = WeaponFeelMath.ShotStrengthScale(
                _weaponController != null ? _weaponController.LastShotStrength01 : 1f);
            float omega = feel.SpringOmega;

            _recoilZSpring.Kick(-AnimSpring1D.ImpulseForPeak(
                feel.RecoilKickMeters * strength, omega, WeaponFeelMath.RecoilDampingRatio));

            float pitchPeak = (feel.RecoilPitchDegrees + UnityEngine.Random.Range(-0.6f, 0.6f)) * strength;
            _recoilPitchSpring.Kick(AnimSpring1D.ImpulseForPeak(
                pitchPeak, omega, WeaponFeelMath.RecoilDampingRatio));

            float yawPeak = UnityEngine.Random.Range(-0.9f, 0.9f) * strength;
            _recoilYawSpring.Kick(AnimSpring1D.ImpulseForPeak(
                yawPeak, omega, WeaponFeelMath.RecoilDampingRatio));

            // P26 待机微动作避让：刚开火不久站桩做小动作很出戏
            _lastFireTime = Time.time;
        }

        /// <summary>
        /// P35 开火动作触发（每帧最多一次）：小枪后坐、双枪左右交替、蓄力狙释放、泼桶甩出、爆破回坐。
        /// 滚筒走自己的四段时间线，不在此列。
        /// </summary>
        private void TriggerWieldAction(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Shooter:
                    _shooterKick01 = 1f;
                    break;

                case WeaponKind.Charger:
                    _chargerReleaseStrength = _weaponController != null
                        ? Mathf.Clamp01(_weaponController.LastShotStrength01)
                        : 1f;
                    _chargerReleaseTimer = WeaponWieldSolver.ChargerReleaseDuration;
                    break;

                case WeaponKind.Dualies:
                    _dualiesKickSign = -_dualiesKickSign;
                    _dualiesKick01 = 1f;
                    break;

                case WeaponKind.Slosher:
                    _slosherThrowTimer = Mathf.Max(0.01f, AnimConfig.slosherThrowDuration);
                    break;

                case WeaponKind.Blaster:
                    _blasterKickTimer = WeaponWieldSolver.BlasterKickDuration;
                    break;
            }
        }

        /// <summary>
        /// P35 通用持握/动作采样：把 WeaponController 的蓄力/扫射/翻滚/架枪/前摇状态与开火动作计时
        /// 汇总成纯数据姿态，供武器模型、挂点、胸口、果冻缩放四个写回点合成。
        /// 非滚筒武器每帧只做一次 switch + 少量三角/插值，零分配。
        /// </summary>
        private void UpdateWeaponWieldPose(float dt)
        {
            _wieldTime += dt;

            // 动作计时始终推进：切乌贼形态/切枪时不会把半截动作冻结在模型上。
            if (_shooterKick01 > 0f)
                _shooterKick01 = Mathf.MoveTowards(_shooterKick01, 0f, dt / WeaponWieldSolver.ShooterKickDuration);
            if (_dualiesKick01 > 0f)
                _dualiesKick01 = Mathf.MoveTowards(_dualiesKick01, 0f, dt / WeaponWieldSolver.DualiesKickDuration);
            if (_chargerReleaseTimer > 0f)
                _chargerReleaseTimer = Mathf.Max(0f, _chargerReleaseTimer - dt);
            if (_blasterKickTimer > 0f)
                _blasterKickTimer = Mathf.Max(0f, _blasterKickTimer - dt);
            if (_slosherThrowTimer > 0f)
                _slosherThrowTimer = Mathf.Max(0f, _slosherThrowTimer - dt);

            if (_currentWeaponKind == WeaponKind.Roller)
            {
                ClearWieldPose();
                return;
            }

            CharacterAnimConfigSO cfg = AnimConfig;
            WeaponWieldParams p = BuildWieldParams(cfg);

            float smooth = Mathf.Max(1f, p.SmoothRate);
            float weightK = 1f - Mathf.Exp(-smooth * dt);
            bool dodging = _weaponController != null && _weaponController.IsRollingDodge;
            bool turret = _weaponController != null && _weaponController.IsTurretLocked;
            bool streaming = _weaponController != null && _weaponController.IsStreaming;
            _dodgeWeight += ((dodging ? 1f : 0f) - _dodgeWeight) * weightK;
            _turretWeight += ((turret ? 1f : 0f) - _turretWeight) * weightK;
            _streamingWeight += ((streaming ? 1f : 0f) - _streamingWeight) * weightK;

            float windup01 = _weaponController != null ? _weaponController.Windup01 : 0f;
            float throw01 = cfg.slosherThrowDuration > 0.0001f
                ? Mathf.Clamp01(_slosherThrowTimer / cfg.slosherThrowDuration)
                : 0f;

            float fireKick01;
            switch (_currentWeaponKind)
            {
                case WeaponKind.Shooter:
                    fireKick01 = _shooterKick01;
                    break;
                case WeaponKind.Dualies:
                    fireKick01 = _dualiesKick01;
                    break;
                case WeaponKind.Blaster:
                    fireKick01 = WeaponWieldSolver.BlasterKickDuration > 0.0001f
                        ? Mathf.Clamp01(_blasterKickTimer / WeaponWieldSolver.BlasterKickDuration)
                        : 0f;
                    break;
                default:
                    fireKick01 = 0f;
                    break;
            }

            float release01 = WeaponWieldSolver.ChargerReleaseDuration > 0.0001f
                ? Mathf.Clamp01(_chargerReleaseTimer / WeaponWieldSolver.ChargerReleaseDuration)
                  * _chargerReleaseStrength
                : 0f;

            float side = dodging && _weaponController != null ? _weaponController.DodgeSide : 0f;
            float forwardSign = dodging && _weaponController != null ? _weaponController.DodgeForward : 1f;
            float dodgeSign = Mathf.Abs(side) > 0.2f ? Mathf.Sign(side) : 0f;

            WeaponWieldInput input = new WeaponWieldInput
            {
                Time = _wieldTime,
                Speed01 = Mathf.Clamp01(_planarSpeed / 6f),
                Charging = _weaponController != null && _weaponController.IsCharging,
                Charge01 = _weaponController != null ? _weaponController.Charge01 : 0f,
                Streaming = streaming,
                Streaming01 = _streamingWeight,
                Rolling = false,
                Dodge01 = _dodgeWeight,
                DodgeSign = dodgeSign,
                DodgeForwardSign = forwardSign,
                Turret01 = _turretWeight,
                Windup01 = windup01,
                Throw01 = throw01,
                FireKick01 = fireKick01,
                FireKickSign = _dualiesKickSign,
                Release01 = release01
            };

            WeaponWieldPose pose = WeaponWieldSolver.Sample(_currentWeaponKind, input, p);
            _wieldPitchDeg = pose.PitchDeg;
            _wieldYawDeg = pose.YawDeg;
            _wieldRollDeg = pose.RollDeg;
            _wieldDrop = pose.DropMeters;
            _wieldForward = pose.ForwardMeters;
            _wieldBodyPitchDeg = pose.BodyPitchDeg;
            _wieldBodyRollDeg = pose.BodyRollDeg;
            _wieldSink01 = pose.Sink01;
        }

        /// <summary>滚筒/切枪时清空通用动作层，防止上一把枪的蓄力/后坐残留在新枪上。</summary>
        private void ClearWieldPose()
        {
            _wieldPitchDeg = 0f;
            _wieldYawDeg = 0f;
            _wieldRollDeg = 0f;
            _wieldDrop = 0f;
            _wieldForward = 0f;
            _wieldBodyPitchDeg = 0f;
            _wieldBodyRollDeg = 0f;
            _wieldSink01 = 0f;
            _dodgeWeight = 0f;
            _turretWeight = 0f;
            _streamingWeight = 0f;
            _shooterKick01 = 0f;
            _dualiesKick01 = 0f;
            _chargerReleaseTimer = 0f;
            _chargerReleaseStrength = 1f;
            _slosherThrowTimer = 0f;
            _blasterKickTimer = 0f;
        }

        /// <summary>把 CharacterAnimConfigSO 的 P35 字段打包成纯函数参数（字段缺失时用默认值兜底）。</summary>
        private static WeaponWieldParams BuildWieldParams(CharacterAnimConfigSO cfg)
        {
            return new WeaponWieldParams
            {
                SmoothRate = Mathf.Max(1f, cfg.weaponWieldSmoothRate),
                IdleSwayDeg = Mathf.Max(0f, cfg.weaponIdleSwayDeg),
                IdleSwayHz = Mathf.Max(0.01f, cfg.weaponIdleSwayHz),
                ChargePullbackMeters = Mathf.Max(0f, cfg.weaponChargePullback),
                ChargeDropMeters = Mathf.Max(0f, cfg.weaponChargeDrop),
                ChargeBodyPitchDeg = Mathf.Max(0f, cfg.weaponChargeBodyPitchDeg),
                ChargeTremorDeg = Mathf.Max(0f, cfg.weaponChargeTremorDeg),
                SlosherCoilPitchDeg = Mathf.Max(0f, cfg.slosherCoilPitchDeg),
                SlosherThrowPitchDeg = Mathf.Max(0f, cfg.slosherThrowPitchDeg),
                SlosherThrowBodyPitchDeg = Mathf.Max(0f, cfg.slosherThrowBodyPitchDeg),
                BlasterKickPitchDeg = Mathf.Max(0f, cfg.blasterKickPitchDeg),
                BlasterKickBackMeters = Mathf.Max(0f, cfg.blasterKickBackMeters),
                DualiesDodgeBodyRollDeg = Mathf.Max(0f, cfg.dualiesDodgeBodyRollDeg),
                DualiesDodgeSink = Mathf.Clamp01(cfg.dualiesDodgeSink),
                DualiesTurretSink = Mathf.Clamp01(cfg.dualiesTurretSink),
                DualiesTurretBodyPitchDeg = Mathf.Max(0f, cfg.dualiesTurretBodyPitchDeg),
                SplatlingChargeDropMeters = Mathf.Max(0f, cfg.splatlingChargeDropMeters),
                SplatlingChargeBodyPitchDeg = Mathf.Max(0f, cfg.splatlingChargeBodyPitchDeg),
                SplatlingSpinMinDegPerSec = Mathf.Max(0f, cfg.splatlingSpinMinDegPerSec),
                SplatlingSpinMaxDegPerSec = Mathf.Max(0f, cfg.splatlingSpinMaxDegPerSec),
                SplatlingSpinCoastRate = Mathf.Max(0f, cfg.splatlingSpinCoastRate),
                SplatlingStreamShakeDeg = Mathf.Max(0f, cfg.splatlingStreamShakeDeg),
                ShooterFireBodyPitchDeg = Mathf.Max(0f, cfg.shooterFireBodyPitchDeg),
                ShooterFirePullbackMeters = Mathf.Max(0f, cfg.shooterFirePullbackMeters)
            };
        }

        public void EquipWeaponKind(WeaponKind kind)
        {
            _currentWeaponKind = kind;
            CacheHeldGripTargets(kind);
            ClearWieldPose();
            _splatlingSpinDegPerSec = 0f;
            _rollerSprayTimer = 0f;

            // P28 切枪复位后坐力弹簧，避免上一把枪的残余位移带到新枪
            _recoilZSpring.Reset();
            _recoilPitchSpring.Reset();
            _recoilYawSpring.Reset();
            _lastRecoilFrame = -1;

            // P27 切枪复位滚筒动画状态（时间线/拖地权重立即归零，滚轴角速度保留惯性自然滑停）
            _rollerFlickActive = false;
            _rollerFlickTime = 0f;
            _prevFlicking = false;
            _rollerRollWeight = 0f;

            foreach (var kvp in _weaponMap)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.SetActive(kvp.Key == kind);
                }
            }

            if (_muzzleMap.TryGetValue(kind, out Transform m) && m != null)
            {
                _activeMuzzle = m;
            }
            else
            {
                _activeMuzzle = muzzleShooter != null ? muzzleShooter : transform;
            }

            if (_weaponController != null && _activeMuzzle != null)
            {
                _weaponController.SetMuzzleTransform(_activeMuzzle);
            }

            // P34 手臂持握姿态：每把武器在装配期已把"握把 → 手位"对齐并用两骨 IK 反解出肩/肘角度，
            // 切枪时整套切换（双枪左肩外展 + 左手持枪；其余武器左手自然垂下托握/随身）。
            // 表缺失（旧场景/未见装配器）时退回旧的位置 hack + authored 角度。
            if (_armHoldPoses.TryGetValue(kind, out ArmHoldPose holdPose))
            {
                if (rightArmTransform != null)
                {
                    rightArmTransform.localPosition = holdPose.RightShoulderLocalPos;
                    rightArmTransform.localRotation = holdPose.RightShoulder;
                }
                if (rightElbowPivot != null) rightElbowPivot.localRotation = holdPose.RightElbow;
                if (leftArmTransform != null)
                {
                    leftArmTransform.localPosition = holdPose.LeftShoulderLocalPos;
                    leftArmTransform.localRotation = holdPose.LeftShoulder;
                }
                if (leftElbowPivot != null) leftElbowPivot.localRotation = holdPose.LeftElbow;

                _rightArmPoseRot = holdPose.RightShoulder;
                _rightElbowPoseRot = holdPose.RightElbow;
                _leftArmWeaponPoseRot = holdPose.LeftShoulder;
                _leftElbowPoseRot = holdPose.LeftElbow;
            }
            else if (leftArmTransform != null)
            {
                if (kind == WeaponKind.Dualies)
                {
                    leftArmTransform.localPosition = _defaultLeftArmPos + new Vector3(-0.06f, 0.00f, -0.04f);
                    _leftArmWeaponPoseRot = Quaternion.identity;
                }
                else
                {
                    // 单持武器：左手自然垂在身侧，不再横到胸前（旧姿势会在奶油白身体上留下一个肤色圆点）
                    leftArmTransform.localPosition = _defaultLeftArmPos + new Vector3(-0.03f, 0.00f, 0.02f);
                    _leftArmWeaponPoseRot = Quaternion.Euler(4f, -6f, -2f);
                }
                leftArmTransform.localRotation = _leftArmWeaponPoseRot;
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            SampleMotion(dt);
            UpdateFormMorph(dt);
            UpdateRollerPose(dt);   // P27 滚筒时间线先算帧值，供下方武器/身体写回段合成
            UpdateWeaponWieldPose(dt); // P35 其余武器的持握/蓄力/翻滚/泼洒动作帧值
            UpdateAimPose(dt);
            UpdateFlourish(dt);

            if (_formAmount < 0.99f)
            {
                UpdateBodyPose(dt);
                UpdateHairSway(dt);
                UpdateRecoilKick(dt);
                UpdateTankFill();
            }
            else
            {
                // 乌贼潜行姿态：身体起伏 + 转向侧倾 + 保体积拉伸 + 逐根触须行波
                UpdateSwimPosture(dt);
            }

            UpdateWeaponMechanics(dt);
            ApplyRollerGroundContact(dt);   // P31 最后一步：所有姿态写完后按真实地面反解滚筒俯仰
            ApplyHeldHandTargets();         // P34 手持 IK：手贴握把（含瞄准/后坐力/滚筒动画位移）
        }

        /// <summary>
        /// 全形态共享的运动采样（每帧一次）：真实位移速度 / 局部速度 / 偏航角速度 / 着地 / 传送跳变。
        /// 潜行期同样计算——游动姿态的起伏频率、触须摆幅、转向侧倾都依赖真实速度与 yawRate；
        /// 关键：速度必须用真实位移而不是 motor.CurrentMoveSpeed（那是形态速度上限，站立时也为 6）。
        /// </summary>
        private void SampleMotion(float dt)
        {
            Vector3 curPos = transform.position;
            Vector3 worldVel = (curPos - _prevWorldPos) / Mathf.Max(0.0001f, dt);

            // 传送/复活导致的速度尖峰（> 50 m/s）：直接复位，避免误判为超高速起跳/落地
            _teleportedThisFrame = worldVel.sqrMagnitude > 2500f;
            if (_teleportedThisFrame)
            {
                worldVel = Vector3.zero;
                _verticalVel = 0f;
                _lastAirVelY = 0f;
            }
            else
            {
                _verticalVel = worldVel.y;
            }

            _prevWorldPos = curPos;
            _planarSpeed = new Vector2(worldVel.x, worldVel.z).magnitude;
            _localVel = transform.InverseTransformDirection(worldVel);

            float curYaw = transform.eulerAngles.y;
            _yawRate = Mathf.DeltaAngle(_prevYaw, curYaw) / Mathf.Max(0.0001f, dt);
            _prevYaw = curYaw;

            _groundedNow = _motor == null || _motor.IsGrounded;

            // P26 局部加速度（惯性倾斜/骨盆滞后弹簧的输入）：
            // 无水平加速度曲线，起步/刹车帧差分会非常尖峰，这里只钳制量程，平滑交给下游弹簧
            if (_teleportedThisFrame)
            {
                _localAccel = Vector3.zero;
            }
            else
            {
                float accelClamp = AnimConfig.leanAccelClamp;
                Vector3 rawAccel = (_localVel - _prevLocalVel) / Mathf.Max(0.0001f, dt);
                rawAccel.x = Mathf.Clamp(rawAccel.x, -accelClamp, accelClamp);
                rawAccel.z = Mathf.Clamp(rawAccel.z, -accelClamp, accelClamp);
                rawAccel.y = 0f;
                _localAccel = rawAccel;
            }
            _prevLocalVel = _localVel;
        }

        /// <summary>
        /// 人形态 ⇄ 乌贼形态弹性手势（Squash & Stretch）：
        /// AnimSpring1D 驱动形态标量 f，kid/squid 的缩放从 SO 四条 AnimationCurve 求值
        /// （曲线过冲区编码表演：f&lt;0 浮出拉伸、f&gt;1 潜入回稳摆动），缩放严格基于默认值乘算，绝不每帧累乘。
        /// 数值来自 INKWAVE character.js L383-386/1342-1358：双向约 0.42s、潜入压扁 Y→0.16、
        /// 浮出拉伸 Y≈1.36、乌贼钻出过冲 1.28 后回稳。
        /// </summary>
        private void UpdateFormMorph(float dt)
        {
            CharacterAnimConfigSO cfg = AnimConfig;
            // 乌贼形态（含干地乌贼）都走 squid 形变与姿态管线；是否沉入墨面由 IsSwimming 单独判定
            bool wantsSwim = _motor != null && _motor.IsInSquidForm;

            // 形态翻转边沿：触发表演性点缀（跳水甩臂 / 亮相甩臂），让切换有生命力而不是淡入淡出
            if (wantsSwim != _isCurrentlySwimming)
            {
                _isCurrentlySwimming = wantsSwim;
                TriggerFormFlourish(wantsSwim);
            }

            _formSpring.Step(wantsSwim ? 1f : 0f, cfg.formSpringOmega, cfg.formSpringZeta, dt);
            float f = Mathf.Clamp(_formSpring.Value, cfg.formAmountMin, cfg.formAmountMax);
            _formAmount = f;

            if (kidRoot != null)
            {
                bool kidVisible = f < cfg.kidHideFormAmount;
                kidRoot.SetActive(kidVisible);
                if (kidVisible)
                {
                    float ft = Mathf.Clamp01(f);
                    float sy = EvalCurve(cfg.kidScaleY, f, Mathf.Lerp(1f, 0.15f, ft));
                    float sxz = EvalCurve(cfg.kidScaleXZ, f, Mathf.Lerp(1f, 1.25f, ft));
                    kidRoot.transform.localScale = Vector3.Scale(_defaultKidRootScale, new Vector3(sxz, sy, sxz));
                }
            }

            if (squidRoot != null)
            {
                bool squidVisible = f > cfg.squidShowFormAmount;
                squidRoot.SetActive(squidVisible);
                if (squidVisible)
                {
                    float fallbackPop = Mathf.SmoothStep(0.2f, 1f, Mathf.Clamp01(f));
                    float sy = EvalCurve(cfg.squidScaleY, f, fallbackPop);
                    float sxz = EvalCurve(cfg.squidScaleXZ, f, fallbackPop);
                    squidRoot.transform.localScale = Vector3.Scale(_defaultSquidRootScale, new Vector3(sxz, sy, sxz));
                }
            }

            if (squidBodyTransform != null)
            {
                if (f > cfg.squidShowFormAmount)
                {
                    // 潜行贴地：随形态进度沉入墨面（半潜观感，只露背脊与眼睛）；
                    // 仅在真正潜入己方墨水（IsSwimming）时下沉 —— 干地乌贼趴在地面，不往地里钻；
                    // 姿态（起伏/侧倾/拉伸）由 UpdateSwimPosture 写入
                    bool submerged = _motor != null && _motor.IsSwimming;
                    float submergeDepth = submerged ? cfg.squidSubmergeDepth * Mathf.Clamp01(f) : 0f;
                    squidBodyTransform.localPosition = _squidBodyDefaultPos
                        + Vector3.down * submergeDepth;
                }
                else
                {
                    // 人形态期间恢复默认位姿，保证下一次潜入从同一基准展开
                    squidBodyTransform.localPosition = _squidBodyDefaultPos;
                    squidBodyTransform.localRotation = _defaultSquidBodyRot;
                    squidBodyTransform.localScale = _defaultSquidBodyScale;
                }
            }

            if (weaponRoot != null)
            {
                // 迟滞：潜入时 f 一到高位立即藏枪，浮出时 f 回落到低位才亮枪，
                // 防止弹簧过冲在阈值边界反复闪烁
                if (_isCurrentlySwimming && f >= cfg.weaponHideFormAmount) _weaponVisuallyHidden = true;
                else if (!_isCurrentlySwimming && f <= cfg.weaponShowFormAmount) _weaponVisuallyHidden = false;
                weaponRoot.SetActive(!_weaponVisuallyHidden);
            }
        }

        /// <summary>曲线求值兜底：设计器误清空曲线时退回旧版线性语义，保证动画不崩。</summary>
        private static float EvalCurve(AnimationCurve curve, float t, float fallback)
        {
            return curve != null && curve.length >= 2 ? curve.Evaluate(t) : fallback;
        }

        /// <summary>形态翻转瞬间的表演性点缀：甩臂 + 果冻弹簧冲量 + 墨焰惯性甩动。</summary>
        private void TriggerFormFlourish(bool toSquid)
        {
            CharacterAnimConfigSO cfg = AnimConfig;
            float duration;
            if (toSquid)
            {
                _flingArmDeg = cfg.diveArmFlingDeg;
                duration = cfg.diveArmFlingDuration;
                _jellyVel += cfg.diveJellyKick;
            }
            else
            {
                _flingArmDeg = cfg.emergeArmFlingDeg;
                duration = cfg.emergeArmFlingDuration;
                _jellyVel += cfg.emergeJellyKick;
            }
            _flingArmRate = _flingArmDeg / Mathf.Max(0.05f, duration);
            _flingHairPitchDeg = cfg.formHairKickDeg;
            _flingHairRollDeg = cfg.formHairKickDeg * 0.6f;
            _flingHairRate = cfg.formHairKickDeg / 0.30f;
        }

        /// <summary>表演点缀通道的线性衰减（MoveTowards 到 0，时长确定、可单测）。</summary>
        private void UpdateFlourish(float dt)
        {
            if (_flingArmDeg > 0f)
            {
                _flingArmDeg = Mathf.MoveTowards(_flingArmDeg, 0f, _flingArmRate * dt);
            }
            if (_flingHairPitchDeg > 0f)
            {
                _flingHairPitchDeg = Mathf.MoveTowards(_flingHairPitchDeg, 0f, _flingHairRate * dt);
                _flingHairRollDeg = Mathf.MoveTowards(_flingHairRollDeg, 0f, _flingHairRate * 0.6f * dt);
            }

            // P26 落地表演冲量衰减：双臂下甩 ~0.2s、胸口前倾 ~0.15s 弹回
            _landArmKickDeg = Mathf.MoveTowards(_landArmKickDeg, 0f, 60f * dt);
            _landPitchKickDeg = Mathf.MoveTowards(_landPitchKickDeg, 0f, 40f * dt);
        }

        /// <summary>
        /// 乌贼游动姿态（潜行期替代人形态管线，数值对齐 INKWAVE character.js L3550-3611）：
        /// 身体起伏波（频率 5→14 rad/s 随速度）+ 转向侧倾 banking + 保体积速度拉伸（Z 1+0.2·sv，XZ 1/√）
        /// + 头部点摆 + 逐根触须行波。
        /// </summary>
        private void UpdateSwimPosture(float dt)
        {
            CharacterAnimConfigSO cfg = AnimConfig;
            float sv = SwimPoseSolver.Speed01(
                _planarSpeed,
                cfg.ResolveSwimRefSpeed(_motor != null ? _motor.Config : null));

            SwimPoseSolver.SwimPose pose = SwimPoseSolver.Sample(sv, Time.time, _yawRate, cfg);

            // 侧倾走平滑，防角速度尖峰直接上身
            _swimBankCurrent = Mathf.SmoothDamp(_swimBankCurrent, pose.RollDeg, ref _swimBankVelocity, 0.08f, 120f, dt);

            if (squidBodyTransform != null)
            {
                squidBodyTransform.localRotation = _defaultSquidBodyRot
                    * Quaternion.Euler(pose.PitchDeg, pose.SwayDeg, _swimBankCurrent);
                // 保体积拉伸：沿游进方向拉长、横向收缩（INKWAVE sy=1+0.2sv, sxz=1/√sy）
                squidBodyTransform.localScale = Vector3.Scale(
                    _defaultSquidBodyScale,
                    new Vector3(pose.StretchXZ, pose.StretchXZ, pose.StretchZ));
            }

            UpdateSwimTentacles(sv);

            if (squidTentaclesRoot != null && (_squidTentacles == null || _squidTentacles.Length == 0))
            {
                // 未绑定逐根触须时的兜底：根节点整体 Y 轴摆动（旧行为）
                float wave = Mathf.Sin(Time.time * 16f) * 12f * sv;
                squidTentaclesRoot.localRotation = _squidTentaclesRootBaseRot * Quaternion.Euler(0f, wave, 0f);
            }

            _wasGrounded = _groundedNow;
        }

        /// <summary>4 根拖尾触须逐根驱动：相邻相位差近似根→尖行波（INKWAVE 顶点着色器行波的无蒙皮近似）。</summary>
        private void UpdateSwimTentacles(float sv)
        {
            if (_squidTentacles == null || _squidTentacles.Length == 0) return;

            CharacterAnimConfigSO cfg = AnimConfig;
            float t = Time.time;
            for (int i = 0; i < _squidTentacles.Length; i++)
            {
                Transform tentacle = _squidTentacles[i];
                if (tentacle == null) continue;
                SwimPoseSolver.TentacleFlap(sv, t, i, cfg, out float flapDeg, out float sideDeg);
                tentacle.localRotation = _squidTentacleBaseRots[i] * Quaternion.Euler(flapDeg, 0f, sideDeg);
            }
        }

        /// <summary>
        /// 无腿蛋形角色程序化动作管线核心：
        /// 1. 速度与着地边沿采样（含传送跳变重置）；
        /// 2. 贴地步态（KidGaitSolver 采样轻微起伏 Bob、左右摇摆 Rock、少量挤压 Squash、手臂反相摆动 ArmSwing）；
        /// 3. 果冻弹簧（起跳瞬间拉伸、真正有下落速度的落地才压扁缓冲、滞空仰俯）；
        /// 4. 躯干前倾与转弯侧向倾斜（针对贴地枢轴下调角度倍率的 Lean Springs）；
        /// 5. 待机呼吸与实例异步自然眨眼（呼吸只做缩放，不做位移）；
        /// 6. 踩敌方墨水高频挣扎抖动；
        /// 7. 墨墙攀爬蠕动；
        /// 8. 姿态与挤压写回（所有形变全部作用于 bodySquashTransform 节点，严禁缩放 Chest 以免武器变形）。
        /// </summary>
        private void UpdateBodyPose(float dt)
        {
            // --- 1. 速度与着地采样（SampleMotion 每帧已算好；传送跳变在此复位果冻弹簧） ---
            bool grounded = _groundedNow;
            if (_teleportedThisFrame)
            {
                _jelly = 0f;
                _jellyVel = 0f;
                _lastAirVelY = 0f;
            }
            // 关键：步态必须用真实位移速度，而不是 motor.CurrentMoveSpeed（那是当前形态的速度上限，站立时也为 6）。
            // 旧写法导致角色一落地就永远在走弹跳步态，原地站着也一蹦一蹦。
            float speed = _planarSpeed;
            Vector3 localVel = _localVel;
            float yawRate = _yawRate;
            float speed01 = KidGaitSolver.Speed01Run(speed);
            CharacterAnimConfigSO cfg = AnimConfig;
            bool isWallClimbing = _motor != null && _motor.IsWallClimbing;

            // 步态权重：0.25 m/s 起进入步态，SmoothStep 起步/停步柔和；静止不触发持续原地起伏。
            float gaitWeight = grounded
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((speed - KidGaitSolver.GaitMinSpeed) / 1.4f))
                : 0f;
            // 步向衰减：横移/后退时前后摆收小（侧移由半侧转+侧倾表达），bob/squash 全幅保留。
            float forwardness = speed > 0.1f ? localVel.z / speed : 0f;
            float swingScale = KidGaitSolver.DirectionalSwingScale(forwardness);
            if (gaitWeight > 0.02f)
            {
                // 防滑核心（峰值匹配律）：步频 = speed/(π·L·ampEff)，支撑中点脚相对身体后扫速度
                // 恰好等于身体速度 → 支撑中段脚世界钉地。真腿长 0.433m 下同速步频约为旧蛋腿一半，
                // 跑姿从"高频碎步"回到正常步幅节奏。
                float ampEffRad = Mathf.Lerp(cfg.kidLegSwingSlowDeg, cfg.kidLegSwingFastDeg, speed01)
                    * swingScale * Mathf.Deg2Rad;
                _gaitPhase += Mathf.PI * KidGaitSolver.CadenceFromGait(speed, ampEffRad, cfg.kidLegLength) * dt;
            }

            // 步态全过程参数化：步幅/摆幅/脚尖/腿长/脊柱分配全部来自 CharacterAnimConfigSO
            KidGaitParams gaitParams = new KidGaitParams
            {
                BobSlow = cfg.kidBobSlow,
                BobFast = cfg.kidBobFast,
                SquashDepth = cfg.kidSquashDepth,
                SquashBulge = cfg.kidSquashBulge,
                RockSlowDeg = cfg.kidRockSlowDeg,
                RockFastDeg = cfg.kidRockFastDeg,
                ArmSwingSlowDeg = cfg.kidArmSwingSlowDeg,
                ArmSwingFastDeg = cfg.kidArmSwingFastDeg,
                LegSwingSlowDeg = cfg.kidLegSwingSlowDeg,
                LegSwingFastDeg = cfg.kidLegSwingFastDeg,
                KneeBendSlowDeg = cfg.kidKneeBendSlowDeg,
                KneeBendFastDeg = cfg.kidKneeBendFastDeg,
                ToeUpDeg = cfg.kidToeUpDeg,
                ToeOffDeg = cfg.kidToeOffDeg,
                LegLength = cfg.kidLegLength,
                ElbowBendBaseDeg = cfg.kidElbowBendBaseDeg,
                ElbowBendFastDeg = cfg.kidElbowBendFastDeg,
                ElbowSwingGainDeg = cfg.kidElbowSwingGainDeg,
                LeanSlowDeg = cfg.kidLeanSlowDeg,
                LeanFastDeg = cfg.kidLeanFastDeg,
                HipLeanShare = cfg.kidHipLeanShare,
                SpineLeanShare = cfg.kidSpineLeanShare,
                ChestLeanShare = cfg.kidChestLeanShare
            };
            KidGaitPose gait = KidGaitSolver.SampleGait(speed01, _gaitPhase, gaitParams);

            // P29 脚步锁相：相位跨过触地点（φ=π/2 左脚、3π/2 右脚）发一次脚步事件
            if (gaitWeight > 0.3f && !isWallClimbing)
            {
                int stepIndex = Mathf.FloorToInt((_gaitPhase - Mathf.PI * 0.5f) / Mathf.PI);
                if (stepIndex != _lastStepIndex)
                {
                    if (_lastStepIndex != int.MinValue && stepIndex > _lastStepIndex && speed > 0.4f)
                    {
                        FootstepLanded?.Invoke(((stepIndex % 2) + 2) % 2 == 0);
                    }
                    _lastStepIndex = stepIndex;
                }
            }
            else
            {
                // 静止/离地/攀墙时对齐索引，重新起步不补发上一次的脚步声
                _lastStepIndex = int.MinValue;
            }

            // 骨盆竖向：几何下沉 + 步频起伏已合成在 gait.BobY；按 kidHipDropMaxMeters 封顶
            // —— 大摆幅高频帧的全幅下沉会读成上下抖动，残余脚底悬浮 ≤2cm 由鞋底与接触阴影掩盖。
            float bob = Mathf.Max(gait.BobY * gaitWeight, -cfg.kidHipDropMaxMeters);
            float squashY = Mathf.Lerp(1f, gait.SquashY, gaitWeight);
            float squashXZ = Mathf.Lerp(1f, gait.SquashXZ, gaitWeight);
            float rock = gait.RockDeg * gaitWeight;
            // 步态摆臂 + 形态切换表演甩臂（潜入高举 / 浮出弹开，线性衰减）
            float armSwing = gait.ArmSwingDeg * gaitWeight * swingScale + _flingArmDeg;

            // --- 2.5 双腿 FK 步态（左右腿独立相位）：摆动腿屈膝抬脚，支撑腿伸直踩地 ---
            float leftThigh = gait.LeftThighDeg * gaitWeight * swingScale;
            float rightThigh = gait.RightThighDeg * gaitWeight * swingScale;
            float leftKnee = gait.LeftKneeDeg * gaitWeight * swingScale;
            float rightKnee = gait.RightKneeDeg * gaitWeight * swingScale;
            float leftFoot = gait.LeftFootPitchDeg * gaitWeight * swingScale;
            float rightFoot = gait.RightFootPitchDeg * gaitWeight * swingScale;

            // --- 2.6 脊柱链步态前倾源值（INKWAVE runPose 的髋/腰/胸分配，随速度加深）---
            float leanHip = gait.HipPitchDeg * gaitWeight;
            float leanSpine = gait.SpinePitchDeg * gaitWeight;
            float leanChest = gait.ChestPitchDeg * gaitWeight;

            // --- 3. 起跳 / 滞空 / 落地（果冻弹簧与滞空仰俯；P26 按 INKWAVE _poseAir 重做表演分段） ---
            if (_wasGrounded && !grounded)
            {
                // 着地 -> 离地：起跳瞬间向上拉伸（P26 冲量加大强化弹射感），记录水平速度供跑步起跳劈腿
                _jellyVel += cfg.airLaunchStretchImpulse;
                _jumpRunSpeed = speed;
            }
            else if (!_wasGrounded && grounded)
            {
                // 离地 -> 着地：只有真正下落（不是贴地被 isGrounded 抖动）才触发压扁缓冲，
                // 否则角色站着会每隔几帧被弹簧弹一下，看起来像原地蹦。
                float fallSpeed = Mathf.Max(0f, -_lastAirVelY);
                if (fallSpeed > 1.2f)
                {
                    // P26 落地三段·触发：弹簧冲量按落地速度归一 + 屈膝保持窗 + 双臂下甩/胸口前倾冲量
                    _landSquash01 = Mathf.Clamp01(fallSpeed / 10f);
                    _jellyVel -= Mathf.Lerp(cfg.landImpulseMin, cfg.landImpulseMax, Mathf.Clamp01(fallSpeed / 14f));
                    _landTime = 0f;
                    _landArmKickDeg = cfg.landArmDropDeg * _landSquash01;
                    _landPitchKickDeg = cfg.landChestPitchDeg * _landSquash01;
                    if (fallSpeed > cfg.landHardBlinkSpeed)
                    {
                        TriggerBlink();
                    }

                    // P29 落地墨花：与视觉压扁同帧触发（装饰性反馈失败绝不阻断动画，也不触发 PlayMode 日志门禁）
                    if (fallSpeed >= cfg.landVfxMinFallSpeed)
                    {
                        try
                        {
                            if (VfxPool.Instance != null && InkSurfaceController.Instance != null && _motor != null)
                            {
                                Color landColor = InkSurfaceController.Instance.GetTeamColor(_motor.Team);
                                VfxPool.Instance.SpawnInkSplat(
                                    transform.position, landColor, cfg.landVfxSize,
                                    Mathf.Clamp(Mathf.RoundToInt(fallSpeed), 4, 12));
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[CharacterVisual] 落地墨花 VFX 失败（不影响动作）: {e.Message}");
                        }
                    }
                }
                else
                {
                    // 轻落地（贴地抖动级）：清保持强度，防止残留上一次重落地的屈膝保持窗
                    _landSquash01 = 0f;
                }
            }
            _wasGrounded = grounded;
            // 滞空计时（供收腿淡入）：着地立即清零；落地保持窗计时（封顶防无限增长）
            if (grounded)
            {
                _airTime = 0f;
                if (_landTime < 1f) _landTime += dt;
            }
            else
            {
                _airTime += dt;
            }

            float airPitch = 0f;
            KidAirPose air = KidAirPose.Neutral;
            if (!grounded)
            {
                _lastAirVelY = _verticalVel;
                // 滞空期间：上升抬头、下落低头
                airPitch = Mathf.Clamp(-_verticalVel * 1.2f, -10f, 10f);
                // 滞空四权重：起跳蹬伸 / 顶点悬停 / 下落够地 / 快速下落拉伸
                air = KidGaitSolver.SampleAir(_verticalVel, _airTime, _jumpRunSpeed,
                    new KidAirParams
                    {
                        ApexSoften = cfg.airApexSoften,
                        FallStretch = cfg.airFallStretch,
                        ReachThighDeg = cfg.airReachThighDeg,
                        RunSplitThighDeg = cfg.airRunSplitThighDeg
                    });
            }

            // 滞空团身时序分配（参考 INKWAVE _poseAir）：
            // 起跳瞬间腿还伸着（launch）→ 中段团身（tuck）→ 下落后期伸腿够地（reach）
            float airTuck = grounded ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_airTime / 0.12f));
            float tuckW = airTuck * (1f - air.LaunchW) * (1f - air.ReachW);
            float tuckThigh = -KidGaitSolver.AirTuckThighDeg * tuckW + air.ReachW * cfg.airReachThighDeg;
            float tuckKnee = KidGaitSolver.AirTuckKneeDeg * tuckW * (1f + air.ApexW * 0.25f);
            float tuckFoot = KidGaitSolver.AirTuckFootDeg * tuckW * (1f - air.ReachW);

            // 二阶果冻弹簧模拟 (Jelly Spring: K=260, D=18)
            const float JellyK = 260f;
            const float JellyD = 18f;
            _jellyVel += (-JellyK * _jelly - JellyD * _jellyVel) * dt;
            _jelly += _jellyVel * dt;
            _jelly = Mathf.Clamp(_jelly, -0.22f, 0.16f);
            // P26 滞空挤压修正：快速下落再拉伸、顶点微团身缩（贴地时 air 为 Neutral，不修正）
            squashY *= air.SquashYMul;
            // P27/P35 重心下沉：滚筒拖地 + 双枪翻滚/架枪/加特林蓄力共用同一竖向缩放通道
            squashY *= 1f - Mathf.Clamp01(_rollerSink01 + _wieldSink01);

            // --- 4. 前倾与侧倾（速度层 Lean Springs） ---
            // P33 真骨骼：俯仰枢轴从"贴地整蛋"回到腰/胸骨，倾斜不再被枢轴放大，
            // 幅度回到正常跑姿量级（INKWAVE 实测 6m/s 整体前倾 ~0.3rad≈17°，此处取保守上限 14°）。
            float targetPitch = Mathf.Clamp(localVel.z * 1.1f, -6f, 14f);
            float targetRoll = Mathf.Clamp(-yawRate * 0.12f - localVel.x * 1.0f, -10f, 10f);

            _currentPitch = Mathf.SmoothDamp(_currentPitch, targetPitch, ref _pitchVelocity, 0.08f, 100f, dt);
            _currentRoll = Mathf.SmoothDamp(_currentRoll, targetRoll, ref _rollVelocity, 0.08f, 100f, dt);

            // P26 惯性倾斜弹簧层（加速度层）：起步前倾冲劲/刹车后仰/侧移压弯 + 骨盆滞后位移
            UpdateLeanSprings(dt, grounded, isWallClimbing, cfg);

            // P36 受击弹簧回稳：目标恒 0，冲量只在 DamageApplied 事件里注入
            float flinchOmega = 2f * Mathf.PI * Mathf.Max(1f, cfg.flinchSpringHz);
            _flinchPitchSpring.Step(0f, flinchOmega, cfg.flinchSpringZeta, dt);
            _flinchRollSpring.Step(0f, flinchOmega, cfg.flinchSpringZeta, dt);

            // --- 5. 待机生命感（P26）：呼吸节奏起伏 / 重心转移 / 偶发小动作 ---
            bool idle = grounded && speed < KidGaitSolver.GaitMinSpeed && !isWallClimbing;
            float breath = UpdateIdleBreath(dt, idle, cfg);
            squashY += breath * cfg.idleBreathSquash;
            squashXZ -= breath * cfg.idleBreathSquash * 0.5f;
            bob += breath * cfg.idleBreathBob;
            float breathPitch = breath * cfg.idleBreathPitchDeg;

            float shift = UpdateIdleShift(dt, idle, cfg);
            rock += shift * cfg.idleShiftRockDeg;
            float shiftSway = shift * cfg.idleShiftSwayMeters;

            UpdateIdleFidget(dt, idle, cfg, ref rock);

            UpdateBlink(dt);
            UpdateEyeLook(dt, cfg, idle);

            // --- 6. 踩敌墨高频受击抖动 ---
            float grip = _motor != null ? _motor.EnemyInkGrip01 : 0f;
            if (grip > 0.001f)
            {
                rock += Mathf.Sin(Time.time * 38f) * grip * 3.5f;
                squashY -= grip * 0.05f;
            }

            // --- 7. 墨墙攀爬小幅蠕动 ---
            if (isWallClimbing)
            {
                // 攀爬时步态权重强制为 0，杜绝在垂直墙面上横向乱弹；改为 4Hz 垂直蠕动
                bob = 0f;
                rock = 0f;
                armSwing = 0f;
                leanHip = 0f;
                leanSpine = 0f;
                leanChest = 0f;
                squashY = 1f + Mathf.Sin(Time.time * 22f) * 0.02f;
                squashXZ = 1f;
                // 双腿收拢悬挂，不再摆动（避免吊挂时腿随步态乱蹬）
                leftThigh = 0f;
                rightThigh = 0f;
                leftKnee = 0f;
                rightKnee = 0f;
                leftFoot = 0f;
                rightFoot = 0f;
                airTuck = 0f;
                tuckThigh = -10f;
                tuckKnee = KidGaitSolver.WallClimbTuckKneeDeg;
                tuckFoot = 8f;
            }

            // --- 8. 骨骼链写回（P33 真骨骼版）---
            // 8.1 全身挤压（果冻/滞空/滚筒/待机）只作用 BodySquash；武器挂胸腔骨之下会跟着被缩放，
            //     在武器根逐帧做等量反缩放抵消 —— 枪永远不压扁（对齐 INKWAVE：skid 形变不进武器骨骼）。
            float sy = Mathf.Clamp(squashY * (1f + _jelly), 0.55f, 1.40f);
            float sxz = Mathf.Clamp(squashXZ * (1f - _jelly * 0.5f), 0.60f, 1.35f);
            if (bodySquashTransform != null)
            {
                bodySquashTransform.localPosition = _defaultBodySquashPos + new Vector3(shiftSway, 0f, 0f);
                // 严格基于 _defaultBodySquashScale 乘算，绝不每帧累乘
                bodySquashTransform.localScale = Vector3.Scale(_defaultBodySquashScale, new Vector3(sxz, sy, sxz));
            }
            if (weaponRoot != null)
            {
                weaponRoot.transform.localScale = Vector3.Scale(
                    _defaultWeaponRootScale, new Vector3(1f / sxz, 1f / sy, 1f / sxz));
            }

            // 8.2 横移半侧转平滑：只在贴地行走时生效，滞空/攀墙回正（保持瞄准姿态）
            float strafeTarget = grounded && !isWallClimbing
                ? KidGaitSolver.StrafeYawTargetDeg(localVel, speed, gaitWeight)
                : 0f;
            _strafeYawCurrent = Mathf.SmoothDamp(
                _strafeYawCurrent, strafeTarget, ref _strafeYawVelocity,
                Mathf.Max(0.01f, cfg.strafeYawSmoothTime), 360f, dt);

            // 8.3 动态俯仰源（速度层 + 加速度弹簧 + 滞空仰俯 + 瞄准分担 + 落地冲量 + 滚筒身体）
            PlayerConfigSO poseConfig = _motor != null ? _motor.Config : null;
            float chestAimWeight = poseConfig != null
                ? Mathf.Clamp01(poseConfig.aimPoseChestPitchWeight)
                : 0.18f;
            float dynPitch = _currentPitch + _leanPitchSpring.Value + airPitch
                + _aimPitchCurrent * chestAimWeight + _landPitchKickDeg
                + _rollerBodyPitchDeg     // 滚筒蓄力后仰/砸地前压/拖地前倾
                + _wieldBodyPitchDeg      // P35 蓄力后仰/泼桶甩出/爆破后坐/架枪前压
                + _flinchPitchSpring.Value; // P36 受击后仰顿挫
            float dynRoll = _currentRoll + _leanRollSpring.Value + _wieldBodyRollDeg
                + _flinchRollSpring.Value; // P36 受击侧让顿挫
            float rollMix = dynRoll + rock * 0.6f;

            // 8.4 Hips：骨盆竖向起伏（贴地几何+步频起伏，含惯性滞后）+ 侧转主轴 + 25% 步态前倾 + 部分侧倾
            //     腿挂在 Hips 下随骨盆自然带向移动方向 —— 蛋形版"胸口反补偿髋部"的解耦补丁不再需要。
            if (hipsTransform != null)
            {
                hipsTransform.localPosition = _defaultHipsPos
                    + Vector3.up * (bob * cfg.hipsBobShare)
                    + new Vector3(_hipsLagSpringX.Value, 0f, _hipsLagSpringZ.Value);
                hipsTransform.localRotation = _defaultHipsRot
                    * Quaternion.Euler(leanHip, _strafeYawCurrent * 0.55f, rollMix * 0.35f);
            }

            // 8.5 Spine（45% 步态前倾 + 动态层一部分）与 Chest（30% + 一部分）两级折叠：
            //     上半身在腰带处弯一半、胸口再压一半 —— 蛋形"整块板翻"僵硬感的根治。
            float spinePitch = leanSpine + dynPitch * cfg.kidSpineDynamicShare + breathPitch * 0.35f;
            float chestPitch = leanChest + dynPitch * cfg.kidChestDynamicShare + breathPitch * 0.5f;
            if (spineTransform != null)
            {
                spineTransform.localRotation = _defaultSpineRot
                    * Quaternion.Euler(spinePitch, -_strafeYawCurrent * 0.12f, rollMix * 0.30f);
            }
            if (chestTransform != null)
            {
                chestTransform.localRotation = _defaultChestRot
                    * Quaternion.Euler(chestPitch, _strafeYawCurrent * 0.35f, rollMix * 0.25f);
            }

            // 8.6 Neck/Head 头部稳定：链上累积的俯仰按比例反偿 —— 冲刺压胸、瞄准俯角、落地冲量时
            //     脸始终朝准星方向读表情；颈先柔回 40%、头硬回剩余 60%，视线不会刚性钉死在胸腔上。
            float chainPitch = spinePitch + chestPitch;
            float chainYaw = _strafeYawCurrent * 0.78f;
            float chainRoll = rollMix * 0.55f;
            float stab = Mathf.Clamp01(cfg.headStabilizePitchShare);
            if (neckTransform != null)
            {
                neckTransform.localRotation = _defaultNeckRot
                    * Quaternion.Euler(-chainPitch * 0.40f * stab, 0f, -chainRoll * 0.40f * stab);
            }
            if (headTransform != null)
            {
                float residual = chainPitch - chainPitch * 0.40f * stab;
                headTransform.localRotation = _defaultHeadRot
                    * Quaternion.Euler(-residual * 0.60f * stab, -chainYaw * 0.50f * stab, 0f)
                    // 呼吸时头部极轻反向浮动（生命感；与挤压同相位）
                    * Quaternion.Euler(breathPitch * 0.12f, 0f, 0f);
            }

            // 双臂基于持枪基础位姿叠加反相摆荡。P34：姿态基准改为"按武器 IK 反解的持握姿态"，
            // 并整体降幅（kidArmedArmSwingScale）——手握着枪，手臂大甩会让手掌从握把上滑开。
            // 用前乘（Chest 空间 X 轴）而不是后乘：无论持握姿态朝哪，摆臂都绕身体横轴前后甩，方向稳定。
            float armedSwingScale = _armHoldPoses.Count > 0
                ? Mathf.Clamp01(cfg.kidArmedArmSwingScale)
                : 1f;
            float armSwingPose = (armSwing + _landArmKickDeg) * armedSwingScale;
            if (leftArmTransform != null)
            {
                leftArmTransform.localRotation = Quaternion.Euler(armSwingPose, 0f, 0f) * _leftArmWeaponPoseRot;
            }
            if (rightArmTransform != null)
            {
                rightArmTransform.localRotation = Quaternion.Euler(-armSwingPose, 0f, 0f) * _rightArmPoseRot;
            }

            // --- 8.7 二段臂肘部（P33/P34）：按武器 IK 反解出的屈肘角为基准，
            // 步态前摆相额外收拢、随速度加深、落地伸肘；持枪时同样按 armedSwingScale 降幅。
            if (leftElbowPivot != null || rightElbowPivot != null)
            {
                float elbowSwingW = gaitWeight * swingScale;
                float speedDeepen = (cfg.kidElbowBendFastDeg - cfg.kidElbowBendBaseDeg) * speed01 * gaitWeight;
                float leftElbowAdd = ((gait.LeftElbowBendDeg - cfg.kidElbowBendBaseDeg) * elbowSwingW
                    + speedDeepen - _landArmKickDeg * 0.5f) * armedSwingScale;
                float rightElbowAdd = ((gait.RightElbowBendDeg - cfg.kidElbowBendBaseDeg) * elbowSwingW
                    + speedDeepen - _landArmKickDeg * 0.5f) * armedSwingScale;
                if (leftElbowPivot != null)
                {
                    leftElbowPivot.localRotation = _leftElbowPoseRot
                        * Quaternion.Euler(-leftElbowAdd, 0f, 0f);
                }
                if (rightElbowPivot != null)
                {
                    rightElbowPivot.localRotation = _rightElbowPoseRot
                        * Quaternion.Euler(-rightElbowAdd, 0f, 0f);
                }
            }

            // --- 9. 双腿 FK 写回（P29）：左右腿独立相位（摆动腿屈膝抬脚，支撑腿伸直踩地）+ 滞空团身/落地缓冲 ---
            // 旋转符号约定（腿指向 -Y、角色面向 +Z）：X 正旋转 = 腿向后摆 / 屈膝（脚跟后抬）/ 脚尖下垂
            float landingAbsorb = Mathf.Max(0f, -_jelly) * 110f;   // 果冻压扁（_jelly<0）瞬间屈膝缓冲，随弹簧回弹伸直
            // P26 落地三段·保持：压扁后屈膝保持片刻再回弹，砸地有重量感
            float landHold = Mathf.Clamp01(1f - _landTime / Mathf.Max(0.01f, cfg.landHoldTime)) * _landSquash01;
            landingAbsorb += landHold * cfg.landHoldKneeDeg;
            // P26 跑步起跳劈腿：前腿（左）膝前顶、后腿蹬直，随 launch 权重 0.2s 衰减
            float splitDeg = air.RunSplitDeg;
            if (leftLegRoot != null || rightLegRoot != null)
            {
                if (leftLegRoot != null)
                {
                    leftLegRoot.localRotation = _defaultLeftLegRot
                        * Quaternion.Euler(leftThigh + tuckThigh - splitDeg, 0f, 0f);
                }
                if (rightLegRoot != null)
                {
                    rightLegRoot.localRotation = _defaultRightLegRot
                        * Quaternion.Euler(rightThigh + tuckThigh + splitDeg * 0.6f, 0f, 0f);
                }
                if (leftKneePivot != null)
                {
                    leftKneePivot.localRotation = _defaultLeftKneeRot * Quaternion.Euler(leftKnee + tuckKnee + landingAbsorb, 0f, 0f);
                }
                if (rightKneePivot != null)
                {
                    rightKneePivot.localRotation = _defaultRightKneeRot * Quaternion.Euler(rightKnee + tuckKnee + landingAbsorb, 0f, 0f);
                }
                if (leftAnklePivot != null)
                {
                    leftAnklePivot.localRotation = _defaultLeftAnkleRot * Quaternion.Euler(leftFoot + tuckFoot, 0f, 0f);
                }
                if (rightAnklePivot != null)
                {
                    rightAnklePivot.localRotation = _defaultRightAnkleRot * Quaternion.Euler(rightFoot + tuckFoot, 0f, 0f);
                }
            }
        }

        /// <summary>
        /// P26 惯性倾斜弹簧层（加速度驱动，参考 INKWAVE character.js L1642-1657 的 lean springs）：
        /// 纵向：加速前倾冲劲 / 刹车后仰；横向：侧向起步压弯。滞后层：骨盆位移弹簧，
        /// 起步身体落后根节点、刹车身体冲过头再弹回 —— 走跑"有重量感"的关键。
        /// 输入已在 SampleMotion 钳制量程，弹簧负责平滑与轻微过冲；滞空/攀墙/传送时目标归零自然回稳。
        /// </summary>
        private void UpdateLeanSprings(float dt, bool grounded, bool isWallClimbing, CharacterAnimConfigSO cfg)
        {
            bool active = grounded && !isWallClimbing && !_teleportedThisFrame;
            float pitchTarget = 0f;
            float rollTarget = 0f;
            float lagX = 0f;
            float lagZ = 0f;
            if (active)
            {
                // 符号约定：pitch 正 = 前倾（加速 a.z>0 → 前倾冲劲）；roll 正 = 向左倾（向右加速 a.x>0 → 向右压弯）
                pitchTarget = Mathf.Clamp(_localAccel.z * cfg.leanPitchGain, -cfg.leanPitchMaxDeg, cfg.leanPitchMaxDeg);
                rollTarget = Mathf.Clamp(-_localAccel.x * cfg.leanRollGain, -cfg.leanRollMaxDeg, cfg.leanRollMaxDeg);
                // 滞后与倾斜反号：加速向前 → 身体重心向后拖（惯性）
                lagZ = Mathf.Clamp(-_localAccel.z * cfg.hipsLagGain, -cfg.hipsLagMaxMeters, cfg.hipsLagMaxMeters);
                lagX = Mathf.Clamp(-_localAccel.x * cfg.hipsLagGain, -cfg.hipsLagMaxMeters, cfg.hipsLagMaxMeters);
            }

            const float TwoPi = 2f * Mathf.PI;
            _leanPitchSpring.Step(pitchTarget, TwoPi * cfg.leanPitchHz, cfg.leanPitchZeta, dt);
            _leanRollSpring.Step(rollTarget, TwoPi * cfg.leanRollHz, cfg.leanRollZeta, dt);
            _hipsLagSpringX.Step(lagX, TwoPi * cfg.hipsLagHz, cfg.hipsLagZeta, dt);
            _hipsLagSpringZ.Step(lagZ, TwoPi * cfg.hipsLagHz, cfg.hipsLagZeta, dt);
        }

        /// <summary>
        /// P26 待机呼吸（参考 INKWAVE _breathe 节奏）：吸气 40% → 呼气 45% → 停顿 15%，
        /// 比旧版匀速正弦更像"一口气"；相位常驻推进（恢复 idle 时无缝续接），幅度仅 idle 生效。
        /// 返回 -1..1 呼吸相位波。
        /// </summary>
        private float UpdateIdleBreath(float dt, bool idle, CharacterAnimConfigSO cfg)
        {
            _breathPhase += dt * cfg.idleBreathHz * _breathHzJitter;
            if (_breathPhase >= 1f) _breathPhase -= Mathf.Floor(_breathPhase);
            if (!idle) return 0f;

            float bu = _breathPhase;
            float bb = bu < 0.4f
                ? Mathf.SmoothStep(0f, 1f, bu / 0.4f)
                : bu < 0.85f ? 1f - Mathf.SmoothStep(0f, 1f, (bu - 0.4f) / 0.45f)
                : 0f;
            return bb * 2f - 1f;
        }

        /// <summary>
        /// P26 待机重心转移：每 3.2~7.2s 随机把重心换到另一条腿（弹簧慢速过冲回稳），
        /// 表现为身体侧倾 + 横向微位移 —— "人不会一直以完全对称的站姿站着"。
        /// 返回 -1..1 转移量。
        /// </summary>
        private float UpdateIdleShift(float dt, bool idle, CharacterAnimConfigSO cfg)
        {
            if (idle)
            {
                _shiftTimer -= dt;
                if (_shiftTimer <= 0f)
                {
                    _shiftTarget = _shiftTarget >= 0.5f ? -1f : 1f;
                    _shiftTimer = UnityEngine.Random.Range(cfg.idleShiftIntervalMin, cfg.idleShiftIntervalMax);
                }
            }
            else
            {
                _shiftTarget = 0f;
            }

            _shiftSpring.Step(_shiftTarget, 2f * Mathf.PI * 0.7f, 0.85f, dt);
            return _shiftSpring.Value;
        }

        /// <summary>
        /// P26 待机微动作（蛋仔式"活气"）：每 4~9s 随机做一个小动作 —— 晃身体 / 小跳 / 歪身子，
        /// 用 one-shot 计时器 + 正弦窗口驱动；移动/滞空立即打断，刚开火不久不触发。
        /// </summary>
        private void UpdateIdleFidget(float dt, bool idle, CharacterAnimConfigSO cfg, ref float rock)
        {
            if (!idle)
            {
                _fidgetKind = -1;
                return;
            }

            if (_fidgetKind < 0)
            {
                if (Time.time - _lastFireTime < 1.5f) return;
                _fidgetTimer -= dt;
                if (_fidgetTimer <= 0f)
                {
                    _fidgetKind = UnityEngine.Random.Range(0, 3);
                    _fidgetTime = 0f;
                    _fidgetJumpDone = false;
                    _fidgetDuration = UnityEngine.Random.Range(0.9f, 1.3f);
                    _fidgetTimer = UnityEngine.Random.Range(cfg.idleFidgetIntervalMin, cfg.idleFidgetIntervalMax);
                }
                return;
            }

            _fidgetTime += dt;
            float u = _fidgetTime / Mathf.Max(0.01f, _fidgetDuration);
            if (u >= 1f)
            {
                _fidgetKind = -1;
                return;
            }

            float window = Mathf.Sin(u * Mathf.PI);   // 平滑进出
            switch (_fidgetKind)
            {
                case 0: // 晃身体：左右两拍
                    rock += Mathf.Sin(u * Mathf.PI * 4f) * 4f * window;
                    break;
                case 1: // 小跳一下：果冻弹簧拉伸冲量（仅触发一次）
                    if (!_fidgetJumpDone)
                    {
                        _jellyVel += 2.2f;
                        _fidgetJumpDone = true;
                    }
                    break;
                case 2: // 歪身子：保持一个侧倾一拍
                    rock += 3.5f * window;
                    break;
            }
        }

        /// <summary>触发一次眨眼（重落地等事件用）；进行中则忽略，并顺延下次随机眨眼。</summary>
        private void TriggerBlink()
        {
            if (_blinkStartTime >= 0f) return;
            _blinkStartTime = Time.time;
            _nextBlinkTime = Time.time + UnityEngine.Random.Range(2.5f, 5.0f);
        }

        /// <summary>
        /// 程序化眨眼：两眼局部 Y 轴在 0.11s 内压扁至 0.08 再回弹。
        /// 随机间隔 2.5~5.0s，每个角色实例非同步；严格基于默认 scale 插值，杜绝累乘。
        /// </summary>
        private void UpdateBlink(float dt)
        {
            if (leftEyeTransform == null && rightEyeTransform == null) return;

            const float BlinkDuration = 0.11f;
            if (Time.time >= _nextBlinkTime && _blinkStartTime < 0f)
            {
                TriggerBlink();
            }

            float blinkWeight = 0f;
            if (_blinkStartTime >= 0f)
            {
                float elapsed = Time.time - _blinkStartTime;
                if (elapsed <= BlinkDuration)
                {
                    blinkWeight = Mathf.Sin(Mathf.Clamp01(elapsed / BlinkDuration) * Mathf.PI);
                }
                else
                {
                    _blinkStartTime = -1f;
                }
            }

            float scaleYFactor = Mathf.Lerp(1f, 0.08f, blinkWeight);
            if (leftEyeTransform != null)
            {
                leftEyeTransform.localScale = new Vector3(_defaultLeftEyeScale.x, _defaultLeftEyeScale.y * scaleYFactor, _defaultLeftEyeScale.z);
            }
            if (rightEyeTransform != null)
            {
                rightEyeTransform.localScale = new Vector3(_defaultRightEyeScale.x, _defaultRightEyeScale.y * scaleYFactor, _defaultRightEyeScale.z);
            }
        }

        /// <summary>
        /// P36 视线灵动：眼球（白膜+虹膜+瞳孔+高光在装配期已按眼合并成单个网格）在深色眼罩内
        /// 整体微移，模拟"看"。待机且未开火时随机游目（saccade），活动/开火后回中；
        /// 瞄准俯仰映射到竖向偏移（抬头瞄准眼球上移）。只写 localPosition，
        /// 严格基于装配期缓存的默认位置，杜绝累乘；偏移钳在眼罩留白内（眼罩比眼球宽约 12mm）。
        /// </summary>
        private void UpdateEyeLook(float dt, CharacterAnimConfigSO cfg, bool idle)
        {
            if (leftEyeTransform == null && rightEyeTransform == null) return;

            float maxOffset = Mathf.Max(0f, cfg.eyeLookMaxOffset);
            if (idle && Time.time - _lastFireTime > 1.5f)
            {
                _eyeSaccadeTimer -= dt;
                if (_eyeSaccadeTimer <= 0f)
                {
                    _eyeSaccadeTimer = UnityEngine.Random.Range(cfg.eyeSaccadeIntervalMin, cfg.eyeSaccadeIntervalMax);
                    _eyeSaccadeTarget = new Vector2(
                        UnityEngine.Random.Range(-maxOffset, maxOffset),
                        UnityEngine.Random.Range(-maxOffset * 0.5f, maxOffset * 0.7f));
                }
            }
            else
            {
                _eyeSaccadeTarget = Vector2.zero;
                // 回到待机后 0.4s 内恢复游目，不会长时间"瞪直眼"
                _eyeSaccadeTimer = Mathf.Min(_eyeSaccadeTimer, 0.4f);
            }

            // 瞄准注视：_aimPitchCurrent 正 = 低头 → 眼球下移；负 = 抬头 → 上移
            float aimY = Mathf.Clamp(-_aimPitchCurrent * cfg.eyeLookAimGain, -maxOffset, maxOffset);
            Vector2 target = _eyeSaccadeTarget + new Vector2(0f, aimY);
            target.x = Mathf.Clamp(target.x, -maxOffset, maxOffset);
            target.y = Mathf.Clamp(target.y, -maxOffset, maxOffset);

            _eyeLookCurrent = Vector2.SmoothDamp(_eyeLookCurrent, target, ref _eyeLookVelocity,
                Mathf.Max(0.02f, cfg.eyeLookSmoothTime), 1f, dt);

            Vector3 offset = new Vector3(_eyeLookCurrent.x, _eyeLookCurrent.y, 0f);
            if (leftEyeTransform != null)
            {
                leftEyeTransform.localPosition = _defaultLeftEyePos + offset;
            }
            if (rightEyeTransform != null)
            {
                rightEyeTransform.localPosition = _defaultRightEyePos + offset;
            }
        }

        /// <summary>
        /// 触须发弹簧链（P33；对齐 INKWAVE 发束 3 段弹簧骨 + 鞭梢的无蒙皮近似，用 2 段枢轴链承担）：
        /// 根段阻尼弹簧追"视在重力"目标（身体前倾/加速 → 发往后甩；侧倾 → 左右让），
        /// 梢段更软弹簧由根段角 × 鞭梢增益驱动 → 二级滞后甩出鞭梢回摆；
        /// 待机每束叠 0.42Hz 错相行波（整头头发不会齐刷刷同摆），起跳/落地/变形注入速度冲量。
        /// 未绑定弹簧链数组（旧场景兜底）时退回双 pivot SmoothDamp 摆动。
        /// </summary>
        private void UpdateHairSway(float dt)
        {
            if (kidHairRootPivots != null && kidHairRootPivots.Length > 0
                && kidHairRootBaseRots != null && kidHairRootBaseRots.Length == kidHairRootPivots.Length
                && _hairRootPitchSprings != null && _hairRootPitchSprings.Length == kidHairRootPivots.Length)
            {
                CharacterAnimConfigSO cfg = AnimConfig;
                float omegaRoot = Mathf.Max(1f, cfg.hairRootOmega);
                float omegaTip = Mathf.Max(1f, cfg.hairMidOmega);

                // 视在重力目标（度）：前倾甩后 + 纵向加速放大；侧倾让发左右摆 + 横向加速放大
                float pitchTarget = Mathf.Clamp(-_currentPitch * 1.15f - _localAccel.z * cfg.hairAccelGain, -24f, 16f);
                float rollTarget = Mathf.Clamp(_currentRoll * 0.9f + _localAccel.x * cfg.hairAccelGain, -18f, 18f);

                for (int i = 0; i < kidHairRootPivots.Length; i++)
                {
                    Transform root = kidHairRootPivots[i];
                    if (root == null) continue;

                    // 逐束错相的待机行波（2π·0.42Hz ≈ 2.637rad/s，束间相位 1.1rad）
                    float idleWave = Mathf.Sin(Time.time * 2.637f + i * 1.1f) * cfg.hairIdleWaveDeg;

                    _hairRootPitchSprings[i].Step(pitchTarget + idleWave, omegaRoot, cfg.hairRootZeta, dt);
                    _hairRootRollSprings[i].Step(rollTarget, omegaRoot, cfg.hairRootZeta, dt);
                    float rp = Mathf.Clamp(_hairRootPitchSprings[i].Value + _flingHairPitchDeg, -34f, 28f);
                    float rr = Mathf.Clamp(_hairRootRollSprings[i].Value + _flingHairRollDeg, -28f, 28f);
                    root.localRotation = kidHairRootBaseRots[i]
                        * Quaternion.Euler(rp, 0f, rr);

                    // 梢段：更软弹簧 + 鞭梢增益 → 比根段更晚、更大反向甩出后回稳
                    if (kidHairMidPivots != null && i < kidHairMidPivots.Length
                        && kidHairMidPivots[i] != null
                        && kidHairMidBaseRots != null && i < kidHairMidBaseRots.Length
                        && _hairMidPitchSprings != null && i < _hairMidPitchSprings.Length)
                    {
                        _hairMidPitchSprings[i].Step(rp * cfg.hairMidWhip, omegaTip, cfg.hairMidZeta, dt);
                        _hairMidRollSprings[i].Step(rr * cfg.hairMidWhip, omegaTip, cfg.hairMidZeta, dt);
                        float mp = Mathf.Clamp(_hairMidPitchSprings[i].Value, -42f, 34f);
                        float mr = Mathf.Clamp(_hairMidRollSprings[i].Value, -32f, 32f);
                        kidHairMidPivots[i].localRotation = kidHairMidBaseRots[i]
                            * Quaternion.Euler(mp, 0f, mr);
                    }
                }
                return;
            }

            // 旧双 pivot 兜底（未重装配的旧场景）：保持 P32 语义，避免退化到无摆动
            if (leftHairPivot == null && rightHairPivot == null) return;

            float targetHairPitch = Mathf.Clamp(-_currentPitch * 0.8f, -18f, 10f);
            float targetHairRoll = Mathf.Clamp(_currentRoll * 0.9f, -14f, 14f);

            _currentHairPitch = Mathf.SmoothDamp(_currentHairPitch, targetHairPitch, ref _hairPitchVel, 0.12f, 120f, dt);
            _currentHairRoll = Mathf.SmoothDamp(_currentHairRoll, targetHairRoll, ref _hairRollVel, 0.12f, 120f, dt);

            Quaternion swayRot = Quaternion.Euler(
                _currentHairPitch + _flingHairPitchDeg, 0f,
                _currentHairRoll + _flingHairRollDeg);
            if (leftHairPivot != null) leftHairPivot.localRotation = _defaultLeftHairRot * swayRot;
            if (rightHairPivot != null) rightHairPivot.localRotation = _defaultRightHairRot * swayRot;
        }

        /// <summary>
        /// 触须发冲量注入（起跳蹬地/落地砸腿/形态翻转）：按束序错开强度，
        /// 让整头头发像涟漪一样从后往前起浪，而不是齐刷刷一抖（INKWAVE _hairKick 观感）。
        /// </summary>
        private void KickHair(float pitchImpulse, float rollImpulse)
        {
            if (_hairRootPitchSprings == null) return;
            for (int i = 0; i < _hairRootPitchSprings.Length; i++)
            {
                float stagger = 1f + i * 0.12f;
                _hairRootPitchSprings[i].Kick(pitchImpulse * stagger);
                _hairRootRollSprings[i].Kick(rollImpulse);
            }
        }

        /// <summary>
        /// 由 PlayerController 每帧写入相机俯仰（度，正 = 低头俯视）。
        /// 武器模型绕挂点整体俯仰（承担全部仰角），胸腔按配置权重分担，角色始终保持直立 ——
        /// 对齐 INKWAVE 的「武器挂点吃满瞄准俯仰、脊柱/胸腔只分担 10~18%」解耦做法。
        /// </summary>
        public void SetAimPitch(float pitchDegrees)
        {
            if (float.IsNaN(pitchDegrees) || float.IsInfinity(pitchDegrees))
                return;

            _aimPitchTarget = Mathf.Clamp(pitchDegrees, -80f, 80f);
        }

        /// <summary>
        /// 瞄准俯仰姿态：平滑相机俯仰并写回当前武器模型根节点（绕握把挂点旋转，枪口随之指向准星方向）。
        /// </summary>
        private void UpdateAimPose(float dt)
        {
            PlayerConfigSO config = _motor != null ? _motor.Config : null;

            float smoothTime = config != null ? Mathf.Max(0.01f, config.aimPoseSmoothTime) : 0.07f;
            _aimPitchCurrent = Mathf.SmoothDamp(
                _aimPitchCurrent, _aimPitchTarget, ref _aimPitchVelocity, smoothTime, 720f, dt);

            if (!_weaponMap.TryGetValue(_currentWeaponKind, out GameObject model) || model == null)
                return;

            Transform modelTransform = model.transform;
            if (!_weaponModelBaseRots.TryGetValue(model, out Quaternion baseRot))
            {
                baseRot = modelTransform.localRotation;
                _weaponModelBaseRots[model] = baseRot;
            }

            float weight = config != null ? Mathf.Clamp01(config.aimPoseWeaponPitchWeight) : 1f;
            // P27 对齐 INKWAVE：滚筒不进瞄准姿态（character.js L1261 显式排除 roller）——
            // 否则瞄准俯仰（权重 1，相机 ±66°）会把贴地拖行的滚筒压穿地板；姿态角全权接管，
            // 瞄准方向由胸腔分担（chestAimWeight 0.18）与弹道解算表达
            bool isRoller = _currentWeaponKind == WeaponKind.Roller;
            if (isRoller) weight = 0f;
            // P27 滚筒持握/攻击姿态角直接合成进同一欧拉角（_rollerPosePitchDeg 非滚筒恒为 0，
            // 且位于瞄准权重之后不受其缩放）——不在 WeaponVisual 上另写旋转，避免与后坐力写回互踩
            modelTransform.localRotation = baseRot * Quaternion.Euler(
                _aimPitchCurrent * weight + _rollerPosePitchDeg + _wieldPitchDeg,
                _rollerPoseYawDeg + _wieldYawDeg,
                _rollerPoseRollDeg + _wieldRollDeg);
        }

        /// <summary>
        /// P27 滚筒持握/攻击时间线：轮询 <see cref="WeaponController.IsFlicking"/> 边沿驱动挥击四段
        /// （前摇抡起过肩 → 墨滴释放帧砸地 → 随挥 → 收势），<see cref="WeaponController.IsRolling"/>
        /// 驱动拖地姿态权重（入 11 / 出 6 阻尼，砸地→拖行的过渡由权重混合天然平滑）。
        /// 产出帧值供 UpdateAimPose / UpdateRecoilKick / UpdateBodyPose 合成写回；
        /// 滚轴自转在 UpdateWeaponMechanics。非滚筒武器帧值恒为 0，各合成点零影响。
        /// </summary>
        private void UpdateRollerPose(float dt)
        {
            if (_currentWeaponKind != WeaponKind.Roller)
            {
                _rollerPosePitchDeg = 0f;
                _rollerPoseYawDeg = 0f;
                _rollerPoseRollDeg = 0f;
                _rollerPoseDrop = 0f;
                _rollerPoseForward = 0f;
                _rollerBodyPitchDeg = 0f;
                _rollerSink01 = 0f;
                _rollerPoseContact01 = 0f;
                _rollerFlickActive = false;
                _prevFlicking = false;
                _rollerRollWeight = 0f;
                return;
            }

            CharacterAnimConfigSO cfg = AnimConfig;
            RollerWieldParams p = new RollerWieldParams
            {
                CarryPitchDeg = cfg.rollerCarryPitchDeg,
                CarryYawDeg = cfg.rollerCarryYawDeg,
                CarryRollDeg = cfg.rollerCarryRollDeg,
                FlickLiftPitchDeg = cfg.rollerFlickLiftPitchDeg,
                FlickSlamPitchDeg = cfg.rollerFlickSlamPitchDeg,
                FlickSlamDropMeters = cfg.rollerFlickSlamDrop,
                FlickWhipDuration = cfg.rollerFlickWhipDuration,
                FlickFollowDuration = cfg.rollerFlickFollowDuration,
                FlickRecoverDuration = cfg.rollerFlickRecoverDuration,
                FlickCoilBodyPitchDeg = cfg.rollerFlickCoilBodyPitchDeg,
                FlickWhipBodyPitchDeg = cfg.rollerFlickWhipBodyPitchDeg,
                SlamSquashImpulse = cfg.rollerSlamSquashImpulse,
                RollPitchDeg = cfg.rollerRollPitchDeg,
                RollDropMeters = cfg.rollerRollDrop,
                RollForwardMeters = cfg.rollerRollForward,
                RollBodyPitchDeg = cfg.rollerRollBodyPitchDeg,
                RollSink = cfg.rollerRollSink,
                RollWeightInRate = cfg.rollerRollWeightInRate,
                RollWeightOutRate = cfg.rollerRollWeightOutRate,
                DrumFlickSpinRad = cfg.rollerDrumFlickSpin,
                DrumCoastDecay = cfg.rollerDrumCoastDecay
            };

            bool flicking = _weaponController != null && _weaponController.IsFlicking;
            bool rolling = _weaponController != null && _weaponController.IsRolling;

            // 挥击时间线状态机：上升沿从持握起表抡起；下降沿 = 墨滴释放帧，只触发砸地冲量与滚轴甩转
            //（时间线不重置时钟——前摇段与砸下段在抡起角处天然连续，帧量化导致的小偏差被钳制吸收）
            if (flicking && !_prevFlicking)
            {
                _rollerFlickActive = true;
                _rollerFlickTime = 0f;
            }
            else if (!flicking && _prevFlicking && _rollerFlickActive)
            {
                _jellyVel -= p.SlamSquashImpulse;   // 负向 = 压扁（与落地同款果冻弹簧）
                _drumSpinRadPerSec += p.DrumFlickSpinRad;
            }
            _prevFlicking = flicking;

            // 拖地姿态权重（INKWAVE wRoll：切入 11 / 退出 6 的指数阻尼）
            float previousRollWeight = _rollerRollWeight;
            float rollRate = rolling ? p.RollWeightInRate : p.RollWeightOutRate;
            _rollerRollWeight += ((rolling ? 1f : 0f) - _rollerRollWeight) * (1f - Mathf.Exp(-rollRate * dt));

            // P31 入地重量感：拖地权重首次压过 0.65（滚筒真正落到地面附近）时给果冻弹簧一次压扁冲量，
            // 让"按住→滚筒拍地→推着走"有一个明确的重量落点；松手回落再次进入会重新触发。
            if (previousRollWeight < 0.65f && _rollerRollWeight >= 0.65f)
            {
                _jellyVel -= Mathf.Max(0f, cfg.rollerRollPlantSquashImpulse);
            }

            RollerWieldPose pose;
            if (_rollerFlickActive)
            {
                _rollerFlickTime += dt;
                float windup = _weaponController != null && _weaponController.Config != null
                    ? Mathf.Max(0f, _weaponController.Config.flickWindup)
                    : 0.22f;
                pose = RollerWieldSolver.SampleFlick(_rollerFlickTime, windup, p);
                if (_rollerFlickTime >= RollerWieldSolver.TotalDuration(windup, p))
                {
                    _rollerFlickActive = false;
                }
            }
            else
            {
                pose = RollerWieldSolver.SampleCarry(p);
            }

            // 拖地姿态按 wRoll 覆盖（推地时即使还在收势，滚筒也会被压回地面继续拖行）
            pose = RollerWieldSolver.Blend(pose, RollerWieldSolver.SampleRoll(p), _rollerRollWeight);

            _rollerPosePitchDeg = pose.PitchDeg;
            _rollerPoseYawDeg = pose.YawDeg;
            _rollerPoseRollDeg = pose.RollDeg;
            _rollerPoseDrop = pose.DropMeters;
            _rollerPoseForward = pose.ForwardMeters;
            _rollerBodyPitchDeg = pose.BodyPitchDeg;
            _rollerSink01 = pose.Sink01;
            // P35：只要处于推地状态就强制贴地闭环满权重，挥击收势姿态不得把滚筒重新抬离地面
            _rollerPoseContact01 = rolling ? 1f : Mathf.Clamp01(pose.GroundContact01);
        }

        /// <summary>
        /// 开火后坐力弹簧回稳（P28）：Z / Pitch / Yaw 共用同一角频率，快速出去、轻微过冲后回稳。
        /// </summary>
        private void UpdateRecoilKick(float dt)
        {
            if (weaponRoot == null) return;

            float omega = _weaponController != null
                ? _weaponController.CurrentFeel.SpringOmega
                : WeaponFeelProfile.ForKind(_currentWeaponKind).SpringOmega;

            _recoilZSpring.Step(0f, omega, WeaponFeelMath.RecoilDampingRatio, dt);
            _recoilPitchSpring.Step(0f, omega, WeaponFeelMath.RecoilDampingRatio, dt);
            _recoilYawSpring.Step(0f, omega, WeaponFeelMath.RecoilDampingRatio, dt);

            // 安全钳制：极端配置/连续冲量下也不让武器穿出角色身体
            float recoilZ = Mathf.Clamp(_recoilZSpring.Value, -0.28f, 0.02f);
            float recoilPitch = Mathf.Clamp(_recoilPitchSpring.Value, -3f, 16f);
            float recoilYaw = Mathf.Clamp(_recoilYawSpring.Value, -4f, 4f);

            // P27 滚筒砸地/拖地挂点下压与前推合成进同一位置（非滚筒武器两项恒为 0）
            weaponRoot.transform.localPosition = _weaponRootBasePos
                + new Vector3(0f, -(_rollerPoseDrop + _wieldDrop),
                    recoilZ + _rollerPoseForward + _wieldForward);
            weaponRoot.transform.localRotation = Quaternion.Euler(-recoilPitch, recoilYaw, 0f);
        }

        /// <summary>
        /// 墨水储罐背包液面动态升降
        /// </summary>
        private void UpdateTankFill()
        {
            if (tankLiquidFill == null || _motor == null) return;

            float ink01 = _motor.Ink01;
            // 墨水高度与位置随墨水量比例线性收缩，底部吸附在底盖
            float sy = Mathf.Clamp(_tankFillBaseScale.y * ink01, _tankFillBaseScale.y * 0.05f, _tankFillBaseScale.y);
            tankLiquidFill.localScale = new Vector3(_tankFillBaseScale.x, sy, _tankFillBaseScale.z);

            float deltaY = (sy - _tankFillBaseScale.y) * 0.5f;
            tankLiquidFill.localPosition = _tankFillBasePos + new Vector3(0f, deltaY, 0f);
        }

        /// <summary>
        /// 武器专属机械构件动画
        /// </summary>
        private void UpdateWeaponMechanics(float dt)
        {
            // 1. 滚筒滚轴旋转（P27 重做为 INKWAVE 公式 character-weapons.js L691-699：
            //    拖地贴地时按地速无滑动滚动 ω=地速/滚筒半径；停转按 exp(-decay·t) 惯性滑行衰减；
            //    挥击砸地瞬间叠加甩转冲量。举起携带跑动时滚轴不再空转）
            if (_currentWeaponKind == WeaponKind.Roller && rollerDrumTransform != null)
            {
                bool rolling = _weaponController != null && _weaponController.IsRolling;
                if (rolling && _motor != null && _motor.IsGrounded)
                {
                    _drumSpinRadPerSec = RollerWieldSolver.DrumSpinRadPerSec(_planarSpeed, true);
                }
                else
                {
                    _drumSpinRadPerSec = RollerWieldSolver.DrumCoastStep(
                        _drumSpinRadPerSec, AnimConfig.rollerDrumCoastDecay, dt);
                }

                if (_drumSpinRadPerSec > 0.05f)
                {
                    rollerDrumTransform.Rotate(Vector3.right, _drumSpinRadPerSec * Mathf.Rad2Deg * dt, Space.Self);
                }

                // P35 轻量墨雾：推地时在滚轴接地处每 0.15s 喷 2~3 颗队伍色墨滴，
                // 走 VfxPool 池化粒子，补足"滚筒压着湿墨推着走"的视觉信号。
                if (rolling && _motor != null && _motor.IsGrounded && VfxPool.Instance != null)
                {
                    _rollerSprayTimer -= dt;
                    if (_rollerSprayTimer <= 0f)
                    {
                        _rollerSprayTimer = 0.15f;
                        Color sprayColor = InkSurfaceController.Instance != null
                            ? InkSurfaceController.Instance.GetTeamColor(_team)
                            : Color.white;
                        Vector3 sprayPos = rollerDrumTransform.position
                            + Vector3.down * (RollerWieldSolver.DrumRadiusMeters * 0.6f);
                        VfxPool.Instance.SpawnInkSplat(sprayPos, sprayColor, 0.3f, 3);
                    }
                }
                else
                {
                    _rollerSprayTimer = 0f;
                }
            }

            // 2. 加特林枪管旋转（P35：转速随蓄力刻度连续上升，扫射满速，松开惯性滑停）
            if (_currentWeaponKind == WeaponKind.Splatling && splatlingBarrelsTransform != null)
            {
                WeaponWieldParams p = BuildWieldParams(AnimConfig);
                bool charging = _weaponController != null && _weaponController.IsCharging;
                bool streaming = _weaponController != null && _weaponController.IsStreaming;
                bool special = _weaponController != null && _weaponController.IsPerformingSpecial;
                float charge01 = _weaponController != null ? _weaponController.Charge01 : 0f;

                float targetSpin = special
                    ? p.SplatlingSpinMaxDegPerSec
                    : WeaponWieldSolver.SplatlingSpinDegPerSec(charging, streaming, charge01, p);

                if (targetSpin > 0.5f)
                {
                    _splatlingSpinDegPerSec = Mathf.MoveTowards(
                        _splatlingSpinDegPerSec, targetSpin, 6000f * dt);
                }
                else
                {
                    _splatlingSpinDegPerSec = WeaponWieldSolver.SplatlingSpinCoastStep(
                        _splatlingSpinDegPerSec, dt, p);
                }

                if (_splatlingSpinDegPerSec > 0.5f)
                {
                    splatlingBarrelsTransform.Rotate(
                        Vector3.forward, _splatlingSpinDegPerSec * dt, Space.Self);
                }
            }

            // 3. 蓄力狙发光线圈
            if (_currentWeaponKind == WeaponKind.Charger && chargerCoilRenderers != null && chargerCoilRenderers.Length > 0)
            {
                float charge = _weaponController != null ? _weaponController.Charge01 : 0f;
                Color baseGlow = _team == Team.TeamA ? new Color(1f, 0.45f, 0.08f) : new Color(0.16f, 0.55f, 1f);
                // P35 蓄力越高，线圈脉冲越快，读作"能量在往枪口涌"
                float pulseHz = Mathf.Lerp(12f, 34f, Mathf.Clamp01(charge));
                float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * pulseHz);
                Color glow = baseGlow * (0.2f + charge * 2.8f) * pulse;

                if (_chargerCoilPropBlock == null)
                {
                    _chargerCoilPropBlock = new MaterialPropertyBlock();
                }

                for (int i = 0; i < chargerCoilRenderers.Length; i++)
                {
                    var rend = chargerCoilRenderers[i];
                    if (rend != null)
                    {
                        rend.GetPropertyBlock(_chargerCoilPropBlock);
                        _chargerCoilPropBlock.SetColor(EmissionColorProp, glow);
                        rend.SetPropertyBlock(_chargerCoilPropBlock);
                    }
                }
            }
        }

        /// <summary>
        /// P31 滚筒贴地闭环（末段接管）：滚轴挂在 Chest 下，胸口俯仰（第三人称视角 ±66° × 0.18 + 移动前倾）
        /// 会让 0.48m 长杠杆把几度胸口变化放大成数厘米的离地/陷地。固定角度无法覆盖全视角，这里在所有
        /// 姿态写完后：
        /// 1) 射线下探滚轴正下方的真实地面（忽略角色自身碰撞体，坡道/平台边缘按实际高度）；
        /// 2) 以握把为枢轴，用数值斜率反解需要的俯仰增量，把滚轴外缘钉在 ground + 半径 - 压入量；
        /// 3) 权重 = 姿态层 GroundContact01 × 贴地状态，平滑进出；只看世界坐标，不依赖旧标定常量。
        /// 修正作用于武器模型自身旋转（握把不脱手），不影响后坐力/瞄准的写回。
        /// </summary>
        private void ApplyRollerGroundContact(float dt)
        {
            if (!_weaponMap.TryGetValue(WeaponKind.Roller, out GameObject rollerModel) || rollerModel == null)
            {
                _rollerContactWeight = 0f;
                return;
            }

            Transform model = rollerModel.transform;

            // P35 先复位到装配期基准：上一帧的高度保险/俯仰修正绝不累积。
            // 基准在 RebuildArmHoldPoses（握把锚定）之后捕获，切枪/重装配时会刷新。
            // 即使当前拿的是别的武器也复位一次，避免隐藏的滚筒残留高度修正。
            if (_rollerModelBaseCaptured)
                model.localPosition = _rollerModelBaseLocalPos;

            if (_currentWeaponKind != WeaponKind.Roller ||
                weaponRoot == null ||
                rollerDrumTransform == null)
            {
                _rollerContactWeight = 0f;
                return;
            }

            CharacterAnimConfigSO cfg = AnimConfig;
            bool canContact = _groundedNow && _motor != null && _formAmount < 0.5f;
            float target = canContact ? Mathf.Clamp01(_rollerPoseContact01) : 0f;
            float rate = target > _rollerContactWeight
                ? Mathf.Max(0.01f, cfg.rollerGroundContactInRate)
                : Mathf.Max(0.01f, cfg.rollerGroundContactOutRate);
            _rollerContactWeight += (target - _rollerContactWeight) * (1f - Mathf.Exp(-rate * dt));
            if (_rollerContactWeight < 0.002f)
                return;

            Vector3 drumAxis = rollerDrumTransform.position;
            float lever = Vector3.Dot(drumAxis - model.position, model.forward);
            if (lever <= 0.02f)
                return;

            float groundY = ResolveRollerGroundY(drumAxis);
            float targetAxisY = RollerWieldSolver.GroundContactTargetAxisY(groundY, cfg.rollerGroundPenetration);
            float targetForwardY = RollerWieldSolver.GroundContactTargetForwardY(
                model.position.y, targetAxisY, lever);

            // 数值斜率：绕握把局部 X 轴每转 1° 对应的 world forward.y 变化。
            // 不解析骨架/滚轴偏置，装配器改挂点/比例后依然成立。
            // 视角大角度变化时单步线性反解会残留厘米级误差（forward.y 对俯仰是三角函数），
            // 这里在同一帧迭代 3 次求收敛解，再按贴地权重与原始姿态混合。
            Quaternion originalRot = model.localRotation;
            Quaternion solvedRot = originalRot;
            for (int iteration = 0; iteration < 3; iteration++)
            {
                model.localRotation = solvedRot;
                float forwardY = model.forward.y;
                model.localRotation = solvedRot * Quaternion.Euler(1f, 0f, 0f);
                float slope = model.forward.y - forwardY;
                if (Mathf.Abs(slope) < 0.00001f)
                    break;

                float deltaDeg = Mathf.Clamp((targetForwardY - forwardY) / slope, -60f, 60f);
                solvedRot *= Quaternion.Euler(deltaDeg, 0f, 0f);
                if (Mathf.Abs(deltaDeg) < 0.02f)
                    break;
            }

            model.localRotation = Quaternion.Slerp(originalRot, solvedRot, _rollerContactWeight);

            // P35 最终高度保险：俯仰反解在极端视角/坡度下仍可能残留 1~2cm，这里直接按世界高度差
            // 平移整个滚筒模型（握把/双手随 IK 跟下去），确保"按住推地 = 滚轴底缘贴地"。
            // 只在贴地权重过半后生效；单帧限幅 ±5cm，避免把滚筒拽下悬崖或抬上箱顶。
            if (_rollerContactWeight > 0.5f)
            {
                float gap = rollerDrumTransform.position.y
                            - RollerWieldSolver.DrumRadiusMeters
                            - groundY;
                if (Mathf.Abs(gap) > 0.001f)
                {
                    float correction = Mathf.Clamp(gap, -0.05f, 0.05f) * _rollerContactWeight;
                    model.position += Vector3.down * correction;
                }
            }
        }

        /// <summary>
        /// 滚轴正下方地面高度：从滚轴上方 1m 下探 2.5m，跳过角色自身所有碰撞体。
        /// 结果钳在角色脚下地面 ±可站立高度带内：墙顶/集装箱顶不会把滚筒抬飞，深坑不会把滚筒拽下悬崖。
        /// 无命中（场地缺口/悬空）时回退到角色脚下地面（root.y - skinWidth）。
        /// </summary>
        private float ResolveRollerGroundY(Vector3 drumAxis)
        {
            float skin = _motor != null && _motor.Config != null
                ? Mathf.Clamp(_motor.Config.controllerSkinWidth, 0.004f, 0.04f)
                : 0.006f;
            float rootY = _motor != null ? _motor.transform.position.y : transform.position.y;
            float feetGroundY = rootY - skin;

            Vector3 origin = drumAxis + Vector3.up * 1.0f;
            int count = Physics.RaycastNonAlloc(
                origin, Vector3.down, _rollerGroundHits, 2.5f, GameLayers.WorldMask, QueryTriggerInteraction.Ignore);

            Transform selfRoot = _motor != null ? _motor.transform : transform.root;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider col = _rollerGroundHits[i].collider;
                if (col == null || col.transform.IsChildOf(selfRoot))
                    continue;
                // 其他角色不是地形：WorldMask 已排除 Combatant 层；保留 CharacterController 类型判断，
                // 兼容「层还没建立」的旧场景/测试（此时 mask 回退 ~0）。
                if (col is CharacterController)
                    continue;
                float d = _rollerGroundHits[i].distance;
                if (d < nearest)
                    nearest = d;
            }

            if (float.IsInfinity(nearest))
                return feetGroundY;

            float hitGroundY = origin.y - nearest;
            // 只接受角色可站立高度带内的地面：命中集装箱/墙顶（高于脚下 35cm）直接回退到脚下地面，
            // 不能把滚筒抬到箱顶；深坑（低于脚下 1m）同理回退，避免滚筒被拽下悬崖。
            if (hitGroundY > feetGroundY + 0.35f || hitGroundY < feetGroundY - 1.0f)
                return feetGroundY;
            return hitGroundY;
        }
    }
}
