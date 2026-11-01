using System.Collections.Generic;
using NUnit.Framework;
using P1.AI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// Bot 寻路 / 选点失败恢复的纯逻辑验收：
    /// 1. A* 失败时不得残留「直线 goal」（旧 FollowPath 兜底会让 Bot 顶着墙冲锋）；
    /// 2. 连续失败退避曲线单调、有上限；
    /// 3. 无目标时必须尊重重选冷却（旧实现每帧重跑 20 个候选点采样的根因）。
    /// 全部不依赖场景对象，EditMode 秒级复现。
    /// </summary>
    public class BotAiRecoveryTests
    {
        // ---------- 退避曲线 ----------

        [Test]
        public void RetryDelay_EscalatesThenCaps_Monotonic()
        {
            Assert.AreEqual(0f, BotRecoveryPolicy.RetryDelay(0), 1e-4f, "没有失败时不应启用退避");
            Assert.AreEqual(0f, BotRecoveryPolicy.RetryDelay(-3), 1e-4f, "负值按无失败处理");

            float previous = 0f;
            for (int failures = 1; failures <= 12; failures++)
            {
                float delay = BotRecoveryPolicy.RetryDelay(failures);
                Assert.GreaterOrEqual(delay, previous, $"失败 {failures} 次的退避不应变短");
                Assert.LessOrEqual(delay, BotRecoveryPolicy.MaxRetryDelay + 1e-3f,
                    $"失败 {failures} 次的退避不应超过上限");
                previous = delay;
            }

            Assert.AreEqual(BotRecoveryPolicy.MinRetryDelay, BotRecoveryPolicy.RetryDelay(1), 1e-4f);
            Assert.Greater(BotRecoveryPolicy.RetryDelay(2), BotRecoveryPolicy.RetryDelay(1),
                "第二次连续失败应拉长间隔");
            Assert.AreEqual(BotRecoveryPolicy.MaxRetryDelay, BotRecoveryPolicy.RetryDelay(5), 1e-4f,
                "连续失败若干次后应封顶，不能无限增长");
        }

        // ---------- 目标重选冷却 ----------

        [Test]
        public void NeedsReselect_WithTarget_ReselectsOnArrivalOrExpiry()
        {
            Assert.IsTrue(BotRecoveryPolicy.NeedsReselect(true, true, 5f, 0f), "到达目标应立即重选");
            Assert.IsTrue(BotRecoveryPolicy.NeedsReselect(true, false, 0f, 0f), "目标过期应重选");
            Assert.IsFalse(BotRecoveryPolicy.NeedsReselect(true, false, 5f, 0f), "目标有效时不应重选");
        }

        [Test]
        public void NeedsReselect_WithoutTarget_OnlyHonoursCooldown()
        {
            // 回归点：旧条件 `!_hasMoveTarget || …` 让选点失败后每帧重采样，
            // 现在没有目标时只认冷却计时。
            Assert.IsFalse(BotRecoveryPolicy.NeedsReselect(false, false, 0f, 1.5f),
                "选点失败后的冷却期内不得每帧重采样");
            Assert.IsTrue(BotRecoveryPolicy.NeedsReselect(false, false, 0f, 0f),
                "冷却结束后才允许重选");
            Assert.IsFalse(BotRecoveryPolicy.NeedsReselect(false, true, 0f, 0.5f),
                "没有目标时冷却优先于『到达』判定");
        }

        // ---------- 放弃目标阈值 ----------

        [Test]
        public void ShouldAbandonTarget_AfterTwoConsecutiveFailures()
        {
            Assert.IsFalse(BotRecoveryPolicy.ShouldAbandonTarget(0));
            Assert.IsFalse(BotRecoveryPolicy.ShouldAbandonTarget(1), "单次失败只退避，不立刻换目标");
            Assert.IsTrue(BotRecoveryPolicy.ShouldAbandonTarget(2));
            Assert.IsTrue(BotRecoveryPolicy.ShouldAbandonTarget(5));
        }

        // ---------- A* 失败语义 ----------

        [Test]
        public void AStar_UnreachableGoal_LeavesNoPath_SoCallerCannotChargeStraight()
        {
            // 5x5 格、中间整列是墙：左右两半互不连通
            const int width = 5, height = 5;
            var walkable = new bool[width * height];
            for (int i = 0; i < walkable.Length; i++) walkable[i] = true;
            for (int y = 0; y < height; y++) walkable[y * width + 2] = false;

            NavGrid grid = NavGrid.CreateFromMask(width, height, 1f, Vector2.zero, walkable);

            // 预先塞入「直线 goal」，模拟旧 FollowPath 的兜底：失败后必须被清空，不能残留
            var path = new List<Vector3> { grid.CellToWorld(4, 2) };

            Assert.IsFalse(grid.TryFindPath(grid.CellToWorld(0, 2), grid.CellToWorld(4, 2), path),
                "被墙隔开的目标不应被判为可达");
            Assert.AreEqual(0, path.Count, "寻路失败后不得残留直线 goal（否则 Bot 会顶墙）");
        }

        [Test]
        public void AStar_ReachableGoal_ProducesPathThroughGap()
        {
            // 5x7 格：中间列只在 y=3 留一个缺口
            const int width = 5, height = 7;
            var walkable = new bool[width * height];
            for (int i = 0; i < walkable.Length; i++) walkable[i] = true;
            for (int y = 0; y < height; y++)
            {
                if (y != 3) walkable[y * width + 2] = false;
            }

            NavGrid grid = NavGrid.CreateFromMask(width, height, 1f, Vector2.zero, walkable);
            var path = new List<Vector3>();

            Assert.IsTrue(grid.TryFindPath(grid.CellToWorld(0, 0), grid.CellToWorld(4, 6), path),
                "有缺口时 A* 应能找到绕行路径");

            bool passesGap = false;
            for (int i = 0; i < path.Count; i++)
            {
                if (!grid.TryWorldToCell(path[i], out int cx, out int cy)) continue;
                if (cx == 2 && cy == 3) passesGap = true;
            }

            Assert.IsTrue(passesGap, "路径必须从缺口穿过（涂地目标的可达性校验依赖这一语义）");
        }
    }
}
