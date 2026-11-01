using NUnit.Framework;
using P1.Ink;

namespace P1.Tests.EditMode
{
    /// <summary>
    /// 墨迹图集分批消费规则测试（纯逻辑，不实例化 <see cref="InkSplatAtlas"/>，因此不碰 RT / Material）。
    ///
    /// 背景（v0.1.9 已确证 bug）：单帧落墨超过 <see cref="InkSplatAtlas.MaxQuads"/> 时，
    /// 旧版 <c>Advance</c> 把待绘制队列一次性搬进诊断队列再 <c>Clear()</c>，
    /// 而 <c>BuildMesh</c> 只画得下 MaxQuads 个 quad —— 超出的墨斑既不绘制也不再排队，永久不显示；
    /// 玩法网格与覆盖率却已经算上，玩家读作「涂了不显示」。
    ///
    /// 修法：单帧只消费最多 MaxQuads 枚，剩余留到后续帧继续画。本文件覆盖该消费 / 容量策略。
    /// </summary>
    public class InkSplatAtlasBatchTests
    {
        private const int Budget = InkSplatAtlas.MaxQuads; // 384

        [Test]
        public void MaxQuads_AndCapacity_HaveExpectedRelationship()
        {
            Assert.AreEqual(384, InkSplatAtlas.MaxQuads, "MaxQuads 是网格/索引缓冲容量契约，改动需同步 _indices/_verts");
            Assert.AreEqual(InkSplatAtlas.MaxQuads * 2, InkSplatAtlas.PendingCapacity,
                "待绘制队列容量应为两帧预算，保证正常节奏下不触发丢墨");
        }

        // ---------- 分批消费：1000 枚 → 384 / 384 / 232 ----------

        [Test]
        public void PlanDrawBatch_1000Pending_DrainsInThreeFrames_384_384_232()
        {
            int pending = 1000;

            InkSplatAtlas.PlanDrawBatch(pending, Budget, out int draw1, out int remain1);
            Assert.AreEqual(384, draw1);
            Assert.AreEqual(616, remain1);

            InkSplatAtlas.PlanDrawBatch(remain1, Budget, out int draw2, out int remain2);
            Assert.AreEqual(384, draw2);
            Assert.AreEqual(232, remain2);

            InkSplatAtlas.PlanDrawBatch(remain2, Budget, out int draw3, out int remain3);
            Assert.AreEqual(232, draw3);
            Assert.AreEqual(0, remain3, "第三帧应把剩余全部画完，不得丢墨");
        }

        [Test]
        public void PlanDrawBatch_NeverDropsSplats_AccumulatedDrawEqualsPending()
        {
            const int pendingTotal = 1000;
            int pending = pendingTotal;
            int drawn = 0;
            int frames = 0;

            while (pending > 0 && frames < 100)
            {
                InkSplatAtlas.PlanDrawBatch(pending, Budget, out int draw, out int remain);
                Assert.LessOrEqual(draw, Budget, "单帧不得超过绘制预算");
                Assert.Greater(draw, 0, "队列非空时必须推进，否则会无限积压");
                drawn += draw;
                pending = remain;
                frames++;
            }

            Assert.AreEqual(3, frames);
            Assert.AreEqual(pendingTotal, drawn, "累计绘制数必须等于落墨总数（不丢墨）");
            Assert.AreEqual(0, pending);
        }

        [Test]
        public void PlanDrawBatch_PendingNotMultipleOfBudget_ConsumesTail()
        {
            InkSplatAtlas.PlanDrawBatch(Budget, Budget, out int draw, out int remain);
            Assert.AreEqual(Budget, draw);
            Assert.AreEqual(0, remain);

            InkSplatAtlas.PlanDrawBatch(Budget + 1, Budget, out draw, out remain);
            Assert.AreEqual(Budget, draw);
            Assert.AreEqual(1, remain);
        }

        [Test]
        public void PlanDrawBatch_BelowBudget_ConsumesAllInOneFrame()
        {
            InkSplatAtlas.PlanDrawBatch(5, Budget, out int draw, out int remain);
            Assert.AreEqual(5, draw);
            Assert.AreEqual(0, remain);
        }

        [Test]
        public void PlanDrawBatch_EmptyOrInvalidInput_IsSafe()
        {
            InkSplatAtlas.PlanDrawBatch(0, Budget, out int draw, out int remain);
            Assert.AreEqual(0, draw);
            Assert.AreEqual(0, remain);

            InkSplatAtlas.PlanDrawBatch(-7, Budget, out draw, out remain);
            Assert.AreEqual(0, draw, "负数待绘制数不得画东西");
            Assert.AreEqual(0, remain);

            InkSplatAtlas.PlanDrawBatch(10, 0, out draw, out remain);
            Assert.AreEqual(0, draw, "预算为 0 时不绘制");
            Assert.AreEqual(10, remain, "但也不得丢掉待绘制墨斑");

            InkSplatAtlas.PlanDrawBatch(10, -3, out draw, out remain);
            Assert.AreEqual(0, draw);
            Assert.AreEqual(10, remain);
        }

        // ---------- 排空帧数 ----------

        [Test]
        public void FramesToDrain_MatchesCeilingDivision()
        {
            Assert.AreEqual(0, InkSplatAtlas.FramesToDrain(0, Budget));
            Assert.AreEqual(0, InkSplatAtlas.FramesToDrain(-5, Budget));
            Assert.AreEqual(1, InkSplatAtlas.FramesToDrain(1, Budget));
            Assert.AreEqual(1, InkSplatAtlas.FramesToDrain(384, Budget));
            Assert.AreEqual(2, InkSplatAtlas.FramesToDrain(385, Budget));
            Assert.AreEqual(3, InkSplatAtlas.FramesToDrain(1000, Budget));
            Assert.AreEqual(int.MaxValue, InkSplatAtlas.FramesToDrain(10, 0),
                "预算非法时不得声称能排空，避免调用方把积压当成已完成");
        }

        // ---------- 队列上限 / 丢最旧策略 ----------

        [Test]
        public void PendingOverflowDropCount_OnlyDropsWhenOverCapacity()
        {
            int cap = InkSplatAtlas.PendingCapacity; // 768

            Assert.AreEqual(0, InkSplatAtlas.PendingOverflowDropCount(cap - 1, cap));
            Assert.AreEqual(0, InkSplatAtlas.PendingOverflowDropCount(cap, cap), "刚好装满不丢墨");
            Assert.AreEqual(1, InkSplatAtlas.PendingOverflowDropCount(cap + 1, cap));
            Assert.AreEqual(232, InkSplatAtlas.PendingOverflowDropCount(1000, cap), "1000 枚超出容量 768，应丢 232 枚");
            Assert.AreEqual(0, InkSplatAtlas.PendingOverflowDropCount(0, cap));
        }

        [Test]
        public void OneFrameBurstAtExactlyOneFrameOfInk_NeverDrops()
        {
            // 真实战斗节奏：一帧最多落墨 ≈ 一帧预算（多枚弹丸 + 拖尾）。
            // 由于每帧 LateUpdate 都会消费 MaxQuads 枚，队列不会逼近容量，因此不会丢墨。
            Assert.AreEqual(0, InkSplatAtlas.PendingOverflowDropCount(Budget, InkSplatAtlas.PendingCapacity));
            Assert.AreEqual(0, InkSplatAtlas.PendingOverflowDropCount(Budget * 2, InkSplatAtlas.PendingCapacity));
        }

        [Test]
        public void OverflowSimulation_BacklogStaysBoundedAndDrains()
        {
            // 模拟「单帧瞬间落墨 1000 枚（超出容量 768）」的最坏情形：
            // 队列封顶到容量、丢弃 232 枚最旧，其余在 2 帧内画完，不产生无限积压。
            int cap = InkSplatAtlas.PendingCapacity;
            int queue = 0;
            int dropped = 0;
            int frames = 0;

            int[] addsPerFrame = { 1000, 0, 0, 0 };

            for (int i = 0; i < addsPerFrame.Length; i++)
            {
                for (int n = 0; n < addsPerFrame[i]; n++)
                {
                    int drop = InkSplatAtlas.PendingOverflowDropCount(queue + 1, cap);
                    if (drop > 0)
                    {
                        dropped += drop;
                        queue -= drop;
                    }
                    queue++;
                }

                Assert.LessOrEqual(queue, cap, "队列不得超出容量（否则内存无上限增长）");

                InkSplatAtlas.PlanDrawBatch(queue, Budget, out int draw, out int remain);
                queue = remain;
                frames++;
            }

            Assert.AreEqual(232, dropped, "1000 枚超容量 768 时应丢 232 枚最旧");
            Assert.AreEqual(0, queue, "后续帧必须把积压排空");
            Assert.AreEqual(4, frames);
        }
    }
}
