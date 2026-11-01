using System.Collections.Generic;
using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 单把武器的手臂持握姿态（全部为骨骼局部旋转，装配期/运行期解算一次、运行期直接套用）：
    /// 肩枢轴的 localRotation（Chest 空间）+ 肘枢轴的 localRotation（肩空间）的左右两侧组合。
    /// LeftShoulderLocalPos / RightShoulderLocalPos 用于双枪左肩外展等基于体型的相对位移，
    /// 保证姿态旋转与枢轴位置永远成套，不会出现"肩挪了但角度还按旧位置算"的错位。
    /// </summary>
    public struct ArmHoldPose
    {
        public Quaternion RightShoulder;
        public Quaternion RightElbow;
        public Quaternion LeftShoulder;
        public Quaternion LeftElbow;
        public Vector3 RightShoulderLocalPos;
        public Vector3 LeftShoulderLocalPos;

        public ArmHoldPose(
            Quaternion rightShoulder, Quaternion rightElbow,
            Quaternion leftShoulder, Quaternion leftElbow,
            Vector3 rightShoulderLocalPos, Vector3 leftShoulderLocalPos)
        {
            RightShoulder = rightShoulder;
            RightElbow = rightElbow;
            LeftShoulder = leftShoulder;
            LeftElbow = leftElbow;
            RightShoulderLocalPos = rightShoulderLocalPos;
            LeftShoulderLocalPos = leftShoulderLocalPos;
        }
    }

    /// <summary>
    /// 两骨解析 IK（上臂 → 肘 → 前臂），只解一次姿态、不参与每帧热路径。
    /// 骨骼约定与 <see cref="CharacterVisualAssembler"/> 的装配一致：
    /// 骨链静止指向局部 -Y，肘部绕局部 X 负角向前卷，肘枢轴挂在肩枢轴下 -upperLen 处，
    /// 手（骨端）挂在肘枢轴下 -lowerLen 处。
    ///
    /// 解算步骤：由余弦定理求肘内角 → 沿肩→目标连线偏移出肘点（偏移方向由 pole 决定，
    /// 即"肘往哪边拐"）→ 用 "前臂方向 × 上臂方向" 构造肩的完整姿态（局部 +X 对准两骨平面法线，
    /// 使肘部仍能用纯 X 角折叠）→ 肘角 = -(两骨夹角)。
    /// </summary>
    public static class TwoBoneArmSolver
    {
        public static void Solve(
            Vector3 shoulderPos,
            Vector3 target,
            float upperLen,
            float lowerLen,
            Vector3 pole,
            out Quaternion shoulderRot,
            out Quaternion elbowRot)
        {
            Vector3 toTarget = target - shoulderPos;
            float rawDist = toTarget.magnitude;
            if (rawDist < 0.0001f)
                toTarget = Vector3.down * 0.0001f;
            else
                toTarget = toTarget / rawDist;

            float maxReach = Mathf.Max(0.0001f, upperLen + lowerLen - 0.001f);
            float minReach = Mathf.Abs(upperLen - lowerLen) + 0.001f;
            float dist = Mathf.Clamp(rawDist < 0.0001f ? maxReach : rawDist, minReach, maxReach);
            if (rawDist < 0.0001f) toTarget = Vector3.down;
            Vector3 dir = toTarget.normalized;

            // 肘内角（两骨夹角 = π - 内角）
            float cosInterior = (upperLen * upperLen + lowerLen * lowerLen - dist * dist)
                                / (2f * upperLen * lowerLen);
            float interior = Mathf.Acos(Mathf.Clamp(cosInterior, -1f, 1f));
            float bend = Mathf.PI - interior;

            // 肘点在"肩→目标"轴上的投影 + 由 pole 决定的侧向偏移
            float along = (upperLen * upperLen - lowerLen * lowerLen + dist * dist) / (2f * dist);
            float sideOffset = Mathf.Sqrt(Mathf.Max(0f, upperLen * upperLen - along * along));
            Vector3 poleDir = pole - dir * Vector3.Dot(pole, dir);
            if (poleDir.sqrMagnitude < 1e-6f)
            {
                poleDir = Vector3.Cross(dir, Vector3.right);
                if (poleDir.sqrMagnitude < 1e-6f) poleDir = Vector3.Cross(dir, Vector3.forward);
            }
            poleDir.Normalize();

            Vector3 elbowPos = shoulderPos + dir * along + poleDir * sideOffset;
            Vector3 upperDir = (elbowPos - shoulderPos).normalized;
            Vector3 foreDir = (target - elbowPos).normalized;

            // 两骨平面法线 = 前臂 × 上臂；把它作为肩的局部 +X，
            // 这样肘部绕局部 X 的负角折叠方向与 authored 约定（-35° 前卷）一致。
            Vector3 hinge = Vector3.Cross(foreDir, upperDir);
            if (hinge.sqrMagnitude < 1e-6f) hinge = Vector3.Cross(upperDir, Vector3.forward);
            hinge.Normalize();
            Vector3 zAxis = Vector3.Cross(hinge, -upperDir);
            shoulderRot = Quaternion.LookRotation(zAxis, -upperDir);

            elbowRot = Quaternion.Euler(-bend * Mathf.Rad2Deg, 0f, 0f);
        }

        /// <summary>把姿态表写进骨骼（肩旋转 / 肘旋转 / 扁平的两侧枢轴位置）。</summary>
        public static void Apply(
            Transform rightArm, Transform rightElbow,
            Transform leftArm, Transform leftElbow,
            ArmHoldPose pose)
        {
            if (rightArm != null)
            {
                rightArm.localPosition = pose.RightShoulderLocalPos;
                rightArm.localRotation = pose.RightShoulder;
            }
            if (rightElbow != null) rightElbow.localRotation = pose.RightElbow;
            if (leftArm != null)
            {
                leftArm.localPosition = pose.LeftShoulderLocalPos;
                leftArm.localRotation = pose.LeftShoulder;
            }
            if (leftElbow != null) leftElbow.localRotation = pose.LeftElbow;
        }
    }

    /// <summary>
    /// 每把武器的持握数据：右手（扳机握把）目标手位 + 左手是否托握 / 双持。
    /// 位置为 KidRoot 空间（脚底 y=0），与武器挂点表同一坐标系，方便直接落到
    /// <see cref="CharacterMetrics.WeaponMountOffset"/> 的替代锚点上。
    /// </summary>
    public static class WeaponHoldTargets
    {
        /// <summary>右手（握把）手位 —— 适度向右外展，避免背后视角被角色后脑勺与墨水罐完全挡死。</summary>
        public static Vector3 RightHand(WeaponKind kind)
        {
            switch (kind)
            {
                // 右手统一抬到胸腹之间偏右前方（肩高 0.746，手臂总长 0.325）：手位离肩 0.26~0.30m，
                // 肘弯 40~65°，向右外展 5~7cm，从正背后与过肩视角看过去枪身清晰可辨，不挡中线准星。
                case WeaponKind.Charger: return new Vector3(0.165f, 0.590f, 0.260f);
                case WeaponKind.Roller: return new Vector3(0.050f, 0.560f, 0.160f);
                case WeaponKind.Slosher: return new Vector3(0.155f, 0.540f, 0.220f);
                case WeaponKind.Splatling: return new Vector3(0.160f, 0.570f, 0.230f);
                case WeaponKind.Blaster: return new Vector3(0.170f, 0.580f, 0.245f);
                case WeaponKind.Dualies: return new Vector3(0.160f, 0.580f, 0.225f);
                default: return new Vector3(0.165f, 0.585f, 0.250f);
            }
        }

        /// <summary>左手托握/持枪目标（双枪、滚筒、加特林使用；其余武器左手自然放松备战）。</summary>
        public static Vector3 LeftHand(WeaponKind kind)
            => kind == WeaponKind.Dualies
                ? new Vector3(-0.160f, 0.580f, 0.225f)
                : Vector3.zero;

        /// <summary>右手握把部件名（按武器模型实际几何，枪型再变手也落在扳机上）。</summary>
        public static string RightGripName(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Roller: return "GripRubberRear";
                case WeaponKind.Splatling: return "RearGrip";
                case WeaponKind.Slosher: return "HandleRear";
                default: return "Grip";
            }
        }

        /// <summary>左手是否真的抓在武器上（双枪持左枪、滚筒握杆；其余武器左手自然放松备战）。</summary>
        public static bool HasLeftWeaponSupport(WeaponKind kind)
            => kind == WeaponKind.Dualies || kind == WeaponKind.Roller;

        /// <summary>
        /// 滚筒左手的托握点（武器模型局部空间，未经模型缩放）：
        /// 后握把 z=-0.10、前握把 z=+0.18；取后握把前 4cm（z=-0.06），双手一前一后夹住握把根部。
        /// 这样两只手都在肩部可达范围内，推地/挥击时随模型一起走。
        /// </summary>
        public static Vector3 LeftSupportLocal(WeaponKind kind)
            => kind == WeaponKind.Roller ? new Vector3(0f, 0f, -0.06f) : Vector3.zero;

        /// <summary>肘部朝向：外侧偏下偏后，避免上臂贴胸、肘尖戳进衣服。</summary>
        public static Vector3 RightPole => new Vector3(0.80f, -0.85f, -0.40f);
        public static Vector3 LeftPole => new Vector3(-0.80f, -0.85f, -0.40f);

        /// <summary>
        /// 不持枪那一侧手的自然待机姿态（肩 + 肘，Chest/肩局部角）。
        /// 消除旧版小臂向肚子内卷 50° 导致的痉挛假肢感：上臂顺重力自然下垂微外展，肘部微屈 20°，手掌放松垂于大腿左前方。
        /// </summary>
        public static void LeftRelaxedPose(WeaponKind kind, out Quaternion shoulder, out Quaternion elbow)
        {
            float shoulderPitch = 6f;   // 上臂顺重力自然下垂，微向后
            float shoulderRoll = -10f;  // 略微外展，避开衣服短裤侧缘
            float elbowPitch = -20f;    // 肘部自然微屈 20°

            shoulder = Quaternion.Euler(shoulderPitch, 0f, shoulderRoll);
            elbow = Quaternion.Euler(elbowPitch, 0f, 0f);
        }
    }

    /// <summary>
    /// 持握姿态解算（装配器与运行期共用）：先把每把武器的握把平移到手位，再用两骨 IK 反解肩/肘。
    /// 关键点：
    /// 1) 手位表 <see cref="WeaponHoldTargets"/> 定义在 KidRoot 空间（脚底 y=0），
    ///    换算到 Chest 空间只需减去脊柱链高度 <see cref="ChestChainY"/>；
    /// 2) 武器归位以"真实握把部件"为准（Grip / RearGrip / GripRubberRear / HandleRear），
    ///    而不是模型原点 —— 枪身形状再变，手也永远落在扳机握把上；
    /// 3) 双枪逐把平移左右手枪；滚筒左手托在前握把/杆中间；其余武器左手自然备战。
    /// </summary>
    public static class WeaponHoldRig
    {
        /// <summary>KidRoot 脚底到 Chest 的骨骼链高度（Hips + Spine + Chest）。</summary>
        public const float ChestChainY = CharacterMetrics.HipBoneY
            + CharacterMetrics.SpineLocalY + CharacterMetrics.ChestLocalY;

        /// <summary>双枪左肩的额外外展位移（与右手对称持枪时给肘部留出空间）。</summary>
        private static readonly Vector3 DualLeftShoulderOffset = new Vector3(-0.035f, 0f, 0.010f);

        public static Dictionary<WeaponKind, ArmHoldPose> Build(
            Transform weaponRoot,
            Transform leftArm, Transform rightArm, Transform leftElbow, Transform rightElbow,
            GameObject shooterGo, GameObject rollerGo, GameObject chargerGo, GameObject dualiesGo,
            GameObject slosherGo, GameObject blasterGo, GameObject splatlingGo,
            float chainY = ChestChainY)
        {
            // ---- 1. 握把锚定（模型根无旋转，只有统一缩放，直接平移 localPosition 即可）----
            AnchorWeaponGrip(weaponRoot, shooterGo, WeaponKind.Shooter);
            AnchorWeaponGrip(weaponRoot, rollerGo, WeaponKind.Roller);
            AnchorWeaponGrip(weaponRoot, chargerGo, WeaponKind.Charger);
            AnchorWeaponGrip(weaponRoot, blasterGo, WeaponKind.Blaster);
            AnchorWeaponGrip(weaponRoot, splatlingGo, WeaponKind.Splatling);
            AnchorWeaponGrip(weaponRoot, slosherGo, WeaponKind.Slosher);

            if (dualiesGo != null)
            {
                Transform rightPistol = FindChildByName(dualiesGo.transform, "RightPistol");
                Transform leftPistol = FindChildByName(dualiesGo.transform, "LeftPistol");
                AnchorGripToTarget(weaponRoot, rightPistol, "Grip", WeaponHoldTargets.RightHand(WeaponKind.Dualies));
                AnchorGripToTarget(weaponRoot, leftPistol, "Grip", WeaponHoldTargets.LeftHand(WeaponKind.Dualies));
            }

            // ---- 2. 姿态反解 ----
            Vector3 rightShoulderPos = rightArm != null ? rightArm.localPosition : Vector3.zero;
            Vector3 leftShoulderPos = leftArm != null ? leftArm.localPosition : Vector3.zero;
            Transform chest = rightArm != null ? rightArm.parent : null;

            var poses = new Dictionary<WeaponKind, ArmHoldPose>();
            WeaponKind[] kinds =
            {
                WeaponKind.Shooter, WeaponKind.Roller, WeaponKind.Charger, WeaponKind.Dualies,
                WeaponKind.Slosher, WeaponKind.Blaster, WeaponKind.Splatling
            };

            for (int i = 0; i < kinds.Length; i++)
            {
                WeaponKind kind = kinds[i];
                Vector3 rightTarget = ToChestSpace(WeaponHoldTargets.RightHand(kind), chainY);

                TwoBoneArmSolver.Solve(
                    rightShoulderPos, rightTarget,
                    CharacterMetrics.UpperArmLength, CharacterMetrics.ForearmLength,
                    WeaponHoldTargets.RightPole,
                    out Quaternion rightShoulder, out Quaternion rightElbowRot);

                if (kind == WeaponKind.Dualies)
                {
                    Vector3 dualLeftShoulderPos = leftShoulderPos + DualLeftShoulderOffset;
                    Vector3 leftTarget = ToChestSpace(WeaponHoldTargets.LeftHand(kind), chainY);
                    TwoBoneArmSolver.Solve(
                        dualLeftShoulderPos, leftTarget,
                        CharacterMetrics.UpperArmLength, CharacterMetrics.ForearmLength,
                        WeaponHoldTargets.LeftPole,
                        out Quaternion leftShoulder, out Quaternion leftElbowRot);

                    poses[kind] = new ArmHoldPose(
                        rightShoulder, rightElbowRot, leftShoulder, leftElbowRot,
                        rightShoulderPos, dualLeftShoulderPos);
                }
                else if (WeaponHoldTargets.HasLeftWeaponSupport(kind))
                {
                    Transform model = null;
                    if (kind == WeaponKind.Roller && rollerGo != null) model = rollerGo.transform;
                    else if (kind == WeaponKind.Splatling && splatlingGo != null) model = splatlingGo.transform;

                    Vector3 leftTarget = ResolveLeftSupportTarget(kind, model, chest, chainY);
                    TwoBoneArmSolver.Solve(
                        leftShoulderPos, leftTarget,
                        CharacterMetrics.UpperArmLength, CharacterMetrics.ForearmLength,
                        WeaponHoldTargets.LeftPole,
                        out Quaternion leftShoulder, out Quaternion leftElbowRot);

                    poses[kind] = new ArmHoldPose(
                        rightShoulder, rightElbowRot, leftShoulder, leftElbowRot,
                        rightShoulderPos, leftShoulderPos);
                }
                else
                {
                    WeaponHoldTargets.LeftRelaxedPose(kind, out Quaternion leftShoulder, out Quaternion leftElbowRot);
                    poses[kind] = new ArmHoldPose(
                        rightShoulder, rightElbowRot, leftShoulder, leftElbowRot,
                        rightShoulderPos, leftShoulderPos);
                }
            }

            return poses;
        }

        /// <summary>
        /// 运行期逐帧把手臂重新指到武器实际握把（瞄准俯仰 / 后坐力 / 滚筒挥击都会移动握把）。
        /// 返回 false 表示该武器没有可用的握把变换，调用方保留静态姿态。
        /// </summary>
        public static bool SolveRightHand(
            Transform chest, Transform rightArm, Vector3 gripWorld, out Quaternion shoulderRot, out Quaternion elbowRot)
        {
            shoulderRot = Quaternion.identity;
            elbowRot = Quaternion.identity;
            if (chest == null || rightArm == null) return false;

            Vector3 target = chest.InverseTransformPoint(gripWorld);
            TwoBoneArmSolver.Solve(
                rightArm.localPosition, target,
                CharacterMetrics.UpperArmLength, CharacterMetrics.ForearmLength,
                WeaponHoldTargets.RightPole,
                out shoulderRot, out elbowRot);
            return true;
        }

        /// <summary>运行期逐帧把左手指到武器托握点（滚筒杆 / 双枪左手枪握把）。</summary>
        public static bool SolveLeftHand(
            Transform chest, Transform leftArm, Vector3 gripWorld, out Quaternion shoulderRot, out Quaternion elbowRot)
        {
            shoulderRot = Quaternion.identity;
            elbowRot = Quaternion.identity;
            if (chest == null || leftArm == null) return false;

            Vector3 target = chest.InverseTransformPoint(gripWorld);
            TwoBoneArmSolver.Solve(
                leftArm.localPosition, target,
                CharacterMetrics.UpperArmLength, CharacterMetrics.ForearmLength,
                WeaponHoldTargets.LeftPole,
                out shoulderRot, out elbowRot);
            return true;
        }

        /// <summary>把武器模型的托握局部点换算成 Chest 空间（装配期解算用）。</summary>
        public static Vector3 ResolveLeftSupportTarget(
            WeaponKind kind, Transform model, Transform chest, float chainY)
        {
            if (kind == WeaponKind.Dualies)
            {
                Transform leftPistol = model != null ? FindChildByName(model, "LeftPistol") : null;
                Transform grip = leftPistol != null ? FindChildByName(leftPistol, "Grip") : null;
                if (grip != null)
                {
                    return chest != null
                        ? chest.InverseTransformPoint(grip.position)
                        : ToChestSpace(WeaponHoldTargets.LeftHand(kind), chainY);
                }
                return ToChestSpace(WeaponHoldTargets.LeftHand(kind), chainY);
            }

            if (kind == WeaponKind.Splatling && model != null)
            {
                Transform fwdGrip = FindChildByName(model, "ForwardVerticalGrip");
                if (fwdGrip != null)
                {
                    return chest != null
                        ? chest.InverseTransformPoint(fwdGrip.position)
                        : ToChestSpace(fwdGrip.position, chainY);
                }
            }

            if (model != null)
            {
                Vector3 world = model.TransformPoint(WeaponHoldTargets.LeftSupportLocal(kind));
                if (chest != null) return chest.InverseTransformPoint(world);
                return ToChestSpace(world, chainY);
            }

            return Vector3.zero;
        }

        public static Vector3 ToChestSpace(Vector3 kidSpacePoint, float chainY)
            => new Vector3(kidSpacePoint.x, kidSpacePoint.y - chainY, kidSpacePoint.z);

        public static Transform FindChildByName(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildByName(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        private static void AnchorWeaponGrip(Transform weaponRoot, GameObject model, WeaponKind kind)
            => AnchorGripToTarget(weaponRoot, model != null ? model.transform : null,
                WeaponHoldTargets.RightGripName(kind), WeaponHoldTargets.RightHand(kind));

        private static void AnchorGripToTarget(Transform weaponRoot, Transform modelRoot, string gripName, Vector3 target)
        {
            if (weaponRoot == null || modelRoot == null) return;

            Transform grip = FindChildByName(modelRoot, gripName);
            if (grip == null)
            {
                Debug.LogWarning($"[WeaponHoldRig] 武器 {modelRoot.name} 未找到握把部件 '{gripName}'，保持原挂点。");
                return;
            }

            Vector3 gripInWeaponRoot = weaponRoot.InverseTransformPoint(grip.position);
            Vector3 delta = target - gripInWeaponRoot;
            // 目标点换算回的位移要从 weaponRoot 空间转到"模型父节点"空间：
            // 双枪的手枪挂在 0.92 缩放的 Twinfin 下，直接加在 localPosition 上会被缩放吃掉 8%。
            Vector3 parentDelta = modelRoot.parent != null
                ? modelRoot.parent.InverseTransformVector(weaponRoot.TransformVector(delta))
                : delta;
            modelRoot.localPosition += parentDelta;
        }
    }
}