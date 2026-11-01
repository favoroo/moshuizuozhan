using P1.Weapons;
using UnityEngine;

namespace P1.Player
{
    /// <summary>
    /// 单把武器的手臂持握姿态（全部为骨骼局部旋转，装配期解算一次、运行期直接套用）：
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
    /// 每把武器的持握数据：右手（扳机握把）目标手位 + 左手是否参与托握 / 双持。
    /// 位置为 KidRoot 空间（脚底 y=0），与武器挂点表同一坐标系，方便直接落到
    /// <see cref="CharacterMetrics.WeaponMountOffset"/> 的替代锚点上。
    /// </summary>
    public static class WeaponHoldTargets
    {
        /// <summary>右手（握把）手位 —— 枪身贴胸、不漂在体外，手臂仍能自然屈肘。</summary>
        public static Vector3 RightHand(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Charger: return new Vector3(0.122f, 0.580f, 0.245f);
                case WeaponKind.Roller: return new Vector3(0.045f, 0.446f, 0.100f);
                case WeaponKind.Slosher: return new Vector3(0.055f, 0.560f, 0.195f);
                case WeaponKind.Splatling: return new Vector3(0.120f, 0.570f, 0.210f);
                case WeaponKind.Blaster: return new Vector3(0.125f, 0.570f, 0.215f);
                // 双枪左右各持一把：右手位与单持一致，左手位镜像
                case WeaponKind.Dualies: return new Vector3(0.085f, 0.550f, 0.195f);
                default: return new Vector3(0.125f, 0.570f, 0.215f);
            }
        }

        /// <summary>左手托握/持枪目标（仅双枪使用；其余武器左手自然垂下）。</summary>
        public static Vector3 LeftHand(WeaponKind kind)
            => kind == WeaponKind.Dualies
                ? new Vector3(-0.085f, 0.550f, 0.195f)
                : Vector3.zero;

        /// <summary>肘部朝向：外侧偏下偏后，避免上臂贴胸、肘尖戳进衣服。</summary>
        public static Vector3 RightPole => new Vector3(0.80f, -0.85f, -0.40f);
        public static Vector3 LeftPole => new Vector3(-0.80f, -0.85f, -0.40f);
    }
}
