using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Environment;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    [TestFixture]
    public class MinimapFlashMathTests
    {
        // -------------------------------------------------------------
        // 1. 坐标换算四角与中心
        // -------------------------------------------------------------

        [Test]
        public void WorldToNormalized_Center_ReturnsHalf()
        {
            Vector2 origin = new Vector2(-20f, -28f);
            Vector2 mapSize = new Vector2(40f, 56f);

            Vector3 worldCenter = Vector3.zero;
            Vector2 norm = MinimapFlashMath.WorldToNormalized(worldCenter, origin, mapSize);

            Assert.AreEqual(0.5f, norm.x, 0.001f);
            Assert.AreEqual(0.5f, norm.y, 0.001f);

            Vector2 anchored = MinimapFlashMath.NormalizedToAnchoredPosition(norm, new Vector2(168f, 230f));
            Assert.AreEqual(0f, anchored.x, 0.001f);
            Assert.AreEqual(0f, anchored.y, 0.001f);
        }

        [Test]
        public void WorldToNormalized_FourCorners_ReturnsExactBounds()
        {
            Vector2 origin = new Vector2(-20f, -28f);
            Vector2 mapSize = new Vector2(40f, 56f);
            Vector2 containerSize = new Vector2(168f, 230f);

            // 左下角 (-20, -28)
            Vector3 blWorld = new Vector3(-20f, 0f, -28f);
            Vector2 blNorm = MinimapFlashMath.WorldToNormalized(blWorld, origin, mapSize);
            Assert.AreEqual(0.0f, blNorm.x, 0.001f);
            Assert.AreEqual(0.0f, blNorm.y, 0.001f);
            Vector2 blAnchored = MinimapFlashMath.NormalizedToAnchoredPosition(blNorm, containerSize);
            Assert.AreEqual(-84f, blAnchored.x, 0.001f);
            Assert.AreEqual(-115f, blAnchored.y, 0.001f);

            // 右上角 (20, 28)
            Vector3 trWorld = new Vector3(20f, 0f, 28f);
            Vector2 trNorm = MinimapFlashMath.WorldToNormalized(trWorld, origin, mapSize);
            Assert.AreEqual(1.0f, trNorm.x, 0.001f);
            Assert.AreEqual(1.0f, trNorm.y, 0.001f);
            Vector2 trAnchored = MinimapFlashMath.NormalizedToAnchoredPosition(trNorm, containerSize);
            Assert.AreEqual(84f, trAnchored.x, 0.001f);
            Assert.AreEqual(115f, trAnchored.y, 0.001f);

            // 左上角 (-20, 28)
            Vector3 tlWorld = new Vector3(-20f, 0f, 28f);
            Vector2 tlNorm = MinimapFlashMath.WorldToNormalized(tlWorld, origin, mapSize);
            Assert.AreEqual(0.0f, tlNorm.x, 0.001f);
            Assert.AreEqual(1.0f, tlNorm.y, 0.001f);
            Vector2 tlAnchored = MinimapFlashMath.NormalizedToAnchoredPosition(tlNorm, containerSize);
            Assert.AreEqual(-84f, tlAnchored.x, 0.001f);
            Assert.AreEqual(115f, tlAnchored.y, 0.001f);

            // 右下角 (20, -28)
            Vector3 brWorld = new Vector3(20f, 0f, -28f);
            Vector2 brNorm = MinimapFlashMath.WorldToNormalized(brWorld, origin, mapSize);
            Assert.AreEqual(1.0f, brNorm.x, 0.001f);
            Assert.AreEqual(0.0f, brNorm.y, 0.001f);
            Vector2 brAnchored = MinimapFlashMath.NormalizedToAnchoredPosition(brNorm, containerSize);
            Assert.AreEqual(84f, brAnchored.x, 0.001f);
            Assert.AreEqual(-115f, brAnchored.y, 0.001f);
        }

        [Test]
        public void WorldToNormalized_OutOfBounds_ClampedZeroToOne()
        {
            Vector2 origin = new Vector2(-20f, -28f);
            Vector2 mapSize = new Vector2(40f, 56f);

            Vector3 outPos = new Vector3(-100f, 0f, 200f);
            Vector2 norm = MinimapFlashMath.WorldToNormalized(outPos, origin, mapSize);

            Assert.AreEqual(0.0f, norm.x, 0.001f);
            Assert.AreEqual(1.0f, norm.y, 0.001f);
        }

        [Test]
        public void WorldToNormalized_DefaultArenaLayout_MatchesV2Constants()
        {
            Vector2 centerNorm = MinimapFlashMath.WorldToNormalized(Vector3.zero);
            Assert.AreEqual(0.5f, centerNorm.x, 0.001f);
            Assert.AreEqual(0.5f, centerNorm.y, 0.001f);

            Vector3 minPos = new Vector3(-ArenaLayoutV2.HalfX, 0f, -ArenaLayoutV2.HalfZ);
            Vector2 minNorm = MinimapFlashMath.WorldToNormalized(minPos);
            Assert.AreEqual(0.0f, minNorm.x, 0.001f);
            Assert.AreEqual(0.0f, minNorm.y, 0.001f);

            Vector3 maxPos = new Vector3(ArenaLayoutV2.HalfX, 0f, ArenaLayoutV2.HalfZ);
            Vector2 maxNorm = MinimapFlashMath.WorldToNormalized(maxPos);
            Assert.AreEqual(1.0f, maxNorm.x, 0.001f);
            Assert.AreEqual(1.0f, maxNorm.y, 0.001f);
        }

        [Test]
        public void WorldToMapAnchoredPosition_DegenerateSize_ReturnsSafeFallback()
        {
            Vector2 result = MinimapFlashMath.WorldToNormalized(new Vector3(10f, 0f, 10f), Vector2.zero, Vector2.zero);
            Assert.AreEqual(0.5f, result.x, 0.001f);
            Assert.AreEqual(0.5f, result.y, 0.001f);
        }

        // -------------------------------------------------------------
        // 2. 去重窗口
        // -------------------------------------------------------------

        [Test]
        public void ShouldMerge_SameLocationAndTimeAndTeam_ReturnsTrue()
        {
            Vector3 posA = new Vector3(5f, 0f, 10f);
            Vector3 posB = new Vector3(5.2f, 0f, 10.3f); // 距离约 0.36m <= 1.5m

            bool merge = MinimapFlashMath.ShouldMerge(
                posA, 1.0f, Team.TeamA,
                posB, 1.1f, Team.TeamA,
                1.5f, 0.25f);

            Assert.IsTrue(merge, "近距离同阵营短窗口内应合并");
        }

        [Test]
        public void ShouldMerge_DifferentTeam_ReturnsFalse()
        {
            Vector3 posA = new Vector3(5f, 0f, 10f);
            Vector3 posB = new Vector3(5f, 0f, 10f);

            bool merge = MinimapFlashMath.ShouldMerge(
                posA, 1.0f, Team.TeamA,
                posB, 1.0f, Team.TeamB,
                1.5f, 0.25f);

            Assert.IsFalse(merge, "不同阵营抢地不可合并");
        }

        [Test]
        public void ShouldMerge_ExceedsDistanceThreshold_ReturnsFalse()
        {
            Vector3 posA = new Vector3(0f, 0f, 0f);
            Vector3 posB = new Vector3(2.0f, 0f, 0f); // 2.0m > 1.5m

            bool merge = MinimapFlashMath.ShouldMerge(
                posA, 1.0f, Team.TeamA,
                posB, 1.05f, Team.TeamA,
                1.5f, 0.25f);

            Assert.IsFalse(merge, "超出合并半径的落墨不应合并");
        }

        [Test]
        public void ShouldMerge_ExceedsTimeWindow_ReturnsFalse()
        {
            Vector3 posA = new Vector3(0f, 0f, 0f);
            Vector3 posB = new Vector3(0.1f, 0f, 0.1f);

            bool merge = MinimapFlashMath.ShouldMerge(
                posA, 1.0f, Team.TeamA,
                posB, 1.35f, Team.TeamA, // 时间差 0.35s > 0.25s
                1.5f, 0.25f);

            Assert.IsFalse(merge, "超出时间窗口的后续落墨应产生新闪光");
        }

        [Test]
        public void TryAddWithDeduplication_MergesRapidSplatter()
        {
            var list = new List<MinimapFlashEvent>(64);

            var first = new MinimapFlashEvent(new Vector3(10f, 0f, 10f), Team.TeamA, 1.0f, 1.2f);
            bool addedFirst = MinimapFlashMath.TryAddWithDeduplication(list, first);
            Assert.IsTrue(addedFirst);
            Assert.AreEqual(1, list.Count);

            // 同位置附近快速追加
            var second = new MinimapFlashEvent(new Vector3(10.3f, 0f, 10.2f), Team.TeamA, 1.05f, 1.2f);
            bool addedSecond = MinimapFlashMath.TryAddWithDeduplication(list, second);
            Assert.IsFalse(addedSecond, "近距离同阵营应被合并过滤");
            Assert.AreEqual(1, list.Count);

            // 远距离追加
            var third = new MinimapFlashEvent(new Vector3(-5f, 0f, -5f), Team.TeamA, 1.05f, 1.2f);
            bool addedThird = MinimapFlashMath.TryAddWithDeduplication(list, third);
            Assert.IsTrue(addedThird);
            Assert.AreEqual(2, list.Count);
        }

        // -------------------------------------------------------------
        // 3. 批量上限
        // -------------------------------------------------------------

        [Test]
        public void DeduplicateAndLimit_EnforcesBatchCap()
        {
            var input = new List<MinimapFlashEvent>(100);
            for (int i = 0; i < 100; i++)
            {
                // 分布在不同位置，避免被空间去重合并
                input.Add(new MinimapFlashEvent(new Vector3(i * 3f - 150f, 0f, (i % 5) * 5f), Team.TeamA, 1.0f));
            }

            var output = new List<MinimapFlashEvent>(64);
            int count = MinimapFlashMath.DeduplicateAndLimit(input, output, maxCount: 64);

            Assert.AreEqual(64, count, "单批上限应严格截断至 64");
            Assert.AreEqual(64, output.Count);
        }

        [Test]
        public void DeduplicateAndLimit_SmallerCap_RespectsCustomLimit()
        {
            var input = new List<MinimapFlashEvent>(50);
            for (int i = 0; i < 50; i++)
            {
                input.Add(new MinimapFlashEvent(new Vector3(i * 2f, 0f, 0f), Team.TeamB, 1.0f));
            }

            var output = new List<MinimapFlashEvent>(32);
            int count = MinimapFlashMath.DeduplicateAndLimit(input, output, maxCount: 32);

            Assert.AreEqual(32, count);
            Assert.AreEqual(32, output.Count);
        }

        [Test]
        public void TryAddWithDeduplication_CapsAtMaxCapacity()
        {
            var list = new List<MinimapFlashEvent>(64);
            for (int i = 0; i < 64; i++)
            {
                bool added = MinimapFlashMath.TryAddWithDeduplication(
                    list,
                    new MinimapFlashEvent(new Vector3(i * 4f, 0f, 0f), Team.TeamA, 1.0f),
                    maxCapacity: 64);
                Assert.IsTrue(added);
            }

            Assert.AreEqual(64, list.Count);

            // 第 65 个事件应被容量上限拒绝
            bool overflowAdded = MinimapFlashMath.TryAddWithDeduplication(
                list,
                new MinimapFlashEvent(new Vector3(500f, 0f, 0f), Team.TeamA, 1.0f),
                maxCapacity: 64);

            Assert.IsFalse(overflowAdded, "达到最大容量后不再追加");
            Assert.AreEqual(64, list.Count);
        }

        // -------------------------------------------------------------
        // 4. Alpha 曲线端点
        // -------------------------------------------------------------

        [Test]
        public void EvaluateAlpha_AtStart_ReturnsInitialAlpha()
        {
            float alpha = MinimapFlashMath.EvaluateAlpha(0f, 0.4f, 0.9f);
            Assert.AreEqual(0.9f, alpha, 0.001f);
        }

        [Test]
        public void EvaluateAlpha_AtMidpoint_ReturnsLinearFade()
        {
            float alpha = MinimapFlashMath.EvaluateAlpha(0.2f, 0.4f, 0.9f);
            Assert.AreEqual(0.45f, alpha, 0.001f);
        }

        [Test]
        public void EvaluateAlpha_AtDuration_ReturnsZero()
        {
            float alpha = MinimapFlashMath.EvaluateAlpha(0.4f, 0.4f, 0.9f);
            Assert.AreEqual(0.0f, alpha, 0.001f);
        }

        [Test]
        public void EvaluateAlpha_PastDuration_ReturnsZero()
        {
            float alpha = MinimapFlashMath.EvaluateAlpha(0.5f, 0.4f, 0.9f);
            Assert.AreEqual(0.0f, alpha, 0.001f);
        }

        [Test]
        public void EvaluateAlpha_NegativeElapsed_ReturnsZero()
        {
            float alpha = MinimapFlashMath.EvaluateAlpha(-0.1f, 0.4f, 0.9f);
            Assert.AreEqual(0.0f, alpha, 0.001f);
        }

        [Test]
        public void EvaluateAlpha_ZeroOrNegativeLifetime_ReturnsZero()
        {
            float alphaZero = MinimapFlashMath.EvaluateAlpha(0f, 0f, 0.9f);
            Assert.AreEqual(0.0f, alphaZero, 0.001f);

            float alphaNeg = MinimapFlashMath.EvaluateAlpha(0f, -1f, 0.9f);
            Assert.AreEqual(0.0f, alphaNeg, 0.001f);
        }

        // -------------------------------------------------------------
        // 5. 节流边界
        // -------------------------------------------------------------

        [Test]
        public void ShouldFlush_BelowInterval_ReturnsFalse()
        {
            float lastFlush = 10.0f;
            float interval = 1f / 6f; // ~0.16667s

            // 0.10s 过去
            Assert.IsFalse(MinimapFlashMath.ShouldFlush(10.10f, lastFlush, interval));
            // 0.16s 过去
            Assert.IsFalse(MinimapFlashMath.ShouldFlush(10.16f, lastFlush, interval));
        }

        [Test]
        public void ShouldFlush_AtExactInterval_ReturnsTrue()
        {
            float lastFlush = 10.0f;
            float interval = 1f / 6f;

            Assert.IsTrue(MinimapFlashMath.ShouldFlush(10.0f + interval, lastFlush, interval));
        }

        [Test]
        public void ShouldFlush_BeyondInterval_ReturnsTrue()
        {
            float lastFlush = 10.0f;
            float interval = 1f / 6f;

            Assert.IsTrue(MinimapFlashMath.ShouldFlush(10.20f, lastFlush, interval));
        }

        [Test]
        public void ShouldFlush_ClockRollback_ReturnsTrue()
        {
            float lastFlush = 10.0f;
            float currentTime = 5.0f; // 场景重载或时钟重置

            Assert.IsTrue(MinimapFlashMath.ShouldFlush(currentTime, lastFlush));
        }

        // -------------------------------------------------------------
        // 6. MinimapView 池化生命周期与断言
        // -------------------------------------------------------------

        [Test]
        public void MinimapView_EnsureBuilt_InstantiatesFlashPool()
        {
            var go = new GameObject("TestMinimap", typeof(RectTransform), typeof(MinimapView));
            var view = go.GetComponent<MinimapView>();

            view.EnsureBuilt();

            Assert.AreEqual(64, view.FlashPoolSize, "闪光池应预分配 64 个节点");
            Assert.AreEqual(0, view.ActiveFlashCount, "初始活跃闪光应为 0");
            Assert.AreEqual(0, view.PendingFlashCount, "初始 pending 闪光应为 0");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void MinimapView_ClearAllFlashes_ResetsCounts()
        {
            var go = new GameObject("TestMinimap", typeof(RectTransform), typeof(MinimapView));
            var view = go.GetComponent<MinimapView>();
            view.EnsureBuilt();

            view.RecordFlash(new Vector3(0f, 0f, 0f), 1.5f, Team.TeamA);
            Assert.AreEqual(1, view.PendingFlashCount);

            view.ClearAllFlashes();
            Assert.AreEqual(0, view.PendingFlashCount);
            Assert.AreEqual(0, view.ActiveFlashCount);

            Object.DestroyImmediate(go);
        }
    }
}
