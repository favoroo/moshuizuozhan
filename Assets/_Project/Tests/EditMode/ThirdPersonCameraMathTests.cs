using NUnit.Framework;
using P1.Player;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P9 第三人称相机纯数学验证：
    /// 俯仰夹紧、相机相对移动、瞄准射线起点必须在相机中心线上（准星即真理）、形态枢轴高度切换。
    /// </summary>
    public class ThirdPersonCameraMathTests
    {
        [Test]
        public void ClampPitch_RespectsBounds_HandlesNaN_AndSwappedLimits()
        {
            Assert.AreEqual(66f, ThirdPersonCameraMath.ClampPitch(80f, -60f, 66f), 1e-4f);
            Assert.AreEqual(-60f, ThirdPersonCameraMath.ClampPitch(-80f, -60f, 66f), 1e-4f);
            Assert.AreEqual(12f, ThirdPersonCameraMath.ClampPitch(12f, -60f, 66f), 1e-4f);

            // NaN 回落到合法区间内（0 被夹在 -60~66 之间 → 0）
            Assert.AreEqual(0f, ThirdPersonCameraMath.ClampPitch(float.NaN, -60f, 66f), 1e-4f);

            // min/max 颠倒时自动交换
            Assert.AreEqual(66f, ThirdPersonCameraMath.ClampPitch(80f, 66f, -60f), 1e-4f);
        }

        [Test]
        public void ComputeMoveWorld_IsCameraRelative_AndPreservesMagnitude()
        {
            Vector3 forward = Quaternion.Euler(0f, 90f, 0f) * Vector3.forward; // 世界 +X
            Vector3 right = Quaternion.Euler(0f, 90f, 0f) * Vector3.right;     // 世界 -Z

            Vector3 forwardMove = ThirdPersonCameraMath.ComputeMoveWorld(new Vector2(0f, 1f), forward, right);
            Assert.AreEqual(1f, forwardMove.x, 1e-4f);
            Assert.AreEqual(0f, forwardMove.z, 1e-4f);

            Vector3 rightMove = ThirdPersonCameraMath.ComputeMoveWorld(new Vector2(1f, 0f), forward, right);
            Assert.AreEqual(0f, rightMove.x, 1e-4f);
            Assert.AreEqual(-1f, rightMove.z, 1e-4f);

            // 半推摇杆保留模长，斜向不超速
            Vector3 halfMove = ThirdPersonCameraMath.ComputeMoveWorld(new Vector2(0f, 0.5f), forward, right);
            Assert.AreEqual(0.5f, halfMove.magnitude, 1e-4f);

            Vector3 diagonal = ThirdPersonCameraMath.ComputeMoveWorld(new Vector2(1f, 1f), forward, right);
            Assert.LessOrEqual(diagonal.magnitude, 1.0001f);
        }

        [Test]
        public void ComputeAimRayOrigin_StaysOnCameraCenterLine_AtPlayerDepth()
        {
            Vector3 cameraPos = new Vector3(0f, 2f, -4f);
            Vector3 cameraForward = Vector3.forward;
            Vector3 chest = new Vector3(0f, 1.3f, 0f);

            Vector3 origin = ThirdPersonCameraMath.ComputeAimRayOrigin(cameraPos, cameraForward, chest);

            // 起点在相机中心线上：x=0，y=2（与相机同高），z=0（玩家深度）
            Assert.AreEqual(0f, origin.x, 1e-4f);
            Assert.AreEqual(2f, origin.y, 1e-4f);
            Assert.AreEqual(0f, origin.z, 1e-4f);

            // 起点与相机之间的偏移严格平行于 forward（横向分量为 0）
            Vector3 offset = origin - cameraPos;
            Vector3 lateral = offset - cameraForward * Vector3.Dot(offset, cameraForward);
            Assert.AreEqual(0f, lateral.magnitude, 1e-4f);
        }

        [Test]
        public void ComputePivotHeight_SwitchesForSwimming()
        {
            Assert.AreEqual(1.85f, ThirdPersonCameraMath.ComputePivotHeight(false, 1.85f, 1.30f), 1e-4f);
            Assert.AreEqual(1.30f, ThirdPersonCameraMath.ComputePivotHeight(true, 1.85f, 1.30f), 1e-4f);
        }
    }
}
