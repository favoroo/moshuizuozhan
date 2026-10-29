using NUnit.Framework;
using P1.Core;
using P1.Ink;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 油漆质感重构的纯逻辑回归测试：地面像素不再带逐格噪声、墙面墨水边缘有亚格子覆盖率、
    /// 图集相关配置有合理默认值。
    /// </summary>
    public class InkSurfaceQualityTests
    {
        private static readonly Color32 Neutral = new Color32(56, 61, 71, 0);
        private static readonly Color32 TeamA = new Color32(255, 112, 15, 255);
        private static readonly Color32 TeamB = new Color32(38, 107, 255, 255);
        private static readonly Color32 Blocked = new Color32(25, 28, 36, 180);

        [Test]
        public void InkGrid_PaintedPixels_AreUniformTeamColor_NoPerCellNoise()
        {
            var grid = new InkGrid(100, 100, new Vector2(20f, 20f), null, Neutral, TeamA, TeamB, Blocked);

            grid.PaintCircle(Vector2.zero, 2.0f, Team.TeamA);

            Color32[] pixels = grid.GetPixelBuffer();
            int painted = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 p = pixels[i];
                if (p.a == 0)
                    continue;

                painted++;
                // 旧实现会按 (x,y) 哈希把 RGB 抖动 ±6~8，形成 0.1m 的马赛克砖块；
                // 现在必须是完全一致的队伍色。
                Assert.AreEqual(TeamA.r, p.r, "同一片墨水内不应出现逐格色差");
                Assert.AreEqual(TeamA.g, p.g, "同一片墨水内不应出现逐格色差");
                Assert.AreEqual(TeamA.b, p.b, "同一片墨水内不应出现逐格色差");
            }

            Assert.Greater(painted, 250);
        }

        [Test]
        public void InkGrid_ResetPaint_RestoresNeutralPixels()
        {
            var grid = new InkGrid(50, 50, new Vector2(10f, 10f), null, Neutral, TeamA, TeamB, Blocked);
            grid.PaintCircle(Vector2.zero, 2.0f, Team.TeamB);

            grid.ResetPaintOnly();

            Color32[] pixels = grid.GetPixelBuffer();
            for (int i = 0; i < pixels.Length; i++)
            {
                Assert.AreEqual(Neutral.r, pixels[i].r);
                Assert.AreEqual(Neutral.a, pixels[i].a);
            }
        }

        [Test]
        public void InkWallFace_PaintedEdge_WritesSubCellCoverage()
        {
            var face = new InkWallFace(
                Vector3.zero,
                Vector3.right,
                Vector3.up,
                Vector3.forward,
                4f,
                3f,
                0.1f,
                new Color32(0, 0, 0, 0),
                TeamA,
                TeamB,
                "TestWall");

            int changed = face.PaintCircle(new Vector2(2f, 1.5f), 0.8f, Team.TeamA);

            Assert.Greater(changed, 0);
            Assert.AreEqual(Team.TeamA, face.InkAt(new Vector2(2f, 1.5f)));

            int partial = 0;
            int full = 0;
            Color32[] pixels = face.PixelBuffer;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a == 0)
                    continue;

                if (pixels[i].a < 255)
                    partial++;
                else
                    full++;
            }

            Assert.Greater(partial, 0, "墙墨边缘必须写入 0<alpha<255 的亚格子覆盖率，否则仍会是方块阶梯");
            Assert.Greater(full, 0, "圆内部应保持完全不透明");
            Assert.GreaterOrEqual(full + partial, changed, "所有归属格都必须写出可见像素");
        }

        [Test]
        public void InkGridConfig_HasUsableAtlasDefaults()
        {
            var config = ScriptableObject.CreateInstance<InkGridConfigSO>();
            try
            {
                Assert.GreaterOrEqual(config.inkAtlasResolutionDesktop, 1024,
                    "桌面端图集至少要和玩法网格拉开数量级差距，否则边缘仍是阶梯");
                Assert.Greater(config.GetPlatformAtlasResolution(), 0);
                Assert.Greater(config.inkDryingSeconds, 0.5f);
                Assert.GreaterOrEqual(config.splatGrowSeconds, 0f);
                Assert.Greater(config.maxSplatsPerFrame, 0);
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
