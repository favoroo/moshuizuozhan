using NUnit.Framework;
using P1.AI;
using P1.Core;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// Bot 难度分层纯逻辑验收（EditMode）：
    /// 普通档必须是恒等档（不克隆、不改任何字段），轻松/硬核只沿 INKWAVE 的
    /// 反应/瞄准误差/弹簧刚度/转速/感知半径五个维度做单向往外扩，
    /// 并且永远不能把原始 BotConfig 资产改脏。
    /// </summary>
    public class BotDifficultyTests
    {
        private BotConfigSO _baseConfig;

        [SetUp]
        public void SetUp()
        {
            _baseConfig = ScriptableObject.CreateInstance<BotConfigSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_baseConfig != null)
            {
                Object.DestroyImmediate(_baseConfig);
                _baseConfig = null;
            }
        }

        [Test]
        public void LevelConstants_MatchGameSettings()
        {
            Assert.AreEqual(GameSettingsMath.MinBotDifficulty, BotDifficultyMath.MinLevel);
            Assert.AreEqual(GameSettingsMath.MaxBotDifficulty, BotDifficultyMath.MaxLevel);
            Assert.AreEqual(GameSettingsMath.DefaultBotDifficulty, BotDifficultyMath.DefaultLevel);

            Assert.AreEqual(GameSettingsMath.BotDifficultyBeginner, BotDifficultyMath.LevelBeginner);
            Assert.AreEqual(GameSettingsMath.BotDifficultyEasy, BotDifficultyMath.LevelEasy);
            Assert.AreEqual(GameSettingsMath.BotDifficultyNormal, BotDifficultyMath.LevelNormal);
            Assert.AreEqual(GameSettingsMath.BotDifficultyHard, BotDifficultyMath.LevelHard);
            Assert.AreEqual(MenuLayoutMath.DifficultyTierCount, BotDifficultyMath.MaxLevel - BotDifficultyMath.MinLevel + 1,
                "难度档数在 UI 版面表与 AI 档位区间必须一致");
        }

        [Test]
        public void Normalize_ClampsToFourTiers()
        {
            Assert.AreEqual(BotDifficultyMath.LevelBeginner, BotDifficultyMath.Normalize(-7));
            Assert.AreEqual(BotDifficultyMath.LevelEasy, BotDifficultyMath.Normalize(1));
            Assert.AreEqual(BotDifficultyMath.LevelHard, BotDifficultyMath.Normalize(42));
        }

        [Test]
        public void Labels_AreDistinctAndNonEmpty()
        {
            var labels = new System.Collections.Generic.HashSet<string>();
            for (int level = BotDifficultyMath.MinLevel; level <= BotDifficultyMath.MaxLevel; level++)
            {
                string label = BotDifficultyMath.GetLabel(level);
                Assert.IsNotEmpty(label);
                Assert.IsTrue(labels.Add(label), "难度档位文案不得重复：" + label);
            }

            Assert.AreEqual(4, labels.Count, "应有新手 / 轻松 / 普通 / 硬核四档");
        }

        [Test]
        public void NormalProfile_IsIdentity_AndReturnsSourceAsset()
        {
            BotDifficultyMath.SkillProfile profile = BotDifficultyMath.GetProfile(BotDifficultyMath.LevelNormal);
            Assert.AreEqual(1f, profile.reactionTime, 1e-5f);
            Assert.AreEqual(1f, profile.aimError, 1e-5f);
            Assert.AreEqual(1f, profile.aimSpringOmega, 1e-5f);
            Assert.AreEqual(1f, profile.aimMaxTurnRate, 1e-5f);
            Assert.AreEqual(1f, profile.sightRangeScale, 1e-5f);
            Assert.AreEqual(0, profile.specialMinEnemiesDelta);

            BotConfigSO runtime = BotDifficultyMath.CreateRuntimeConfig(_baseConfig, BotDifficultyMath.LevelNormal);
            Assert.AreSame(_baseConfig, runtime, "普通档必须直接复用原资产，不产生运行时克隆");
        }

        [Test]
        public void Easy_MakesBotsSlowerAndLessAccurate()
        {
            float baseReaction = _baseConfig.reactionTime;
            float baseError = _baseConfig.aimErrorDegrees;
            float baseOmega = _baseConfig.aimSpringOmega;
            float baseTurn = _baseConfig.aimMaxTurnRateDegrees;
            float baseSightScale = _baseConfig.sightRangeScale;
            float basePerception = _baseConfig.perceptionInterval;
            int baseSpecialMinEnemies = _baseConfig.specialMinEnemies;

            BotConfigSO easy = BotDifficultyMath.CreateRuntimeConfig(_baseConfig, BotDifficultyMath.LevelEasy);
            Assert.IsNotNull(easy);
            Assert.AreNotSame(_baseConfig, easy);

            try
            {
                Assert.Greater(easy.reactionTime, baseReaction, "轻松档反应必须更慢");
                Assert.Greater(easy.aimErrorDegrees, baseError, "轻松档瞄准误差必须更大");
                Assert.Greater(easy.aimWanderDegrees, _baseConfig.aimWanderDegrees, "轻松档慢漂移必须更大");
                Assert.Less(easy.aimSpringOmega, baseOmega, "轻松档跟枪弹簧必须更软");
                Assert.Less(easy.aimMaxTurnRateDegrees, baseTurn, "轻松档甩枪转速必须更低");
                Assert.Less(easy.sightRangeScale, baseSightScale, "轻松档索敌半径倍率必须更短");
                Assert.Greater(easy.perceptionInterval, basePerception, "轻松档决策节拍必须更慢");
                Assert.Greater(easy.specialMinEnemies, baseSpecialMinEnemies, "轻松档大招门槛必须更保守");
                Assert.Less(easy.bombUseChance, _baseConfig.bombUseChance, "轻松档炸弹使用率必须更低");

                // 原始资产绝不能被改脏（否则所有难度都会叠加缩放）。
                Assert.AreEqual(baseReaction, _baseConfig.reactionTime, 1e-5f, "原始资产 reactionTime 被改写");
                Assert.AreEqual(baseError, _baseConfig.aimErrorDegrees, 1e-5f, "原始资产 aimError 被改写");
            }
            finally
            {
                Object.DestroyImmediate(easy);
            }
        }

        [Test]
        public void Beginner_IsClearlyFriendlierThanEasy()
        {
            BotConfigSO beginner = BotDifficultyMath.CreateRuntimeConfig(_baseConfig, BotDifficultyMath.LevelBeginner);
            BotConfigSO easy = BotDifficultyMath.CreateRuntimeConfig(_baseConfig, BotDifficultyMath.LevelEasy);
            Assert.IsNotNull(beginner);
            Assert.IsNotNull(easy);

            try
            {
                Assert.Greater(beginner.reactionTime, easy.reactionTime, "新手档反应必须比轻松更慢");
                Assert.Greater(beginner.aimErrorDegrees, easy.aimErrorDegrees, "新手档瞄准误差必须比轻松更大");
                Assert.Greater(beginner.aimWanderDegrees, easy.aimWanderDegrees, "新手档慢漂移必须比轻松更大");
                Assert.Less(beginner.aimSpringOmega, easy.aimSpringOmega, "新手档跟枪必须比轻松更软");
                Assert.Less(beginner.aimMaxTurnRateDegrees, easy.aimMaxTurnRateDegrees, "新手档甩枪必须比轻松更慢");
                Assert.Less(beginner.sightRangeScale, easy.sightRangeScale, "新手档索敌必须比轻松更短");
                Assert.Greater(beginner.perceptionInterval, easy.perceptionInterval, "新手档决策必须比轻松更慢");
                Assert.Greater(beginner.specialMinEnemies, easy.specialMinEnemies, "新手档大招门槛必须更保守");
                Assert.Less(beginner.bombUseChance, easy.bombUseChance, "新手档炸弹使用率必须更低");
                Assert.Less(beginner.retreatHealthThreshold, easy.retreatHealthThreshold, "新手档应更少撤退（更容易被打死）");

                // 手机端最低档的硬指标：反应 ≥0.45s、瞄准误差 ≥12°、索敌倍率 ≤0.6。
                Assert.GreaterOrEqual(beginner.reactionTime, 0.45f);
                Assert.GreaterOrEqual(beginner.aimErrorDegrees, 12f);
                Assert.LessOrEqual(beginner.sightRangeScale, 0.6f);
            }
            finally
            {
                Object.DestroyImmediate(beginner);
                Object.DestroyImmediate(easy);
            }
        }

        [Test]
        public void Hard_MakesBotsSharper()
        {
            float baseReaction = _baseConfig.reactionTime;
            float baseError = _baseConfig.aimErrorDegrees;
            float baseOmega = _baseConfig.aimSpringOmega;
            float baseTurn = _baseConfig.aimMaxTurnRateDegrees;
            float baseSightScale = _baseConfig.sightRangeScale;
            float basePerception = _baseConfig.perceptionInterval;

            BotConfigSO hard = BotDifficultyMath.CreateRuntimeConfig(_baseConfig, BotDifficultyMath.LevelHard);
            Assert.IsNotNull(hard);

            try
            {
                Assert.Less(hard.reactionTime, baseReaction, "硬核档反应必须更快");
                Assert.Less(hard.aimErrorDegrees, baseError, "硬核档瞄准误差必须更小");
                Assert.Less(hard.aimWanderDegrees, _baseConfig.aimWanderDegrees, "硬核档慢漂移必须更小");
                Assert.Greater(hard.aimSpringOmega, baseOmega, "硬核档跟枪弹簧必须更硬");
                Assert.Greater(hard.aimMaxTurnRateDegrees, baseTurn, "硬核档甩枪转速必须更高");
                Assert.Greater(hard.sightRangeScale, baseSightScale, "硬核档索敌半径倍率必须更远");
                Assert.Less(hard.perceptionInterval, basePerception, "硬核档决策节拍必须更快");
            }
            finally
            {
                Object.DestroyImmediate(hard);
            }
        }

        [Test]
        public void ApplyTo_Normal_LeavesValuesUntouched()
        {
            float reaction = _baseConfig.reactionTime;
            float error = _baseConfig.aimErrorDegrees;
            float omega = _baseConfig.aimSpringOmega;

            BotDifficultyMath.ApplyTo(_baseConfig, BotDifficultyMath.LevelNormal);

            Assert.AreEqual(reaction, _baseConfig.reactionTime, 1e-6f);
            Assert.AreEqual(error, _baseConfig.aimErrorDegrees, 1e-6f);
            Assert.AreEqual(omega, _baseConfig.aimSpringOmega, 1e-6f);
        }

        [Test]
        public void ApplyTo_ClampsIntoPhysicallySafeRanges()
        {
            // 极端资产值：确保缩放后仍落在 BotController 可安全消费的区间。
            _baseConfig.reactionTime = 0.06f;
            _baseConfig.perceptionInterval = 0.06f;
            _baseConfig.retreatHealthThreshold = 70f;

            BotConfigSO easy = BotDifficultyMath.CreateRuntimeConfig(_baseConfig, BotDifficultyMath.LevelEasy);
            try
            {
                Assert.GreaterOrEqual(easy.reactionTime, 0.05f);
                Assert.GreaterOrEqual(easy.perceptionInterval, BotConfigSO.MinPerceptionInterval);
                Assert.LessOrEqual(easy.perceptionInterval, BotConfigSO.MaxPerceptionInterval);
                Assert.LessOrEqual(easy.retreatHealthThreshold, 59f);
                Assert.IsTrue(easy.bombUseChance >= 0f && easy.bombUseChance <= 1f);
            }
            finally
            {
                Object.DestroyImmediate(easy);
            }
        }
    }
}
