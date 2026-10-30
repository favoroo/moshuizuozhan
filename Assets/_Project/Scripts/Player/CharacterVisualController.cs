using System;
using System.Collections.Generic;
using P1.Core;
using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 精致多部件程序化角色（Squidkid）与武器模型运行时控制器。
    /// 现已升级适配「Q 版无腿蛋形」形态：
    /// 1. 贴地步态（EggGaitSolver）：左右摇摆 + 极轻微起伏表现无腿走路，不再上下弹跳；
    /// 2. 果冻弹簧（Jelly Spring）：起跳向上拉伸、落地按速度阻尼压扁缓冲、滞空仰俯；
    /// 3. 前倾与侧倾（Lean Springs）：适配贴地枢轴（y=0.05m），抑制过大倾角放大；
    /// 4. 独立挤压节点（BodySquash）：挤压与呼吸形变完全隔离于武器挂点（Chest）之外，杜绝武器被连带压扁；
    /// 5. 待机自然微呼吸与实例非同步眨眼（站立时不位移，避免原地一蹦一蹦）；
    /// 6. 踩敌方墨水抖动与墨墙攀爬小幅蠕动；
    /// 7. 人形态 ⇄ 乌贼潜行弹性形变过渡；
    /// 8. 7 类主武器换装、双枪/单持姿态适配与专属机构动画（滚筒、加特林、蓄力线圈等）。
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterVisualController : MonoBehaviour
    {
        [Header("形态根节点")]
        [SerializeField] private GameObject kidRoot;
        [SerializeField] private GameObject squidRoot;
        [SerializeField] private GameObject weaponRoot;

        [Header("人形态骨骼节点")]
        [SerializeField] private Transform hipsTransform;
        [SerializeField] private Transform chestTransform;
        [SerializeField] private Transform headTransform;
        [SerializeField] private Transform leftHairPivot;
        [SerializeField] private Transform rightHairPivot;
        [SerializeField] private Transform leftArmTransform;
        [SerializeField] private Transform rightArmTransform;
        [SerializeField] private Transform tankLiquidFill;

        [Header("乌贼形态节点")]
        [SerializeField] private Transform squidBodyTransform;
        [SerializeField] private Transform squidTentaclesRoot;

        [Header("蛋形躯干节点")]
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

        private Transform _lastCachedLeftEye;
        private Transform _lastCachedRightEye;
        private Vector3 _defaultLeftEyeScale = Vector3.one;
        private Vector3 _defaultRightEyeScale = Vector3.one;

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

        // 蛋形步态相位与果冻弹簧状态
        private float _gaitPhase;
        private float _jelly;
        private float _jellyVel;

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

        // 开火后坐力脉冲
        private float _recoilZ;
        private float _recoilPitch;
        private float _recoilYaw;

        // 瞄准俯仰姿态：相机俯仰（正 = 低头）→ 武器挂点承担全部仰角，胸腔只分担一小部分
        private float _aimPitchTarget;
        private float _aimPitchCurrent;
        private float _aimPitchVelocity;
        private readonly Dictionary<GameObject, Quaternion> _weaponModelBaseRots = new Dictionary<GameObject, Quaternion>();

        // 实例独立随机眨眼
        private float _nextBlinkTime = 2.0f;
        private float _blinkStartTime = -1f;

        // 乌贼潜行形态平滑过渡 (0 = Kid, 1 = Squid)
        private float _swimTransition01;
        private bool _isCurrentlySwimming;

        // 潜行贴地下沉量（米）：乌贼展开时按 pop 比例沉入墨面，腹部埋进地面、只露背脊与眼睛，
        // 消除"整只乌贼浮在墨面上"的悬浮感（Splatoon 式半潜观感）
        private const float SquidSubmergeDepth = 0.10f;
        private Vector3 _squidBodyDefaultPos = Vector3.zero;

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

        private void Awake()
        {
            _nextBlinkTime = Time.time + UnityEngine.Random.Range(1.5f, 4.0f);
            _blinkStartTime = -1f;

            CacheDefaultTransforms();
            BuildWeaponMaps();

            if (_motor == null) _motor = GetComponentInParent<InkCharacterMotor>();
            if (_weaponController == null) _weaponController = GetComponentInParent<WeaponController>();

            // 编辑器装配阶段订阅过的事件不会跨 Domain Reload 保留；运行时必须在 Awake 重新订阅，
            // 否则开火后坐力与运行时换枪（EquipWeaponAt）都不会驱动外观。
            SubscribeWeaponEvents();

            // 场景里保存的左手姿态可能是上一版装配结果：运行时按当前武器重新套用一次姿态与激活状态
            if (_weaponMap.Count > 0)
            {
                EquipWeaponKind(_currentWeaponKind);
            }
        }

        private void OnEnable()
        {
            SubscribeWeaponEvents();
        }

        private void OnDisable()
        {
            UnsubscribeWeaponEvents();
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

            // 缓存眼睛默认尺寸，防止眨眼循环缩放累乘漂移
            if (leftEyeTransform != null && leftEyeTransform != _lastCachedLeftEye)
            {
                _lastCachedLeftEye = leftEyeTransform;
                _defaultLeftEyeScale = leftEyeTransform.localScale;
            }
            if (rightEyeTransform != null && rightEyeTransform != _lastCachedRightEye)
            {
                _lastCachedRightEye = rightEyeTransform;
                _defaultRightEyeScale = rightEyeTransform.localScale;
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
            // 触发后坐力脉冲：幅度按当前武器手感配置（P16），并加入轻微随机 Yaw，
            // 否则连发时每一发都沿完全相同的轨迹回弹，看起来像机械臂而不是手中武器。
            WeaponConfigSO config = _weaponController != null ? _weaponController.Config : null;
            WeaponFeelParams feel = WeaponFeelProfile.Resolve(config);

            _recoilZ = -feel.RecoilKickMeters;
            _recoilPitch = feel.RecoilPitchDegrees + UnityEngine.Random.Range(-0.6f, 0.6f);
            _recoilYaw = UnityEngine.Random.Range(-0.9f, 0.9f);
        }

        public void EquipWeaponKind(WeaponKind kind)
        {
            _currentWeaponKind = kind;

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

            // 调整左手姿态：双枪双手各持一把（外展平行持枪）；其余单体武器左手向内前托握。
            // 基于装配器生成的 _defaultLeftArmPos 做相对偏移，杜绝写死绝对坐标破坏体型一致性；
            // 且严格保证双枪与单持武器的 localPosition.x 不相等（单测断言）。
            if (leftArmTransform != null)
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

            UpdateFormMorph(dt);
            UpdateAimPose(dt);

            if (_swimTransition01 < 0.99f)
            {
                UpdateBodyPose(dt);
                UpdateHairSway(dt);
                UpdateRecoilKick(dt);
                UpdateTankFill();
            }
            else
            {
                // 潜行期间保持采样锚点同步，避免浮出墨面第一帧由于大位移产生误触发瞬移或着地震荡
                _prevWorldPos = transform.position;
                _planarSpeed = 0f;
                _prevYaw = transform.eulerAngles.y;
                _wasGrounded = _motor == null || _motor.IsGrounded;
            }

            UpdateWeaponMechanics(dt);
        }

        /// <summary>
        /// 人形态 ⇄ 乌贼形态弹性形变（Squash & Stretch）过渡
        /// </summary>
        private void UpdateFormMorph(float dt)
        {
            bool wantsSwim = _motor != null && _motor.IsSwimming;
            _isCurrentlySwimming = wantsSwim;

            float target = wantsSwim ? 1f : 0f;
            _swimTransition01 = Mathf.MoveTowards(_swimTransition01, target, dt * 10f);

            if (kidRoot != null)
            {
                kidRoot.SetActive(_swimTransition01 < 0.98f);
                if (_swimTransition01 < 0.98f)
                {
                    // 下潜时蓄力压扁，浮出时冲顶弹性拉伸
                    float t = _swimTransition01;
                    float sy = Mathf.Lerp(1f, 0.15f, t);
                    float sxz = Mathf.Lerp(1f, 1.25f, t);
                    kidRoot.transform.localScale = new Vector3(sxz, sy, sxz);
                }
            }

            if (squidRoot != null)
            {
                squidRoot.SetActive(_swimTransition01 > 0.02f);
                if (_swimTransition01 > 0.02f)
                {
                    float t = _swimTransition01;
                    float pop = Mathf.SmoothStep(0.2f, 1.0f, t);
                    squidRoot.transform.localScale = new Vector3(pop, pop, pop);

                    // 潜行贴地：身体随展开比例沉入墨面（SquidRoot 被缩放 pop，故世界下沉量随 pop 平滑加速），
                    // 展开过程呈现"从墨里钻出来再沉回去"，而不是凭空放大悬浮
                    if (squidBodyTransform != null)
                    {
                        squidBodyTransform.localPosition = _squidBodyDefaultPos
                            + Vector3.down * (SquidSubmergeDepth * pop);
                    }

                    // 乌贼贴水波浪游动
                    if (squidTentaclesRoot != null && _motor != null)
                    {
                        float spd01 = Mathf.Clamp01(_motor.CurrentMoveSpeed / 12f);
                        float wave = Mathf.Sin(Time.time * 16f) * 12f * spd01;
                        squidTentaclesRoot.localRotation = Quaternion.Euler(0f, wave, 0f);
                    }
                }
                else if (squidBodyTransform != null)
                {
                    // 人形态期间恢复默认高度，保证下一次潜入从同一基准展开
                    squidBodyTransform.localPosition = _squidBodyDefaultPos;
                }
            }

            if (weaponRoot != null)
            {
                weaponRoot.SetActive(_swimTransition01 < 0.5f);
            }
        }

        /// <summary>
        /// 无腿蛋形角色程序化动作管线核心：
        /// 1. 速度与着地边沿采样（含传送跳变重置）；
        /// 2. 贴地步态（EggGaitSolver 采样轻微起伏 Bob、左右摇摆 Rock、少量挤压 Squash、手臂反相摆动 ArmSwing）；
        /// 3. 果冻弹簧（起跳瞬间拉伸、真正有下落速度的落地才压扁缓冲、滞空仰俯）；
        /// 4. 躯干前倾与转弯侧向倾斜（针对贴地枢轴下调角度倍率的 Lean Springs）；
        /// 5. 待机呼吸与实例异步自然眨眼（呼吸只做缩放，不做位移）；
        /// 6. 踩敌方墨水高频挣扎抖动；
        /// 7. 墨墙攀爬蠕动；
        /// 8. 姿态与挤压写回（所有形变全部作用于 bodySquashTransform 节点，严禁缩放 Chest 以免武器变形）。
        /// </summary>
        private void UpdateBodyPose(float dt)
        {
            // --- 1. 速度与着地采样（每帧只算一次） ---
            Vector3 curPos = transform.position;
            Vector3 worldVel = (curPos - _prevWorldPos) / Mathf.Max(0.0001f, dt);

            // 传送/复活导致的速度尖峰（> 50 m/s）：直接复位，避免误判为超高速起跳/落地
            bool teleported = worldVel.sqrMagnitude > 2500f;
            bool grounded = _motor == null || _motor.IsGrounded;

            if (teleported)
            {
                _prevWorldPos = curPos;
                worldVel = Vector3.zero;
                _verticalVel = 0f;
                _wasGrounded = grounded;
                _jelly = 0f;
                _jellyVel = 0f;
                _lastAirVelY = 0f;
                _planarSpeed = 0f;
            }
            _prevWorldPos = curPos;
            _verticalVel = worldVel.y;
            // 关键：步态必须用真实位移速度，而不是 motor.CurrentMoveSpeed（那是当前形态的速度上限，站立时也为 6）。
            // 旧写法导致角色一落地就永远在走弹跳步态，原地站着也一蹦一蹦。
            _planarSpeed = new Vector2(worldVel.x, worldVel.z).magnitude;

            Vector3 localVel = transform.InverseTransformDirection(worldVel);
            float curYaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(_prevYaw, curYaw) / Mathf.Max(0.0001f, dt);
            _prevYaw = curYaw;

            // --- 2. 贴地步态计算 ---
            float speed = _planarSpeed;
            float speed01 = EggGaitSolver.Speed01(speed);
            // 步态权重：仅在地面且高于静止阈值时生效；用 SmoothStep 让起步/停步过渡柔和，
            // 站立（含 Bot 交火时的小幅走位）不会触发持续的"原地弹跳"。
            float gaitWeight = grounded
                ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((speed - EggGaitSolver.MoveSpeedThreshold) / 2.2f))
                : 0f;
            if (gaitWeight > 0.02f)
            {
                _gaitPhase += Mathf.PI * EggGaitSolver.CadenceHz(speed01) * dt;
            }

            EggGaitPose gait = EggGaitSolver.Sample(speed01, _gaitPhase);
            float bob = gait.BobY * gaitWeight;
            float squashY = Mathf.Lerp(1f, gait.SquashY, gaitWeight);
            float squashXZ = Mathf.Lerp(1f, gait.SquashXZ, gaitWeight);
            float rock = gait.RockDeg * gaitWeight;
            float armSwing = gait.ArmSwingDeg * gaitWeight;

            // --- 3. 起跳 / 滞空 / 落地（果冻弹簧与滞空仰俯） ---
            if (_wasGrounded && !grounded)
            {
                // 着地 -> 离地：起跳瞬间向上拉伸
                _jellyVel += 3.2f;
            }
            else if (!_wasGrounded && grounded)
            {
                // 离地 -> 着地：只有真正下落（不是贴地被 isGrounded 抖动）才触发压扁缓冲，
                // 否则角色站着会每隔几帧被弹簧弹一下，看起来像原地蹦。
                float fallSpeed = Mathf.Max(0f, -_lastAirVelY);
                if (fallSpeed > 1.2f)
                {
                    _jellyVel -= Mathf.Lerp(0.8f, 6.0f, Mathf.Clamp01(fallSpeed / 14f));
                }
            }
            _wasGrounded = grounded;

            float airPitch = 0f;
            if (!grounded)
            {
                _lastAirVelY = _verticalVel;
                // 滞空期间：上升抬头、下落低头
                airPitch = Mathf.Clamp(-_verticalVel * 1.2f, -10f, 10f);
            }

            // 二阶果冻弹簧模拟 (Jelly Spring: K=260, D=18)
            const float JellyK = 260f;
            const float JellyD = 18f;
            _jellyVel += (-JellyK * _jelly - JellyD * _jellyVel) * dt;
            _jelly += _jellyVel * dt;
            _jelly = Mathf.Clamp(_jelly, -0.22f, 0.16f);

            // --- 4. 前倾与侧倾（Lean Springs） ---
            // 蛋形角色躯干枢轴贴地（CharacterMetrics.ChestPivotY = 0.05m），
            // 相比旧版人形折叠，贴地倾斜会产生 2.5 倍视觉放大幅度，因此大幅下调目标角度倍率
            float targetPitch = Mathf.Clamp(localVel.z * 1.0f, -5f, 9f);
            float targetRoll = Mathf.Clamp(-yawRate * 0.10f - localVel.x * 1.0f, -8f, 8f);

            _currentPitch = Mathf.SmoothDamp(_currentPitch, targetPitch, ref _pitchVelocity, 0.08f, 100f, dt);
            _currentRoll = Mathf.SmoothDamp(_currentRoll, targetRoll, ref _rollVelocity, 0.08f, 100f, dt);

            // --- 5. 待机呼吸起伏与眨眼 ---
            // 呼吸只做极轻微的缩放，不做上下位移：蛋形角色贴地站立时必须纹丝不动。
            if (speed < EggGaitSolver.MoveSpeedThreshold && grounded)
            {
                float breath = Mathf.Sin(Time.time * 2.2f);
                squashY += breath * 0.006f;
                squashXZ -= breath * 0.003f;
            }
            UpdateBlink(dt);

            // --- 6. 踩敌墨高频受击抖动 ---
            float grip = _motor != null ? _motor.EnemyInkGrip01 : 0f;
            if (grip > 0.001f)
            {
                rock += Mathf.Sin(Time.time * 38f) * grip * 3.5f;
                squashY -= grip * 0.05f;
            }

            // --- 7. 墨墙攀爬小幅蠕动 ---
            bool isWallClimbing = _motor != null && _motor.IsWallClimbing;
            if (isWallClimbing)
            {
                // 攀爬时步态权重强制为 0，杜绝在垂直墙面上横向乱弹；改为 4Hz 垂直蠕动
                bob = 0f;
                rock = 0f;
                armSwing = 0f;
                squashY = 1f + Mathf.Sin(Time.time * 22f) * 0.02f;
                squashXZ = 1f;
            }

            // --- 8. 写回骨骼与挤压节点 ---
            // 关键：所有挤压与呼吸形变必须作用于 bodySquashTransform（武器挂载在 chest 下，缩放 chest 会把枪压扁）
            Transform squash = bodySquashTransform != null ? bodySquashTransform : chestTransform;
            if (squash != null)
            {
                squash.localPosition = _defaultBodySquashPos + Vector3.up * bob;
                float sy = Mathf.Clamp(squashY * (1f + _jelly), 0.55f, 1.40f);
                float sxz = Mathf.Clamp(squashXZ * (1f - _jelly * 0.5f), 0.60f, 1.35f);
                // 严格基于 _defaultBodySquashScale 乘算，绝不每帧累乘
                squash.localScale = Vector3.Scale(_defaultBodySquashScale, new Vector3(sxz, sy, sxz));
            }

            // 瞄准俯仰只让胸腔分担一小部分（默认 18%），角色保持直立：抬头不会整只人后仰
            PlayerConfigSO poseConfig = _motor != null ? _motor.Config : null;
            float chestAimWeight = poseConfig != null
                ? Mathf.Clamp01(poseConfig.aimPoseChestPitchWeight)
                : 0.18f;
            float finalPitch = _currentPitch + airPitch + _aimPitchCurrent * chestAimWeight;
            float finalRoll = _currentRoll + rock;
            if (chestTransform != null)
            {
                chestTransform.localRotation = _defaultChestRot * Quaternion.Euler(finalPitch, 0f, finalRoll);
            }

            // 双臂基于持枪基础位姿叠加反相摆荡，_leftArmWeaponPoseRot 由武器装备决定，不可被覆盖
            if (leftArmTransform != null)
            {
                leftArmTransform.localRotation = _leftArmWeaponPoseRot * Quaternion.Euler(armSwing, 0f, 0f);
            }
            if (rightArmTransform != null)
            {
                rightArmTransform.localRotation = _defaultRightArmRot * Quaternion.Euler(-armSwing, 0f, 0f);
            }
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
                _blinkStartTime = Time.time;
                _nextBlinkTime = Time.time + UnityEngine.Random.Range(2.5f, 5.0f);
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
        /// 触须发辫次级物理摆动（Inertia Sway）
        /// （指向蛋形头顶墨焰时，自动呈现墨焰惯性抖动）
        /// </summary>
        private void UpdateHairSway(float dt)
        {
            if (leftHairPivot == null && rightHairPivot == null) return;

            // 触须发辫受前倾与侧向力反向摆荡
            float targetHairPitch = Mathf.Clamp(-_currentPitch * 0.8f, -18f, 10f);
            float targetHairRoll = Mathf.Clamp(_currentRoll * 0.9f, -14f, 14f);

            _currentHairPitch = Mathf.SmoothDamp(_currentHairPitch, targetHairPitch, ref _hairPitchVel, 0.12f, 120f, dt);
            _currentHairRoll = Mathf.SmoothDamp(_currentHairRoll, targetHairRoll, ref _hairRollVel, 0.12f, 120f, dt);

            Quaternion swayRot = Quaternion.Euler(_currentHairPitch, 0f, _currentHairRoll);
            if (leftHairPivot != null) leftHairPivot.localRotation = _defaultLeftHairRot * swayRot;
            if (rightHairPivot != null) rightHairPivot.localRotation = _defaultRightHairRot * swayRot;
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
            modelTransform.localRotation = baseRot * Quaternion.Euler(_aimPitchCurrent * weight, 0f, 0f);
        }

        /// <summary>
        /// 开火后坐力抖动与平滑阻尼回弹
        /// </summary>
        private void UpdateRecoilKick(float dt)
        {
            if (weaponRoot == null) return;

            _recoilZ = Mathf.MoveTowards(_recoilZ, 0f, dt * 0.35f);
            _recoilPitch = Mathf.MoveTowards(_recoilPitch, 0f, dt * 28f);
            _recoilYaw = Mathf.MoveTowards(_recoilYaw, 0f, dt * 22f);

            weaponRoot.transform.localPosition = _weaponRootBasePos + new Vector3(0f, 0f, _recoilZ);
            weaponRoot.transform.localRotation = Quaternion.Euler(-_recoilPitch, _recoilYaw, 0f);
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
            // 1. 滚筒滚轴旋转
            if (_currentWeaponKind == WeaponKind.Roller && rollerDrumTransform != null)
            {
                if (_motor != null && _motor.IsGrounded && _planarSpeed > 0.1f)
                {
                    rollerDrumTransform.Rotate(Vector3.right, _planarSpeed * 280f * dt, Space.Self);
                }
            }

            // 2. 加特林枪管旋转
            if (_currentWeaponKind == WeaponKind.Splatling && splatlingBarrelsTransform != null)
            {
                bool isOperating = _weaponController != null && (_weaponController.IsCharging || _weaponController.IsPerformingSpecial);
                float spinSpeed = isOperating ? 900f : 0f;
                if (spinSpeed > 0f)
                {
                    splatlingBarrelsTransform.Rotate(Vector3.forward, spinSpeed * dt, Space.Self);
                }
            }

            // 3. 蓄力狙发光线圈
            if (_currentWeaponKind == WeaponKind.Charger && chargerCoilRenderers != null && chargerCoilRenderers.Length > 0)
            {
                float charge = _weaponController != null ? _weaponController.Charge01 : 0f;
                Color baseGlow = _team == Team.TeamA ? new Color(1f, 0.45f, 0.08f) : new Color(0.16f, 0.55f, 1f);
                Color glow = baseGlow * (0.2f + charge * 2.8f);

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
    }
}
