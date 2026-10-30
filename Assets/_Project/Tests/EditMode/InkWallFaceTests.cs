using NUnit.Framework;
using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 墙面墨格（InkWallFace）纯逻辑测试：世界坐标投影、圆形涂色、归属覆盖与越界保护。
    /// </summary>
    public class InkWallFaceTests
    {
        private static readonly Color32 Neutral = new Color32(0, 0, 0, 0);
        private static readonly Color32 TeamA = new Color32(255, 112, 15, 255);
        private static readonly Color32 TeamB = new Color32(38, 107, 255, 255);

        private static InkWallFace CreateFace(float width, float height, float cellSize)
        {
            return new InkWallFace(
                new Vector3(-width * 0.5f, 0f, -2f),
                Vector3.right,
                Vector3.up,
                Vector3.back,
                width,
                height,
                cellSize,
                Neutral,
                TeamA,
                TeamB,
                "TestFace");
        }

        [Test]
        public void NewFace_IsNeutralAndSizedByCellSize()
        {
            InkWallFace face = CreateFace(4f, 2f, 0.2f);

            Assert.AreEqual(20, face.Width);
            Assert.AreEqual(10, face.Height);
            Assert.AreEqual(Team.Neutral, face.InkAt(new Vector2(2f, 1f)));
            Assert.AreEqual(2f, face.TopY, 0.0001f, "默认朝上的立面顶部 = 原点高度 + 面高");
        }

        [Test]
        public void WorldToFace_ProjectsOntoPlaneAxes()
        {
            InkWallFace face = CreateFace(4f, 2f, 0.2f);

            Vector2 lowerLeft = face.WorldToFace(new Vector3(-2f, 0f, -2f));
            Assert.AreEqual(0f, lowerLeft.x, 0.0001f);
            Assert.AreEqual(0f, lowerLeft.y, 0.0001f);

            Vector2 center = face.WorldToFace(new Vector3(0f, 1f, -2f));
            Assert.AreEqual(2f, center.x, 0.0001f);
            Assert.AreEqual(1f, center.y, 0.0001f);
        }

        [Test]
        public void PaintCircle_MarksCellsWithinRadiusOnly()
        {
            InkWallFace face = CreateFace(4f, 2f, 0.2f);

            int changed = face.PaintCircle(new Vector2(2f, 1f), 0.45f, Team.TeamA);

            Assert.Greater(changed, 0, "圆心处应至少涂到一格");
            Assert.AreEqual(Team.TeamA, face.InkAt(new Vector2(2f, 1f)));
            Assert.AreEqual(Team.Neutral, face.InkAt(new Vector2(0.1f, 0.1f)), "远处角落不应被涂到");
            Assert.IsTrue(face.IsDirty);
        }

        [Test]
        public void PaintCircle_OverwritingEnemyInk_TransfersSameCells()
        {
            InkWallFace face = CreateFace(4f, 2f, 0.2f);

            int first = face.PaintCircle(new Vector2(2f, 1f), 0.6f, Team.TeamA);
            int overwrite = face.PaintCircle(new Vector2(2f, 1f), 0.6f, Team.TeamB);

            Assert.Greater(first, 0);
            Assert.AreEqual(first, overwrite, "同位置同半径互刷应覆盖同样的格子");
            Assert.AreEqual(Team.TeamB, face.InkAt(new Vector2(2f, 1f)));
        }

        [Test]
        public void PaintCircle_OutsideFace_DoesNothing()
        {
            InkWallFace face = CreateFace(4f, 2f, 0.2f);

            int changed = face.PaintCircle(new Vector2(-3f, 1f), 0.5f, Team.TeamA);

            Assert.AreEqual(0, changed, "完全落在墙面外的圆斑不应改变任何格子");
        }

        [Test]
        public void TryWorldToCell_RejectsPointsOutsidePlane()
        {
            InkWallFace face = CreateFace(4f, 2f, 0.2f);

            Assert.IsTrue(face.TryWorldToCell(new Vector3(0f, 1f, -2f), out int x, out int y));
            Assert.AreEqual(10, x);
            Assert.AreEqual(5, y);

            Assert.IsFalse(face.TryWorldToCell(new Vector3(10f, 1f, -2f), out _, out _));
        }

        [Test]
        public void ResetPaint_ClearsAllCells()
        {
            InkWallFace face = CreateFace(4f, 2f, 0.2f);
            face.PaintCircle(new Vector2(2f, 1f), 1.0f, Team.TeamB);
            Assert.AreEqual(Team.TeamB, face.InkAt(new Vector2(2f, 1f)));

            face.ResetPaint();

            Assert.AreEqual(Team.Neutral, face.InkAt(new Vector2(2f, 1f)));
        }

        [Test]
        public void PixelBuffer_MatchesGridSizeAndAlphaChannel()
        {
            InkWallFace face = CreateFace(4f, 2f, 0.2f);
            face.PaintCircle(new Vector2(2f, 1f), 0.5f, Team.TeamA);

            Color32[] pixels = face.PixelBuffer;
            Assert.AreEqual(face.Width * face.Height, pixels.Length);
            Assert.AreEqual(0, pixels[0].a, "未涂区域 alpha 必须为 0（叠加层靠它判断透明）");

            int centerIndex = (face.Height / 2) * face.Width + (face.Width / 2);
            Assert.AreEqual(255, pixels[centerIndex].a, "已涂区域 alpha 应为 255");
        }

        [Test]
        public void PaintCircle_RepeatedOverlappingSplats_NeverDemoteExistingInk()
        {
            InkWallFace face = CreateFace(4f, 2f, 0.1f);

            // 第一发：圆心格应为实心墨水（alpha ≥ 0.62 底线）
            face.PaintCircle(new Vector2(2f, 1f), 0.8f, Team.TeamA);
            int centerIndex = (face.Height / 2) * face.Width + (face.Width / 2);
            byte alphaAfterFirst = face.PixelBuffer[centerIndex].a;
            Assert.GreaterOrEqual(alphaAfterFirst, 158, "己方实心格 alpha 不应低于 0.62 底线");

            // 随后 8 发中心错开、互相重叠的墨斑（模拟连续扫射）：
            // 后续墨斑的圆外过渡环（alpha ≤ 0.5，渲染时被整格剔除）绝不允许压低已有墨水，
            // 否则连续喷扫视觉上等于一边喷一边擦。
            for (int i = 1; i <= 8; i++)
            {
                face.PaintCircle(new Vector2(2f + i * 0.12f, 1f + (i % 2) * 0.1f), 0.8f, Team.TeamA);
                Assert.GreaterOrEqual(
                    face.PixelBuffer[centerIndex].a,
                    alphaAfterFirst,
                    $"第 {i + 1} 发墨斑的过渡环不得压低已有墨水 alpha（喷扫 = 擦除回归）");
            }
        }

        [Test]
        public void PaintCircle_RepeatedSpraysOnSameSpot_SolidifyCoverage()
        {
            InkWallFace face = CreateFace(4f, 2f, 0.1f);
            face.PaintCircle(new Vector2(2f, 1f), 0.9f, Team.TeamA);

            int centerIndex = (face.Height / 2) * face.Width + (face.Width / 2);
            face.PixelBuffer[centerIndex].a = 100; // 模拟早期半覆盖格
            byte before = face.PixelBuffer[centerIndex].a;

            // 同位置补喷：覆盖率只升不降
            face.PaintCircle(new Vector2(2f, 1f), 0.9f, Team.TeamA);
            Assert.GreaterOrEqual(face.PixelBuffer[centerIndex].a, before, "补喷不得降低已有格覆盖率");
            Assert.AreEqual(Team.TeamA, face.InkAt(new Vector2(2f, 1f)), "补喷不改归属");
        }
    }
}