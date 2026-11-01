using NUnit.Framework;
using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// P16-D 窄缝自动剔除单元测试：
    /// 确保角色无法通过且难以涂抹的窄缝（≤0.9m）被自动标为 blocked 并退出可涂分母；
    /// 确保单侧贴墙区域不被误封；确保 1.2m 等有效走廊完全保留；
    /// 确保 PaintableCount 计数精确同步。
    /// </summary>
    public class InkGridSealSlitsTests
    {
        private InkGrid CreateGrid(int width = 100, int height = 100, float sizeX = 10f, float sizeZ = 10f)
        {
            // CellWidth = sizeX / width = 0.1m, CellHeight = sizeZ / height = 0.1m
            return new InkGrid(width, height, new Vector2(sizeX, sizeZ), Vector2.zero,
                new Color32(50, 50, 50, 0),
                new Color32(255, 100, 0, 255),
                new Color32(0, 100, 255, 255),
                new Color32(20, 20, 20, 255));
        }

        [Test]
        public void SealNarrowSlits_03mSlit_IsSealed()
        {
            var grid = CreateGrid(100, 100, 10f, 10f); // 0.1m / cell
            int initialPaintable = grid.PaintableCount;

            // 左侧障碍物：x ∈ [1.0, 1.5] (cell 10..15), z ∈ [2.0, 3.0] (cell 20..30)
            grid.SetBlockedWorldRect(new Vector2(1.0f, 2.0f), new Vector2(1.5f, 3.0f), true);
            // 右侧障碍物：x ∈ [1.9, 2.4] (cell 19..24), z ∈ [2.0, 3.0] (cell 20..30)
            // 中间净空缝隙：x ∈ (1.5, 1.9) -> cell 16, 17, 18 (3 个格子 = 0.3m 缝隙)
            grid.SetBlockedWorldRect(new Vector2(1.9f, 2.0f), new Vector2(2.4f, 3.0f), true);

            int paintableBeforeSeal = grid.PaintableCount;
            Assert.Less(paintableBeforeSeal, initialPaintable);

            // 封缝前，缝隙中线点为非 blocked
            Vector2 slitMidPoint = new Vector2(1.75f, 2.5f);
            Assert.IsFalse(grid.IsBlockedAt(slitMidPoint), "封缝前 0.3m 窄缝内部应为 open");

            int sealedCount = grid.SealNarrowSlits(0.9f);
            Assert.Greater(sealedCount, 0, "0.3m 缝隙应被成功封闭");

            // 封缝后，缝隙中线点必须已被标为 blocked
            Assert.IsTrue(grid.IsBlockedAt(slitMidPoint), "0.3m 缝隙内部格子应被封闭标为 blocked");
            Assert.AreEqual(paintableBeforeSeal - sealedCount, grid.PaintableCount,
                "PaintableCount 必须与封闭格子数精确同步扣减");
        }

        [Test]
        public void SealNarrowSlits_SingleSidedWall_IsNotSealed()
        {
            var grid = CreateGrid(100, 100, 10f, 10f);

            // 只有单侧墙体：x ∈ [1.0, 1.5], z ∈ [2.0, 3.0]；右侧空旷无阻挡
            grid.SetBlockedWorldRect(new Vector2(1.0f, 2.0f), new Vector2(1.5f, 3.0f), true);
            int paintableBeforeSeal = grid.PaintableCount;

            int sealedCount = grid.SealNarrowSlits(0.9f);
            Assert.AreEqual(0, sealedCount, "单侧贴墙无对向阻挡时，严禁误封");

            // 墙边 0.1m, 0.2m, 0.5m 处均应正常保持可涂
            Assert.IsFalse(grid.IsBlockedAt(new Vector2(1.65f, 2.5f)), "贴墙 0.15m 格不得被误封");
            Assert.IsFalse(grid.IsBlockedAt(new Vector2(1.85f, 2.5f)), "贴墙 0.35m 格不得被误封");
            Assert.AreEqual(paintableBeforeSeal, grid.PaintableCount, "可涂面积分母不得变动");
        }

        [Test]
        public void SealNarrowSlits_12mCorridor_IsPreserved()
        {
            var grid = CreateGrid(100, 100, 10f, 10f); // 0.1m / cell

            // 左侧障碍物：x ∈ [1.0, 1.5] (cell 10..15)
            grid.SetBlockedWorldRect(new Vector2(1.0f, 2.0f), new Vector2(1.5f, 3.0f), true);
            // 右侧障碍物：x ∈ [2.8, 3.3] (cell 28..33)
            // 净空走廊：x ∈ (1.5, 2.8) -> 12 个格子 = 1.2m 走廊 (> 0.9m maxGap)
            grid.SetBlockedWorldRect(new Vector2(2.8f, 2.0f), new Vector2(3.3f, 3.0f), true);

            int paintableBeforeSeal = grid.PaintableCount;

            int sealedCount = grid.SealNarrowSlits(0.9f);
            Assert.AreEqual(0, sealedCount, "1.2m 有效战术走廊净宽 > 0.9m，不得被封闭");

            // 走廊各个位置（左侧、中间、右侧）均保持 open
            Assert.IsFalse(grid.IsBlockedAt(new Vector2(1.7f, 2.5f)), "走廊左侧格应保持可走/可涂");
            Assert.IsFalse(grid.IsBlockedAt(new Vector2(2.15f, 2.5f)), "走廊中心格应保持可走/可涂");
            Assert.IsFalse(grid.IsBlockedAt(new Vector2(2.6f, 2.5f)), "走廊右侧格应保持可走/可涂");
            Assert.AreEqual(paintableBeforeSeal, grid.PaintableCount);
        }

        [Test]
        public void SealNarrowSlits_PaintableCount_AccuratelyMaintained()
        {
            var grid = CreateGrid(50, 50, 5f, 5f);
            int totalCells = 50 * 50;
            Assert.AreEqual(totalCells, grid.PaintableCount);

            // 构造多处 0.3m 窄缝（X 方向与 Z 方向）
            grid.SetBlockedWorldRect(new Vector2(0.5f, 0.5f), new Vector2(1.0f, 2.0f), true);
            grid.SetBlockedWorldRect(new Vector2(1.3f, 0.5f), new Vector2(1.8f, 2.0f), true);

            int sealedCount = grid.SealNarrowSlits(0.9f);
            Assert.Greater(sealedCount, 0);

            // 手动统计所有 blocked 状态格，验证一致性
            int actualBlocked = 0;
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    float wx = (x + 0.5f) * grid.CellWidth;
                    float wz = (y + 0.5f) * grid.CellHeight;
                    if (grid.IsBlockedAt(new Vector2(wx, wz)))
                    {
                        actualBlocked++;
                    }
                }
            }

            Assert.AreEqual(totalCells - actualBlocked, grid.PaintableCount,
                "Grid.PaintableCount 必须与整网格非 blocked 格数量严格相等");
        }
    }
}
