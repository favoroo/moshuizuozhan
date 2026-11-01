using NUnit.Framework;
using P1.Core;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 结算勋章判定算法纯逻辑验证（EditMode）：
    /// 验证 INKWAVE 7 种勋章的判定逻辑、并列归属、全 0 边界、1 人死亡、人数门槛及 MVP 计算公式。
    /// </summary>
    public class MatchAwardsTests
    {
        [Test]
        public void CalculateMvpScore_StandardStats_ComputesExactFormula()
        {
            // 公式：turf/maxTurf + 0.55*splats/maxSplats - 0.3*deaths/maxDeaths
            var stats = new CombatantMatchStats
            {
                TurfSquareMeters = 50f,
                Kills = 2,
                Deaths = 1
            };

            float score = MatchAwards.CalculateMvpScore(stats, maxTurf: 100f, maxSplats: 4, maxDeaths: 2);
            // 50/100 + 0.55*(2/4) - 0.3*(1/2) = 0.5 + 0.275 - 0.15 = 0.625
            Assert.AreEqual(0.625f, score, 1e-5f);
        }

        [Test]
        public void CalculateMvpScore_ZeroDenominators_HandledAsZero()
        {
            var stats = new CombatantMatchStats
            {
                TurfSquareMeters = 0f,
                Kills = 0,
                Deaths = 0
            };

            float score = MatchAwards.CalculateMvpScore(stats, maxTurf: 0f, maxSplats: 0, maxDeaths: 0);
            Assert.AreEqual(0f, score, 1e-5f);

            var statsWithKills = new CombatantMatchStats { Kills = 5 };
            float scoreTurfZero = MatchAwards.CalculateMvpScore(statsWithKills, maxTurf: 0f, maxSplats: 5, maxDeaths: 0);
            // 0 + 0.55*(5/5) - 0 = 0.55
            Assert.AreEqual(0.55f, scoreTurfZero, 1e-5f);
        }

        [Test]
        public void Evaluate_AllZeroStats_FourPlayers_AwardsUntouchableOnly()
        {
            var players = new[]
            {
                new ContestantAwardInput(Team.TeamA, 0f, 0, 0),
                new ContestantAwardInput(Team.TeamA, 0f, 0, 0),
                new ContestantAwardInput(Team.TeamB, 0f, 0, 0),
                new ContestantAwardInput(Team.TeamB, 0f, 0, 0)
            };

            var awards = MatchAwards.Evaluate(players);

            Assert.AreEqual(4, awards.Length);
            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(AwardType.Untouchable, awards[i],
                    $"全 0 且人数 >= 4 时参战者 {i} 应仅获得 Untouchable");
            }
        }

        [Test]
        public void Evaluate_Untouchable_RequiresAtLeastFourPlayers()
        {
            // 2 人对决，均未死亡
            var duel = new[]
            {
                new ContestantAwardInput(Team.TeamA, 100f, 0, 0),
                new ContestantAwardInput(Team.TeamB, 80f, 0, 0)
            };

            var awards = MatchAwards.Evaluate(duel);
            // 人数 < 4 时不应获得 Untouchable
            Assert.IsFalse((awards[0] & AwardType.Untouchable) != 0, "1v1 时即便 0 死也不发 Untouchable");
            Assert.IsFalse((awards[1] & AwardType.Untouchable) != 0, "1v1 时即便 0 死也不发 Untouchable");
        }

        [Test]
        public void Evaluate_OnePlayerDied_SurvivorAndUntouchableMutualExclusion()
        {
            // 4 人中 3 人 0 死，1 人 1 死
            var players = new[]
            {
                new ContestantAwardInput(Team.TeamA, 120f, 2, 0),
                new ContestantAwardInput(Team.TeamA, 80f, 1, 0),
                new ContestantAwardInput(Team.TeamB, 70f, 0, 0),
                new ContestantAwardInput(Team.TeamB, 60f, 0, 1)
            };

            var awards = MatchAwards.Evaluate(players);

            // 0 死者获得 Untouchable
            Assert.IsTrue((awards[0] & AwardType.Untouchable) != 0);
            Assert.IsTrue((awards[1] & AwardType.Untouchable) != 0);
            Assert.IsTrue((awards[2] & AwardType.Untouchable) != 0);

            // 1 死者不是全场最少死亡（全场最少是 0 死），因此不发 Survivor
            Assert.IsFalse((awards[3] & AwardType.Survivor) != 0, "全场存在 0 死时，有死者不应获得 Survivor");
            Assert.IsFalse((awards[3] & AwardType.Untouchable) != 0);
        }

        [Test]
        public void Evaluate_EveryoneDied_LowestDeathsGetsSurvivor()
        {
            // 所有人都死过，死亡最少者（>0）获得 Survivor
            var players = new[]
            {
                new ContestantAwardInput(Team.TeamA, 100f, 2, 1),
                new ContestantAwardInput(Team.TeamA, 80f, 1, 2),
                new ContestantAwardInput(Team.TeamB, 90f, 0, 3),
                new ContestantAwardInput(Team.TeamB, 60f, 0, 4)
            };

            var awards = MatchAwards.Evaluate(players);

            Assert.IsTrue((awards[0] & AwardType.Survivor) != 0, "全员死过时，最少死亡者 (1 死) 获得 Survivor");
            Assert.IsFalse((awards[0] & AwardType.Untouchable) != 0, "死过的人不得获得 Untouchable");
            Assert.IsFalse((awards[1] & AwardType.Survivor) != 0);
            Assert.IsFalse((awards[2] & AwardType.Survivor) != 0);
        }

        [Test]
        public void Evaluate_TurfKing_And_TopInker_CanStack()
        {
            var players = new[]
            {
                new ContestantAwardInput(Team.TeamA, 200f, 1, 1), // 全场最高，也是 A 队最高
                new ContestantAwardInput(Team.TeamA, 100f, 0, 1),
                new ContestantAwardInput(Team.TeamB, 150f, 1, 1), // B 队最高
                new ContestantAwardInput(Team.TeamB, 50f, 0, 1)
            };

            var awards = MatchAwards.Evaluate(players);

            // Player 0 既是全场最高 (Turf King) 又是本队最高 (Top Inker)
            Assert.IsTrue((awards[0] & AwardType.TurfKing) != 0, "Player 0 应获得 TurfKing");
            Assert.IsTrue((awards[0] & AwardType.TopInker) != 0, "Player 0 应同时获得 TopInker");

            // Player 2 是 B 队最高
            Assert.IsFalse((awards[2] & AwardType.TurfKing) != 0, "Player 2 不是全场最高");
            Assert.IsTrue((awards[2] & AwardType.TopInker) != 0, "Player 2 是 B 队最高 TopInker");
        }

        [Test]
        public void Evaluate_Ties_ShareAwards()
        {
            // 两名玩家全场涂地并列第一、击杀并列第一
            var players = new[]
            {
                new ContestantAwardInput(Team.TeamA, 150f, 3, 1),
                new ContestantAwardInput(Team.TeamB, 150f, 3, 1),
                new ContestantAwardInput(Team.TeamA, 80f, 0, 2),
                new ContestantAwardInput(Team.TeamB, 80f, 0, 2)
            };

            var awards = MatchAwards.Evaluate(players);

            Assert.IsTrue((awards[0] & AwardType.TurfKing) != 0, "并列第一者 0 获得 TurfKing");
            Assert.IsTrue((awards[1] & AwardType.TurfKing) != 0, "并列第一者 1 获得 TurfKing");

            Assert.IsTrue((awards[0] & AwardType.TopSplatter) != 0, "并列第一者 0 获得 TopSplatter");
            Assert.IsTrue((awards[1] & AwardType.TopSplatter) != 0, "并列第一者 1 获得 TopSplatter");

            Assert.IsTrue((awards[0] & AwardType.Mvp) != 0, "并列最高分者 0 获得 Mvp");
            Assert.IsTrue((awards[1] & AwardType.Mvp) != 0, "并列最高分者 1 获得 Mvp");
        }

        [Test]
        public void Evaluate_PurePainter_TopThreeTurfWithZeroKills()
        {
            var players = new[]
            {
                new ContestantAwardInput(Team.TeamA, 200f, 3, 1), // rank 1, kills > 0
                new ContestantAwardInput(Team.TeamA, 150f, 0, 1), // rank 2, kills == 0 -> PurePainter
                new ContestantAwardInput(Team.TeamB, 100f, 0, 1), // rank 3, kills == 0 -> PurePainter
                new ContestantAwardInput(Team.TeamB, 50f, 0, 1)   // rank 4 -> Not top 3
            };

            var awards = MatchAwards.Evaluate(players);

            Assert.IsFalse((awards[0] & AwardType.PurePainter) != 0, "kills > 0 不得获得 PurePainter");
            Assert.IsTrue((awards[1] & AwardType.PurePainter) != 0, "前三涂地且 0 杀应获得 PurePainter");
            Assert.IsTrue((awards[2] & AwardType.PurePainter) != 0, "前三涂地且 0 杀应获得 PurePainter");
            Assert.IsFalse((awards[3] & AwardType.PurePainter) != 0, "排名第四不得获得 PurePainter");
        }

        [Test]
        public void ExtractAwards_MatchesOrderAndCount()
        {
            AwardType mask = AwardType.Mvp | AwardType.TurfKing | AwardType.TopSplatter;
            Assert.AreEqual(3, MatchAwards.GetAwardCount(mask));

            var buffer = new AwardType[8];
            int extracted = MatchAwards.ExtractAwards(mask, buffer);

            Assert.AreEqual(3, extracted);
            Assert.AreEqual(AwardType.Mvp, buffer[0]);
            Assert.AreEqual(AwardType.TurfKing, buffer[1]);
            Assert.AreEqual(AwardType.TopSplatter, buffer[2]);
        }
    }
}
