using System.Collections.Generic;
using NUnit.Framework;
using P1.AI;
using P1.Core;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// [P3-C] Bot AI 纯逻辑验收：临界阻尼瞄准弹簧、墨水加权 A*、武器交火距离与残血撤退判定。
    /// 全部不依赖场景对象，EditMode 可秒级复现。
    /// </summary>
    public class BotAiTests
    {
        // ---------- 二阶临界阻尼瞄准弹簧 ----------

        [Test]
        public void AimSpring_CriticallyDamped_ConvergesWithoutVisibleOvershoot()
        {
            var spring = new HumanAimSpring();
            spring.Reset(0f);

            float maxAngle = float.MinValue;
            for (int i = 0; i < 120; i++)
            {
                spring.Step(90f, 13f, 573f, 1f, 1f / 60f);
                maxAngle = Mathf.Max(maxAngle, spring.AngleDegrees);
            }

            Assert.Greater(spring.AngleDegrees, 89.5f, "0.5s 内应基本收敛到目标角");
            Assert.Less(maxAngle, 90.5f, "临界阻尼不应出现可见过冲");
        }

        [Test]
        public void AimSpring_RespectsMaxTurnRate()
        {
            var spring = new HumanAimSpring();
            spring.Reset(0f);

            spring.Step(180f, 60f, 100f, 1f, 1f / 60f);

            Assert.LessOrEqual(Mathf.Abs(spring.AngularVelocityDegrees), 100f + 1e-3f,
                "角速度必须被转速上限钳制");
            Assert.LessOrEqual(Mathf.Abs(spring.AngleDegrees), 100f / 60f * 1.6f + 0.5f,
                "单帧转角应与转速上限同量级（消灭瞬间锁头）");
        }

        [Test]
        public void AimSpring_WalksShortestArcAcrossWrap()
        {
            var spring = new HumanAimSpring();
            spring.Reset(170f);

            float angle = spring.Step(-170f, 13f, 573f, 1f, 1f / 60f);

            Assert.Greater(angle, 170f, "170° → -170° 应走 +20° 的最短弧，而不是反向绕 340°");
        }

        [Test]
        public void AimSpring_IsStableUnderLargeFrameHitch()
        {
            var spring = new HumanAimSpring();
            spring.Reset(-100f);

            spring.Step(120f, 13f, 573f, 1f, 0.5f);

            Assert.IsFalse(float.IsNaN(spring.AngleDegrees));
            Assert.IsFalse(float.IsInfinity(spring.AngleDegrees));
            Assert.LessOrEqual(Mathf.Abs(spring.AngleDegrees), 180f);
        }

        [Test]
        public void AimTolerance_ShrinksWithDistance_AndWidensWhileFiring()
        {
            var bot = ScriptableObject.CreateInstance<BotConfigSO>();
            bot.aimToleranceDegrees = 9f;
            bot.aimToleranceGrowingScale = 1.6f;

            try
            {
                float near = BotTactics.AimToleranceDegrees(3f, bot, false);
                float far = BotTactics.AimToleranceDegrees(20f, bot, false);
                float nearFiring = BotTactics.AimToleranceDegrees(3f, bot, true);

                Assert.Greater(near, far, "距离越远，目标张角越小，开火门槛越严");
                Assert.Greater(nearFiring, near, "已经开火时应放宽容差，避免一帧抖动断火");
            }
            finally
            {
                Object.DestroyImmediate(bot);
            }
        }

        // ---------- 墨水加权 ----------

        [Test]
        public void InkPathCost_MapsTeamInkToWeights()
        {
            const float own = 0.5f, neutral = 1f, enemy = 3f;

            Assert.AreEqual(own, BotTactics.ResolveInkPathCost(Team.TeamA, Team.TeamA, own, neutral, enemy));
            Assert.AreEqual(own, BotTactics.ResolveInkPathCost(Team.TeamB, Team.TeamB, own, neutral, enemy));
            Assert.AreEqual(neutral, BotTactics.ResolveInkPathCost(Team.Neutral, Team.TeamA, own, neutral, enemy));
            Assert.AreEqual(enemy, BotTactics.ResolveInkPathCost(Team.TeamB, Team.TeamA, own, neutral, enemy));
            Assert.AreEqual(enemy, BotTactics.ResolveInkPathCost(Team.TeamA, Team.TeamB, own, neutral, enemy));
        }

        [Test]
        public void WeightedAStar_DetoursThroughOwnInk_InsteadOfCrossingEnemyInk()
        {
            // 9 x 3 格、0.5m/格：起始与终点都在中间行；中间行全是敌方墨，上下两行是己方墨。
            const int width = 9, height = 3;
            var walkable = new bool[width * height];
            for (int i = 0; i < walkable.Length; i++) walkable[i] = true;

            NavGrid grid = NavGrid.CreateFromMask(width, height, 0.5f, Vector2.zero, walkable);
            Vector3 start = grid.CellToWorld(0, 1);
            Vector3 goal = grid.CellToWorld(width - 1, 1);

            var plainPath = new List<Vector3>();
            Assert.IsTrue(grid.TryFindPath(start, goal, plainPath), "等权 A* 应能找到路径");

            var weightedPath = new List<Vector3>();
            bool found = grid.TryFindPath(start, goal, weightedPath, 0.55f,
                (x, y) => y == 1 ? 3f : 0.5f, 0.5f);
            Assert.IsTrue(found, "加权 A* 应能找到路径");

            bool leftEnemyLane = false;
            for (int i = 0; i < weightedPath.Count; i++)
            {
                if (!grid.TryWorldToCell(weightedPath[i], out int cx, out int cy)) continue;
                if (cy != 1) leftEnemyLane = true;
            }

            Assert.IsTrue(leftEnemyLane, "己方墨道 0.5 vs 敌方墨 3.0 时，路径应绕到己方墨行");
        }

        [Test]
        public void WeightedAStar_AvoidsNonWalkableCells()
        {
            const int width = 7, height = 5;
            var walkable = new bool[width * height];
            for (int i = 0; i < walkable.Length; i++) walkable[i] = true;
            for (int y = 0; y < height; y++) walkable[y * width + 3] = false; // 中间一堵墙

            NavGrid grid = NavGrid.CreateFromMask(width, height, 0.5f, Vector2.zero, walkable);
            var path = new List<Vector3>();

            Assert.IsFalse(grid.TryFindPath(grid.CellToWorld(1, 2), grid.CellToWorld(5, 2), path),
                "整列阻挡时不应虚构路径");
        }

        // ---------- 武器行为适配 ----------

        [Test]
        public void WeaponRoles_ProduceCloseMidAndLongEngagementDistances()
        {
            var bot = ScriptableObject.CreateInstance<BotConfigSO>();
            var shooter = ScriptableObject.CreateInstance<WeaponConfigSO>();
            var roller = ScriptableObject.CreateInstance<WeaponConfigSO>();
            var charger = ScriptableObject.CreateInstance<WeaponConfigSO>();

            try
            {
                shooter.weaponKind = WeaponKind.Shooter;
                shooter.maxRange = 7.2f;
                shooter.projectileSpeed = 21f;

                roller.weaponKind = WeaponKind.Roller;
                roller.maxRange = 6f;

                charger.weaponKind = WeaponKind.Charger;
                charger.maxRange = 27f;
                charger.chargeRangeMax = 27f;

                float mid = BotTactics.PreferredEngageDistance(WeaponKind.Shooter, shooter, bot);
                float close = BotTactics.PreferredEngageDistance(WeaponKind.Roller, roller, bot);
                float far = BotTactics.PreferredEngageDistance(WeaponKind.Charger, charger, bot);

                Assert.Less(close, 1.5f, "滚筒应贴脸");
                Assert.Greater(mid, close + 2f, "小枪应保持中距离");
                Assert.Greater(far, mid + 8f, "蓄力狙应拉开远距离");

                Assert.Greater(BotTactics.MaxFireDistance(WeaponKind.Charger, charger, bot), 20f,
                    "蓄力狙开火距离应覆盖满蓄力射程");
                Assert.Greater(BotTactics.SightRange(WeaponKind.Charger, charger, bot), far,
                    "索敌半径必须覆盖理想交火距离，否则狙永远看不到目标");
                Assert.AreEqual(0f, BotTactics.AimLeadSeconds(WeaponKind.Charger, charger, 20f),
                    "蓄力狙是即时光束，不需要提前量");
                Assert.Greater(BotTactics.AimLeadSeconds(WeaponKind.Shooter, shooter, 6f), 0f,
                    "小枪弹丸有飞行时间，需要提前量");
            }
            finally
            {
                Object.DestroyImmediate(bot);
                Object.DestroyImmediate(shooter);
                Object.DestroyImmediate(roller);
                Object.DestroyImmediate(charger);
            }
        }

        // ---------- 残血撤退 ----------

        [Test]
        public void ShouldRetreat_RequiresLowHealthCloseThreatAndNoCooldown()
        {
            var bot = ScriptableObject.CreateInstance<BotConfigSO>();
            bot.retreatHealthThreshold = 40f;
            bot.retreatTriggerRange = 14f;

            try
            {
                Assert.IsTrue(BotTactics.ShouldRetreat(30f, 6f, bot, 0f), "残血 + 近敌应撤退");
                Assert.IsFalse(BotTactics.ShouldRetreat(60f, 6f, bot, 0f), "血量健康不撤退");
                Assert.IsFalse(BotTactics.ShouldRetreat(30f, 20f, bot, 0f), "威胁太远不撤退");
                Assert.IsFalse(BotTactics.ShouldRetreat(30f, 6f, bot, 1.5f), "撤退冷却中不重复触发");
            }
            finally
            {
                Object.DestroyImmediate(bot);
            }
        }
    }
}
