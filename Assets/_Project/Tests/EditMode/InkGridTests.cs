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
