using NUnit.Framework;
using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.Tests
{
    public class InkGridTests
    {
        [Test]
        public void NewGrid_StartsNeutralWithZeroCoverage()
        {
            var grid = new InkGrid(100, 100, new Vector2(40f, 40f));

            Assert.AreEqual(10000, grid.PaintableCount);
            Assert.AreEqual(0, grid.TeamACount);
            Assert.AreEqual(0, grid.TeamBCount);
            Assert.AreEqual(0f, grid.GetCoverage01(Team.TeamA), 0.0001f);
            Assert.AreEqual(0f, grid.GetCoverage01(Team.TeamB), 0.0001f);
            Assert.AreEqual(Team.Neutral, grid.InkAt(Vector2.zero));
        }

        [Test]
        public void PaintCircle_MarksCellsAndUpdatesCoverage()
        {
            var grid = new InkGrid(200, 200, new Vector2(40f, 40f));

            int painted = grid.PaintCircle(Vector2.zero, 2.0f, Team.TeamA);

            Assert.Greater(painted, 250);
            Assert.AreEqual(painted, grid.TeamACount);
            Assert.AreEqual(Team.TeamA, grid.InkAt(Vector2.zero));
            Assert.Greater(grid.GetCoveragePercent(Team.TeamA), 0.5f);
        }

        [Test]
        public void PaintCircle_OverwritingEnemyInk_TransfersCountsAccurately()
        {
            var grid = new InkGrid(100, 100, new Vector2(20f, 20f));

            int firstPaint = grid.PaintCircle(Vector2.zero, 2.0f, Team.TeamA);
            Assert.AreEqual(firstPaint, grid.TeamACount);
            Assert.AreEqual(0, grid.TeamBCount);

            int overwrite = grid.PaintCircle(Vector2.zero, 2.0f, Team.TeamB);
            Assert.AreEqual(firstPaint, overwrite);
            Assert.AreEqual(0, grid.TeamACount);
            Assert.AreEqual(overwrite, grid.TeamBCount);
            Assert.AreEqual(Team.TeamB, grid.InkAt(Vector2.zero));
        }

        [Test]
        public void BlockedRect_CannotBePaintedAndReducesPaintableDenominator()
        {
            var grid = new InkGrid(100, 100, new Vector2(20f, 20f));
            int initialPaintable = grid.PaintableCount;

            grid.SetBlockedWorldRect(new Vector2(-2f, -2f), new Vector2(2f, 2f), true);
            Assert.Less(grid.PaintableCount, initialPaintable);
            Assert.IsTrue(grid.IsBlockedAt(Vector2.zero));

            int paintedOnBlocked = grid.PaintCircle(Vector2.zero, 1.0f, Team.TeamA);
            Assert.AreEqual(0, paintedOnBlocked);
            Assert.AreEqual(Team.Neutral, grid.InkAt(Vector2.zero));
        }

        /// <summary>
        /// 软占位（掩体墙/木箱/集装箱/花坛/售卖亭）：格子仍算占位，但顶面可以从上方打到——
        /// 从 y ≥ 顶面高度的命中能涂上并计分，地面溅射（y=0）涂不到，避免隔箱蹭分。
        /// </summary>
        [Test]
        public void SoftBlockedRect_OnlyPaintableFromAboveItsTopFace()
        {
            var grid = new InkGrid(100, 100, new Vector2(20f, 20f));
            int initialPaintable = grid.PaintableCount;

            grid.SetBlockedWorldRect(new Vector2(-2f, -2f), new Vector2(2f, 2f), true, 1.2f);

            Assert.AreEqual(initialPaintable, grid.PaintableCount, "软占位的顶面可涂，不能从可涂面积里扣除");
            Assert.IsTrue(grid.IsBlockedAt(Vector2.zero), "占位判定照旧（小地图/接触遮蔽/窄缝剔除依赖它）");
            Assert.IsTrue(grid.IsSoftBlockedAt(Vector2.zero));

            // 在箱子旁边泼地：地面 y=0，涂不到箱顶
            grid.PaintCircle(Vector2.zero, 2.0f, Team.TeamA);
            Assert.AreEqual(Team.Neutral, grid.InkAt(new Vector2(0.5f, 0.5f)), "地面溅射不能隔着箱子白拿覆盖面积");

            // 打在箱顶（y = 顶面高度）：能涂上，并计入覆盖率
            int paintedOnTop = grid.PaintCircle(new Vector3(0f, 1.2f, 0f), 3.0f, Team.TeamA);
            Assert.Greater(paintedOnTop, 0);
            Assert.AreEqual(Team.TeamA, grid.InkAt(new Vector2(0.5f, 0.5f)), "从上方命中箱顶必须显墨并计分");
            Assert.Greater(grid.GetCoverage01(Team.TeamA), 0f);

            // 已认领的箱顶：地面墨会视觉上盖到箱顶，网格必须跟着翻色（否则「显示敌墨却算我的」）
            grid.PaintCircle(Vector2.zero, 2.0f, Team.TeamB);
            Assert.AreEqual(Team.TeamB, grid.InkAt(new Vector2(0.5f, 0.5f)), "已涂过的箱顶会随地面溅射翻色，视觉与比分保持一致");
        }

        [Test]
        public void HardBlockedRect_StaysUnpaintableFromAnyHeight()
        {
            var grid = new InkGrid(100, 100, new Vector2(20f, 20f));
            int initialPaintable = grid.PaintableCount;

            grid.SetBlockedWorldRect(new Vector2(-2f, -2f), new Vector2(2f, 2f), true);

            Assert.Less(grid.PaintableCount, initialPaintable);
            Assert.IsFalse(grid.IsSoftBlockedAt(Vector2.zero), "地形禁区/死缝是硬不可涂");

            grid.PaintCircle(new Vector3(0f, 1.2f, 0f), 3.0f, Team.TeamA);
            Assert.AreEqual(Team.Neutral, grid.InkAt(Vector2.zero), "硬不可涂格任何高度都涂不到");
        }

        /// <summary>
        /// 计分掩码 = 「仍占位、且还没被涂过」：箱顶没涂过时裁掉图集溅射墨迹（箱子旁边泼一地不会蹭上箱顶），
        /// 从上方命中箱顶后放开（看得到 = 会计分）；硬占位永远裁掉。
        /// </summary>
        [Test]
        public void CopyUnpaintedBlockedMaskTo_RevealsPaintedTopCells()
        {
            var grid = new InkGrid(10, 10, new Vector2(10f, 10f));
            grid.SetBlockedWorldRect(new Vector2(-2f, -2f), new Vector2(2f, 2f), true, 1.2f);

            var mask = new byte[grid.Width * grid.Height];
            int center = 5 * grid.Width + 5;
            grid.CopyUnpaintedBlockedMaskTo(mask);
            Assert.AreEqual(255, mask[center], "箱顶没被涂过时必须裁掉图集溅射墨迹");
            Assert.IsFalse(grid.ScoreMaskDirty, "取完掩码应清掉脏标记");

            grid.PaintCircle(new Vector3(0f, 1.2f, 0f), 0.8f, Team.TeamA);

            Assert.IsTrue(grid.ScoreMaskDirty, "箱顶被涂到后掩码需要重传");
            grid.CopyUnpaintedBlockedMaskTo(mask);
            Assert.AreEqual(0, mask[center], "箱顶涂到后掩码必须放开（看得到 = 会计分）");
        }

        [Test]
        public void CopyBlockedMaskTo_WritesRowMajorBinaryMask()
        {
            var grid = new InkGrid(10, 10, new Vector2(10f, 10f));
            grid.SetBlockedWorldRect(new Vector2(-1f, -1f), new Vector2(1f, 1f), true);

            var mask = new byte[grid.Width * grid.Height];
            grid.CopyBlockedMaskTo(mask);

            // 世界 (0,0) 落在中心格 (5,5)：行主序 idx = y * Width + x
            Assert.AreEqual(255, mask[5 * grid.Width + 5]);
            Assert.AreEqual(0, mask[0], "场地角落不应被标记为不可涂");
            Assert.AreEqual(0, mask[grid.Width * grid.Height - 1]);
        }

        [Test]
        public void CopyBlockedMaskTo_RejectsTooSmallDestination()
        {
            var grid = new InkGrid(10, 10, new Vector2(10f, 10f));
            Assert.Throws<System.ArgumentNullException>(() => grid.CopyBlockedMaskTo(null));
            Assert.Throws<System.ArgumentException>(() => grid.CopyBlockedMaskTo(new byte[10]));
        }

        [Test]
        public void ResetPaintOnly_ClearsInkButKeepsBlockedCells()
        {
            var grid = new InkGrid(100, 100, new Vector2(20f, 20f));
            grid.SetBlockedWorldRect(new Vector2(-1f, -1f), new Vector2(1f, 1f), true);
            int paintableWithObstacle = grid.PaintableCount;

            grid.PaintCircle(new Vector2(5f, 5f), 2.0f, Team.TeamA);
            Assert.Greater(grid.TeamACount, 0);

            grid.ResetPaintOnly();

            Assert.AreEqual(0, grid.TeamACount);
            Assert.AreEqual(0, grid.TeamBCount);
            Assert.AreEqual(paintableWithObstacle, grid.PaintableCount);
            Assert.IsTrue(grid.IsBlockedAt(Vector2.zero));
        }
    }
}
