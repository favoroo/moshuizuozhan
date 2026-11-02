using NUnit.Framework;
using P1.Core;
using P1.Player;

namespace P1.Tests
{
    /// <summary>
    /// 本地玩家难度补偿（EditMode）：低难度给玩家加最大生命，普通/硬核保持基准，
    /// 倍率必须收口在安全区间，避免外部误传把血量清零。
    /// </summary>
    public class PlayerDifficultyMathTests
    {
        [Test]
        public void GetMaxHealthScale_BeginnerAndEasyAreTankier()
        {
            Assert.AreEqual(1.5f, PlayerDifficultyMath.GetMaxHealthScale(GameSettingsMath.BotDifficultyBeginner), 1e-4f);
            Assert.AreEqual(1.25f, PlayerDifficultyMath.GetMaxHealthScale(GameSettingsMath.BotDifficultyEasy), 1e-4f);
            Assert.AreEqual(1f, PlayerDifficultyMath.GetMaxHealthScale(GameSettingsMath.BotDifficultyNormal), 1e-4f);
            Assert.AreEqual(1f, PlayerDifficultyMath.GetMaxHealthScale(GameSettingsMath.BotDifficultyHard), 1e-4f);

            // 越界档位按最近档位收口，不能返回 0。
            Assert.AreEqual(1.5f, PlayerDifficultyMath.GetMaxHealthScale(-9), 1e-4f);
            Assert.AreEqual(1f, PlayerDifficultyMath.GetMaxHealthScale(99), 1e-4f);
        }

        [Test]
        public void ClampMaxHealthScale_RejectsDegenerateValues()
        {
            Assert.AreEqual(1f, PlayerDifficultyMath.ClampMaxHealthScale(float.NaN), 1e-4f);
            Assert.AreEqual(1f, PlayerDifficultyMath.ClampMaxHealthScale(float.PositiveInfinity), 1e-4f);
            Assert.AreEqual(PlayerDifficultyMath.MinMaxHealthScale, PlayerDifficultyMath.ClampMaxHealthScale(-3f), 1e-4f);
            Assert.AreEqual(PlayerDifficultyMath.MaxMaxHealthScale, PlayerDifficultyMath.ClampMaxHealthScale(99f), 1e-4f);
            Assert.AreEqual(1.5f, PlayerDifficultyMath.ClampMaxHealthScale(1.5f), 1e-4f);
        }
    }
}
