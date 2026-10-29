using NUnit.Framework;
using P1.Core;
using P1.UI;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// P4-A 弹簧 / 染色数学验证：纯逻辑，不依赖场景与 UI 生命周期。
    /// </summary>
    public class UISpringMathTests
    {
        [Test]
        public void CubicBezierSpring_EndpointsAndOvershoot()
        {
            UICubicBezier spring = UICubicBezier.Spring;

            Assert.AreEqual(0f, spring.Evaluate(0f), 1e-5f);
            Assert.AreEqual(1f, spring.Evaluate(1f), 1e-5f);

            float peak = 0f;
            for (int i = 0; i <= 100; i++)
            {
                peak = Mathf.Max(peak, spring.Evaluate(i / 100f));
            }

            Assert.Greater(peak, 1.05f, "cubic-bezier(.34, 1.56, .64, 1) 应当明显过冲");
            Assert.Less(peak, 1.3f, "过冲幅度不应失控");
        }

        [Test]
        public void CubicBezierEaseOut_IsMonotonic()
        {
            UICubicBezier easeOut = UICubicBezier.EaseOut;

            float previous = 0f;
            for (int i = 1; i <= 40; i++)
            {
                float value = easeOut.Evaluate(i / 40f);
                Assert.GreaterOrEqual(value, previous - 1e-4f, "EaseOut 必须单调递增");
                previous = value;
            }

            Assert.AreEqual(1f, previous, 1e-4f);
        }

        [Test]
        public void SpringFloat_ConvergesWithOvershoot()
        {
            UISpringFloat spring = new UISpringFloat();
            spring.Set(0f);

            UISpringSettings settings = UISpringSettings.Pop;
            float peak = 0f;
            bool settled = false;

            for (int i = 0; i < 240; i++)
            {
                settled = spring.Step(1f, settings, 1f / 60f);
                peak = Mathf.Max(peak, spring.Value);
                if (settled) break;
            }

            Assert.Greater(peak, 1.01f, "Pop 弹簧应当有过冲");
            Assert.IsTrue(settled, "Pop 弹簧应在 4 秒内收敛");
            Assert.AreEqual(1f, spring.Value, 1e-4f);
            Assert.AreEqual(0f, spring.Velocity, 1e-4f);
        }

        [Test]
        public void SpringVector3_RemainsStableAtLargeDeltaTime()
        {
            UISpringVector3 spring = new UISpringVector3();
            spring.Set(Vector3.zero);

            for (int i = 0; i < 160; i++)
            {
                spring.Step(Vector3.one * 2f, UISpringSettings.Pop, 0.1f);
            }

            Assert.IsFalse(float.IsNaN(spring.Value.x), "子步积分应避免低帧率数值爆炸");
            Assert.IsFalse(float.IsInfinity(spring.Value.x));
            Assert.AreEqual(2f, spring.Value.x, 1e-2f);
        }

        [Test]
        public void InkTint_WhiteIconBecomesExactTeamColor()
        {
            Color team = new Color(1f, 0.44f, 0.06f, 1f);
            Color tinted = InkTeamColorFilter.TintColor(Color.white, team, 1f);

            Assert.AreEqual(team.r, tinted.r, 1e-4f);
            Assert.AreEqual(team.g, tinted.g, 1e-4f);
            Assert.AreEqual(team.b, tinted.b, 1e-4f);
            Assert.AreEqual(1f, tinted.a, 1e-4f);
        }

        [Test]
        public void InkTint_PreservesIconAlpha()
        {
            Color icon = new Color(1f, 1f, 1f, 0.35f);
            Color team = new Color(0.1f, 0.2f, 0.3f, 1f);

            Color tinted = InkTeamColorFilter.TintColor(icon, team, 1f, true);
            Assert.AreEqual(0.35f, tinted.a, 1e-4f);

            Color full = InkTeamColorFilter.TintColor(icon, team, 1f, false);
            Assert.AreEqual(1f, full.a, 1e-4f);
        }

        [Test]
        public void ThemeFallback_HasDistinctTeamInkColors()
        {
            UIThemeSO theme = UIThemeSO.Fallback;

            Color teamA = theme.GetTeamColor(Team.TeamA);
            Color teamB = theme.GetTeamColor(Team.TeamB);
            Color neutral = theme.GetTeamColor(Team.Neutral);

            Assert.AreNotEqual(teamA, teamB);
            Assert.Greater(teamA.r, teamB.r, "A 队应偏橙红");
            Assert.Greater(teamB.b, teamA.b, "B 队应偏蓝");
            Assert.AreEqual(1f, neutral.a, 1f);
        }
    }
}