using NUnit.Framework;
using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// 朝向盒墨面（旋转道具 / 斜坡）的结构与涂色验收：
    /// 4 张立面必须跟随盒子的旋转（而不是世界 AABB），命中面独立着墨、背面不漏墨。
    /// </summary>
    public class InkWallOrientedTests
    {
        [Test]
        public void OrientedBox_FacesFollowRotation_AndPaintOnlyHitFace()
        {
            var go = new GameObject("RotatedInkBox");
            var cfg = ScriptableObject.CreateInstance<InkGridConfigSO>();
            try
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = Vector3.one;

                var wall = go.AddComponent<InkWallSurface>();
                Quaternion rotation = Quaternion.Euler(0f, 30f, 0f);
                wall.ConfigureOrientedBox(
                    box,
                    Vector3.zero,
                    new Vector3(1f, 2f, 3f),
                    rotation,
                    null,
                    cfg,
                    allowClimb: false);

                Assert.IsTrue(wall.InitializeIfNeeded(true), "运行时/单测下应能生成朝向盒立面");
                Assert.AreEqual(4, wall.FaceCount, "盒子应生成 4 张侧面墨面");
                Assert.IsFalse(wall.CanClimb, "显式传入的攀爬开关应生效");

                // 旋转盒的世界 AABB：宽 1m × 深 3m 绕 Y 旋转 30°
                // X = 1*cos30 + 3*sin30，Z = 1*sin30 + 3*cos30，Y 保持 2m。
                float cos = Mathf.Cos(30f * Mathf.Deg2Rad);
                float sin = Mathf.Sin(30f * Mathf.Deg2Rad);
                float expectedX = 1f * cos + 3f * sin;
                float expectedZ = 1f * sin + 3f * cos;
                Assert.AreEqual(2f, wall.WorldBounds.size.y, 0.001f, "旋转盒 AABB 的高度应为盒高");
                Assert.AreEqual(expectedX, wall.WorldBounds.size.x, 0.01f,
                    "世界包围盒必须由朝向盒 8 个角点算出，而不是拿 Collider 的世界 AABB 硬套");
                Assert.AreEqual(expectedZ, wall.WorldBounds.size.z, 0.01f);

                // 在 +Z 面中心涂墨：该面法线应跟随 30° 旋转
                Vector3 forward = rotation * Vector3.forward;
                Vector3 point = rotation * new Vector3(0f, 0f, 1.5f) + Vector3.up * 1f;
                Assert.AreEqual(Team.Neutral, wall.InkAtWorld(point, forward));

                int changed = wall.PaintAtWorld(point, forward, 0.4f, Team.TeamA);
                Assert.Greater(changed, 0, "朝向盒命中面应能落墨");
                Assert.AreEqual(Team.TeamA, wall.InkAtWorld(point, forward), "命中的 +Z 面应被涂上");

                // 背面（-Z）不应同时出现墨水
                Vector3 backPoint = rotation * new Vector3(0f, 0f, -1.5f) + Vector3.up * 1f;
                Assert.AreEqual(Team.Neutral, wall.InkAtWorld(backPoint, -forward), "背面不得同步上墨");

                // 顶面（世界 +Y）不属于 4 张侧面：由顶视墨迹图集负责
                Assert.AreEqual(0, wall.PaintAtWorld(Vector3.up * 1f, Vector3.up, 0.4f, Team.TeamB),
                    "朝上的命中应交给顶面图集，立面系统不得接收");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(cfg);
            }
        }
    }
}
