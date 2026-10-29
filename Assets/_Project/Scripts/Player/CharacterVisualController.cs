using System;
using System.Collections.Generic;
using P1.Core;
using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 精致多部件程序化角色（Squidkid）与武器模型运行时控制器。
    /// 负责：
    /// 1. 人形态（Kid）与乌贼形态（Squid）弹性形变切换（Squash & Stretch）；
    /// 2. 程序化动态微表现：待机呼吸起伏、奔跑身体前倾与转弯侧向倾斜（Lean Springs）、开火后坐力抖动（Recoil）；
    /// 3. 触须发辫次级惯性摆动（Hair Inertia Sway）；
    /// 4. 7 类主武器模型挂载、换装切换与动态构件动画（滚筒旋转、加特林枪管旋转、蓄力狙线圈发光等）；
    /// 5. 墨水储罐背包液面随实际剩余墨水量动态升降。
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

        // 动态微表现状态
        private Vector3 _defaultChestScale = Vector3.one;
        private Vector3 _defaultHeadLocalPos = Vector3.zero;
        private Quaternion _defaultChestRot = Quaternion.identity;
        private Quaternion _defaultLeftHairRot = Quaternion.identity;
        private Quaternion _defaultRightHairRot = Quaternion.identity;
        private Vector3 _tankFillBasePos = Vector3.zero;
        private Vector3 _tankFillBaseScale = Vector3.one;
        private Vector3 _defaultLeftArmPos = Vector3.zero;
        private Quaternion _defaultLeftArmRot = Quaternion.identity;
        private Vector3 _weaponRootBasePos = Vector3.zero;
        private bool _weaponEventsBound;

        private Vector3 _prevWorldPos;
        private float _prevYaw;
        private float _currentPitch;
        private float _pitchVelocity;
        private float _currentRoll;
        private float _rollVelocity;
        private float _currentHairPitch;
        private float _hairPitchVel;
        private float _currentHairRoll;
        private float _hairRollVel;

        // 开火后坐力弹簧
        private float _recoilZ;
        private float _recoilPitch;

        // 乌贼潜行形态平滑过渡 (0 = Kid, 1 = Squid)
        private float _swimTransition01;
        private bool _isCurrentlySwimming;

        // 武器缓存表
        private readonly Dictionary<WeaponKind, GameObject> _weaponMap = new Dictionary<WeaponKind, GameObject>();
        private readonly Dictionary<WeaponKind, Transform> _muzzleMap = new Dictionary<WeaponKind, Transform>();

        private static readonly int EmissionColorProp = Shader.PropertyToID("_EmissionColor");

        public GameObject KidRoot => kidRoot;
        public GameObject SquidRoot => squidRoot;
        public GameObject WeaponRoot => weaponRoot;
        public Transform ActiveMuzzle => _activeMuzzle != null ? _activeMuzzle : transform;
        public WeaponKind CurrentWeaponKind => _currentWeaponKind;

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

        private void Awake()
        {
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
            if (chestTransform != null)
            {
                _defaultChestScale = chestTransform.localScale;
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
            if (leftArmTransform != null)
            {
                _defaultLeftArmPos = leftArmTransform.localPosition;
                _defaultLeftArmRot = leftArmTransform.localRotation;
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
            // 触发后坐力脉冲
            _recoilZ = -0.045f;
            _recoilPitch = 3.8f;
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

            // 调整左手姿态：双枪双手各持一把（外展持枪）；其余单体武器左手向内前托握
            if (leftArmTransform != null)
            {
                if (kind == WeaponKind.Dualies)
                {
                    leftArmTransform.localPosition = new Vector3(-0.24f, 0.08f, 0.06f);
                    leftArmTransform.localRotation = Quaternion.identity;
                }
                else
                {
                    leftArmTransform.localPosition = new Vector3(-0.16f, 0.08f, 0.12f);
                    leftArmTransform.localRotation = Quaternion.Euler(15f, 25f, -10f);
                }
            }
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            UpdateFormMorph(dt);

            if (_swimTransition01 < 0.99f)
            {
                UpdateLocomotionLean(dt);
                UpdateIdleBreathing();
                UpdateHairSway(dt);
                UpdateRecoilKick(dt);
                UpdateTankFill();
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

                    // 乌贼贴水波浪游动
                    if (squidTentaclesRoot != null && _motor != null)
                    {
                        float spd01 = Mathf.Clamp01(_motor.CurrentMoveSpeed / 12f);
                        float wave = Mathf.Sin(Time.time * 16f) * 12f * spd01;
                        squidTentaclesRoot.localRotation = Quaternion.Euler(0f, wave, 0f);
                    }
                }
            }

            if (weaponRoot != null)
            {
                weaponRoot.SetActive(_swimTransition01 < 0.5f);
            }
        }

        /// <summary>
        /// 奔跑前倾与转弯侧倾（Lean Springs）
        /// </summary>
        private void UpdateLocomotionLean(float dt)
        {
            Vector3 curPos = transform.position;
            Vector3 worldVel = (curPos - _prevWorldPos) / Mathf.Max(0.0001f, dt);
            _prevWorldPos = curPos;

            Vector3 localVel = transform.InverseTransformDirection(worldVel);

            float curYaw = transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(_prevYaw, curYaw) / Mathf.Max(0.0001f, dt);
            _prevYaw = curYaw;

            // 前进速度 -> 前倾；转弯角速度 + 横移 -> 侧倾 (Banking)
            float targetPitch = Mathf.Clamp(localVel.z * 1.8f, -8f, 15f);
            float targetRoll = Mathf.Clamp(-yawRate * 0.15f - localVel.x * 1.6f, -12f, 12f);

            _currentPitch = Mathf.SmoothDamp(_currentPitch, targetPitch, ref _pitchVelocity, 0.08f, 100f, dt);
            _currentRoll = Mathf.SmoothDamp(_currentRoll, targetRoll, ref _rollVelocity, 0.08f, 100f, dt);

            if (chestTransform != null)
            {
                chestTransform.localRotation = _defaultChestRot * Quaternion.Euler(_currentPitch, 0f, _currentRoll);
            }
        }

        /// <summary>
        /// 待机呼吸起伏（Chest & Head subtle oscillation）
        /// </summary>
        private void UpdateIdleBreathing()
        {
            if (_motor != null && _motor.CurrentMoveSpeed > 0.8f) return;

            float breath = Mathf.Sin(Time.time * 2.2f);
            if (chestTransform != null)
            {
                chestTransform.localScale = _defaultChestScale + new Vector3(-breath * 0.012f, breath * 0.018f, -breath * 0.012f);
            }
            if (headTransform != null)
            {
                headTransform.localPosition = _defaultHeadLocalPos + Vector3.up * (breath * 0.006f);
            }
        }

        /// <summary>
        /// 触须发辫次级物理摆动（Inertia Sway）
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
        /// 开火后坐力抖动与平滑阻尼回弹
        /// </summary>
        private void UpdateRecoilKick(float dt)
        {
            if (weaponRoot == null) return;

            _recoilZ = Mathf.MoveTowards(_recoilZ, 0f, dt * 0.35f);
            _recoilPitch = Mathf.MoveTowards(_recoilPitch, 0f, dt * 28f);

            weaponRoot.transform.localPosition = _weaponRootBasePos + new Vector3(0f, 0f, _recoilZ);
            weaponRoot.transform.localRotation = Quaternion.Euler(-_recoilPitch, 0f, 0f);
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
                if (_motor != null && _motor.IsGrounded && _motor.CurrentMoveSpeed > 0.1f)
                {
                    rollerDrumTransform.Rotate(Vector3.right, _motor.CurrentMoveSpeed * 280f * dt, Space.Self);
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

                for (int i = 0; i < chargerCoilRenderers.Length; i++)
                {
                    var rend = chargerCoilRenderers[i];
                    if (rend != null && rend.sharedMaterial != null)
                    {
                        rend.sharedMaterial.SetColor(EmissionColorProp, glow);
                    }
                }
            }
        }
    }
}
