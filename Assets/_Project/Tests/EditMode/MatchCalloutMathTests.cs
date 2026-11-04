using NUnit.Framework;
using P1.Core;
using P1.UI;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// Callout 播报层纯逻辑测试（对齐 INKWAVE 播报链）：
    /// 验证优先级链：WIPEOUT > MULTI SPLAT > FIRST SPLAT > REVENGE > SHUTDOWN > STREAK；
    /// 验证 4.2s 连杀时间窗口边界、首杀单次门禁、复仇目标匹配、连杀终结阈值、团灭判定与复活去抖、Reset 状态重置。
    /// </summary>
    [TestFixture]
    public class MatchCalloutMathTests
    {
        private MatchCalloutMath _math;

        [SetUp]
        public void SetUp()
        {
            _math = new MatchCalloutMath();
        }

        private static KillFeedEvent CreateLocalKill(string victim = "BotB_1", Team victimTeam = Team.TeamB)
        {
            return new KillFeedEvent
            {
                KillerName = "YOU",
                KillerTeam = Team.TeamA,
                VictimName = victim,
                VictimTeam = victimTeam,
                WeaponKind = WeaponKind.Shooter,
                WeaponDisplayName = "Spritzer",
                IsLocalPlayerKiller = true,
                IsLocalPlayerVictim = false
            };
        }

        private static KillFeedEvent CreateLocalDeath(string killer = "BotB_1")
        {
            return new KillFeedEvent
            {
                KillerName = killer,
                KillerTeam = Team.TeamB,
                VictimName = "YOU",
                VictimTeam = Team.TeamA,
                WeaponKind = WeaponKind.Shooter,
                WeaponDisplayName = "Spritzer",
                IsLocalPlayerKiller = false,
                IsLocalPlayerVictim = true
            };
        }

        private static KillFeedEvent CreateBotKill(string killer, string victim, Team kTeam = Team.TeamB, Team vTeam = Team.TeamA)
        {
            return new KillFeedEvent
            {
                KillerName = killer,
                KillerTeam = kTeam,
                VictimName = victim,
                VictimTeam = vTeam,
                WeaponKind = WeaponKind.Shooter,
                WeaponDisplayName = "Spritzer",
                IsLocalPlayerKiller = false,
                IsLocalPlayerVictim = false
            };
        }

        [Test]
        public void PriorityChain_OrderIsStrictlyRespected()
        {
            Assert.Greater((int)CalloutKind.Wipeout, (int)CalloutKind.MultiSplat, "WIPEOUT 优先级应高于 MULTI SPLAT");
            Assert.Greater((int)CalloutKind.MultiSplat, (int)CalloutKind.FirstSplat, "MULTI SPLAT 优先级应高于 FIRST SPLAT");
            Assert.Greater((int)CalloutKind.FirstSplat, (int)CalloutKind.Revenge, "FIRST SPLAT 优先级应高于 REVENGE");
            Assert.Greater((int)CalloutKind.Revenge, (int)CalloutKind.Shutdown, "REVENGE 优先级应高于 SHUTDOWN");
            Assert.Greater((int)CalloutKind.Shutdown, (int)CalloutKind.Streak, "SHUTDOWN 优先级应高于 STREAK");
            Assert.Greater((int)CalloutKind.Streak, (int)CalloutKind.None, "STREAK 优先级应高于 NONE");
        }

        [Test]
        public void MultiSplat_WindowBoundary_Within4_2Seconds()
        {
            // 4.2s 窗口内多杀：Double / Triple / Quad（4 次击杀全部落在同一 4.2s 窗口内）
            var ev1 = CreateLocalKill("BotB_1");
            var r1 = _math.Evaluate(ev1, 10.0f, 4, 3);
            Assert.AreEqual(CalloutKind.FirstSplat, r1.Kind, "首个击杀且未播报过首杀时为 FIRST SPLAT");

            var ev2 = CreateLocalKill("BotB_2");
            var r2 = _math.Evaluate(ev2, 11.0f, 4, 2);
            Assert.AreEqual(CalloutKind.MultiSplat, r2.Kind);
            Assert.AreEqual(MatchCalloutMath.TextDoubleSplat, r2.MainText);
            Assert.AreEqual(2, r2.Count, "双杀徽章应显示 ×2");
            Assert.AreEqual("mark_splat", r2.IconKey);
            Assert.IsFalse(r2.IsBig, "Double Splat 为常规字号");

            var ev3 = CreateLocalKill("BotB_3");
            var r3 = _math.Evaluate(ev3, 12.0f, 4, 1);
            Assert.AreEqual(CalloutKind.MultiSplat, r3.Kind);
            Assert.AreEqual(MatchCalloutMath.TextTripleSplat, r3.MainText);
            Assert.AreEqual(3, r3.Count, "三杀徽章应显示 ×3");
            Assert.IsTrue(r3.IsBig, "Triple Splat 判定为大播报");

            var ev4 = CreateLocalKill("BotB_4");
            var r4 = _math.Evaluate(ev4, 13.0f, 4, 1);
            Assert.AreEqual(CalloutKind.MultiSplat, r4.Kind);
            Assert.AreEqual(MatchCalloutMath.TextQuadSplat, r4.MainText);
            Assert.IsTrue(r4.IsBig, "Quad Splat 判定为大播报");
        }

        [Test]
        public void MultiSplat_WindowBoundary_Exceeding4_2Seconds_Expires()
        {
            // 超过 4.2s 窗口则不触发多杀
            var ev1 = CreateLocalKill("BotB_1");
            _math.Evaluate(ev1, 10.0f, 4, 3);

            // 14.25s 与 10.0s 相差 4.25s > 4.2s
            var ev2 = CreateLocalKill("BotB_2");
            var r2 = _math.Evaluate(ev2, 14.25f, 4, 2);

            Assert.AreNotEqual(CalloutKind.MultiSplat, r2.Kind, "超出 4.2s 窗口不应触发 MultiSplat");
            Assert.AreEqual(1, _math.LocalKillCountInWindow, "窗口内应只剩当前这 1 次击杀");
        }

        [Test]
        public void FirstSplat_TriggersOnlyOncePerMatch()
        {
            var ev1 = CreateLocalKill("BotB_1");
            var r1 = _math.Evaluate(ev1, 2.0f, 4, 3);
            Assert.AreEqual(CalloutKind.FirstSplat, r1.Kind);
            Assert.AreEqual(MatchCalloutMath.TextFirstSplat, r1.MainText);
            Assert.IsTrue(_math.HasPlayedFirstSplat);

            // 5s 后再击杀（超出多杀窗口，无复仇与终结目标）
            var ev2 = CreateLocalKill("BotB_2");
            var r2 = _math.Evaluate(ev2, 10.0f, 4, 2);
            Assert.AreNotEqual(CalloutKind.FirstSplat, r2.Kind, "整局比赛中 FIRST SPLAT 只能触发一次");
        }

        [Test]
        public void FirstSplat_ConsumedEvenIfFirstKillWasMultiSplat()
        {
            // 首杀如果连续双杀（例如副武器炸双人），第二下为 DOUBLE SPLAT 覆盖 FIRST SPLAT
            var ev1 = CreateLocalKill("BotB_1");
            _math.Evaluate(ev1, 1.0f, 4, 3);

            var ev2 = CreateLocalKill("BotB_2");
            var r2 = _math.Evaluate(ev2, 1.5f, 4, 2);
            Assert.AreEqual(CalloutKind.MultiSplat, r2.Kind);

            // 之后单杀不再有 FIRST SPLAT
            var ev3 = CreateLocalKill("BotB_3");
            var r3 = _math.Evaluate(ev3, 10.0f, 4, 1);
            Assert.AreNotEqual(CalloutKind.FirstSplat, r3.Kind);
        }

        [Test]
        public void Revenge_TriggersWhenKillingLastKiller()
        {
            // 消耗掉 FirstSplat
            _math.Evaluate(CreateLocalKill("BotB_1"), 1.0f, 4, 3);

            // 本地玩家被 BotB_2 击杀
            var death = CreateLocalDeath("BotB_2");
            _math.Evaluate(death, 5.0f, 4, 4);
            Assert.AreEqual("BotB_2", _math.LastKillerName);

            // 击杀其他非仇人
            var killOther = CreateLocalKill("BotB_3");
            var rOther = _math.Evaluate(killOther, 15.0f, 4, 3);
            Assert.AreNotEqual(CalloutKind.Revenge, rOther.Kind);
            Assert.AreEqual("BotB_2", _math.LastKillerName, "仇人记录应保持，直到击杀仇人");

            // 击杀仇人 BotB_2
            var killKiller = CreateLocalKill("BotB_2");
            var rRevenge = _math.Evaluate(killKiller, 25.0f, 4, 2);
            Assert.AreEqual(CalloutKind.Revenge, rRevenge.Kind);
            Assert.AreEqual(MatchCalloutMath.TextRevenge, rRevenge.MainText);
            Assert.IsNull(_math.LastKillerName, "复仇成功后最近击杀者记录应清除");

            // 再次击杀 BotB_2（未重新被击杀过）不再触发复仇
            var killAgain = CreateLocalKill("BotB_2");
            var rAgain = _math.Evaluate(killAgain, 35.0f, 4, 1);
            Assert.AreNotEqual(CalloutKind.Revenge, rAgain.Kind);
        }

        [Test]
        public void Shutdown_TriggersWhenEndingStreakOf3OrMore()
        {
            // 消耗 FirstSplat
            _math.Evaluate(CreateLocalKill("BotB_99"), 1.0f, 4, 3);

            // 敌方 BotB_Ace 连续击杀 3 名我方队友
            _math.Evaluate(CreateBotKill("BotB_Ace", "BotA_1"), 5.0f, 4, 3);
            _math.Evaluate(CreateBotKill("BotB_Ace", "BotA_2"), 8.0f, 4, 2);
            _math.Evaluate(CreateBotKill("BotB_Ace", "BotA_3"), 11.0f, 4, 1);
            Assert.AreEqual(3, _math.GetActorStreak("BotB_Ace"));

            // 本地玩家终结 BotB_Ace
            var killAce = CreateLocalKill("BotB_Ace");
            var r = _math.Evaluate(killAce, 20.0f, 4, 3);

            Assert.AreEqual(CalloutKind.Shutdown, r.Kind);
            Assert.AreEqual(MatchCalloutMath.TextShutdown, r.MainText);
            Assert.AreEqual(MatchCalloutMath.SubShutdown, r.SubText);
            Assert.AreEqual(0, _math.GetActorStreak("BotB_Ace"), "受害者阵亡后连杀数归零");
        }

        [Test]
        public void Shutdown_NotTriggeredIfStreakUnderThreshold()
        {
            _math.Evaluate(CreateLocalKill("BotB_99"), 1.0f, 4, 3);

            // 敌方 BotB_1 只拿了 2 连杀（未达阈值 3）
            _math.Evaluate(CreateBotKill("BotB_1", "BotA_1"), 5.0f, 4, 3);
            _math.Evaluate(CreateBotKill("BotB_1", "BotA_2"), 8.0f, 4, 2);
            Assert.AreEqual(2, _math.GetActorStreak("BotB_1"));

            var kill = CreateLocalKill("BotB_1");
            var r = _math.Evaluate(kill, 20.0f, 4, 3);
            Assert.AreNotEqual(CalloutKind.Shutdown, r.Kind);
        }

        [Test]
        public void Streak_TriggersAtStreak3AndAbove()
        {
            _math.Evaluate(CreateLocalKill("BotB_1"), 1.0f, 4, 3); // Kill 1 (FirstSplat)
            var r2 = _math.Evaluate(CreateLocalKill("BotB_2"), 10.0f, 4, 2); // Kill 2
            Assert.AreEqual(CalloutKind.None, r2.Kind);

            var r3 = _math.Evaluate(CreateLocalKill("BotB_3"), 20.0f, 4, 1); // Kill 3
            Assert.AreEqual(CalloutKind.Streak, r3.Kind);
            Assert.AreEqual(MatchCalloutMath.TextStreak, r3.MainText);
            Assert.AreEqual(3, r3.Count, "连杀徽章应显示 ×3");

            var r4 = _math.Evaluate(CreateLocalKill("BotB_4"), 30.0f, 4, 1); // Kill 4
            Assert.AreEqual(CalloutKind.Streak, r4.Kind);
            Assert.AreEqual(MatchCalloutMath.TextStreak, r4.MainText);
            Assert.AreEqual(4, r4.Count, "连杀徽章应显示 ×4");
        }

        [Test]
        public void Streak_ResetsOnDeath()
        {
            _math.Evaluate(CreateLocalKill("BotB_1"), 1.0f, 4, 3);
            _math.Evaluate(CreateLocalKill("BotB_2"), 10.0f, 4, 2);
            _math.Evaluate(CreateLocalKill("BotB_3"), 20.0f, 4, 1);
            Assert.AreEqual(3, _math.LocalStreak);

            // 本地玩家阵亡
            _math.Evaluate(CreateLocalDeath("BotB_4"), 25.0f, 4, 3);
            Assert.AreEqual(0, _math.LocalStreak, "本地玩家阵亡后自身连杀归零");

            // 复活后第 1 杀
            var rAfter = _math.Evaluate(CreateLocalKill("BotB_1"), 35.0f, 4, 3);
            Assert.AreEqual(1, _math.LocalStreak);
            Assert.AreNotEqual(CalloutKind.Streak, rAfter.Kind);
        }

        [Test]
        public void Wipeout_TriggersWhenEntireTeamIsDead()
        {
            // 4 人队伍，前 3 次击杀还有活人
            _math.Evaluate(CreateLocalKill("BotB_1"), 10.0f, 4, 3);
            _math.Evaluate(CreateLocalKill("BotB_2"), 11.0f, 4, 2);
            _math.Evaluate(CreateLocalKill("BotB_3"), 12.0f, 4, 1);

            // 第 4 人死亡，全队 0 人存活 -> WIPEOUT!
            var ev4 = CreateLocalKill("BotB_4");
            var r4 = _math.Evaluate(ev4, 13.0f, 4, 0);

            Assert.AreEqual(CalloutKind.Wipeout, r4.Kind);
            Assert.AreEqual(MatchCalloutMath.TextWipeout, r4.MainText);
            Assert.AreEqual(MatchCalloutMath.SubWipeout, r4.SubText);
            Assert.IsTrue(r4.IsBig, "Wipeout 判定为大播报");
        }

        [Test]
        public void Wipeout_DebounceDuringRespawnWindow()
        {
            // t = 20.0s 触发团灭
            var ev1 = CreateLocalKill("BotB_4");
            var r1 = _math.Evaluate(ev1, 20.0f, 4, 0);
            Assert.AreEqual(CalloutKind.Wipeout, r1.Kind);

            // 4s 复活窗口内（例如 t = 22.0s）发生事件且存活仍为 0，受冷却去抖保护不重复触发
            var ev2 = CreateLocalKill("BotB_Extra");
            var r2 = _math.Evaluate(ev2, 22.0f, 4, 0);
            Assert.AreNotEqual(CalloutKind.Wipeout, r2.Kind, "冷却期内不得重复触发团灭播报");
        }

        [Test]
        public void Wipeout_TriggersAgainAfterCooldown()
        {
            // t = 20.0s 第一次团灭
            _math.Evaluate(CreateLocalKill("BotB_4"), 20.0f, 4, 0);

            // 冷却期后（默认 8s，t = 30.0s），对方全员复活后再次全部阵亡
            var evAgain = CreateLocalKill("BotB_4");
            var rAgain = _math.Evaluate(evAgain, 30.0f, 4, 0);
            Assert.AreEqual(CalloutKind.Wipeout, rAgain.Kind, "冷却结束后新的团灭应能正常触发");
        }

        [Test]
        public void Wipeout_PriorityOverridesMultiSplatAndRevenge()
        {
            // 设置仇人
            _math.Evaluate(CreateLocalDeath("BotB_Target"), 1.0f, 4, 4);

            // 本地玩家在 1s 内双杀仇人并且直接造成团灭
            _math.Evaluate(CreateLocalKill("BotB_Ally"), 10.0f, 4, 1);
            var ev = CreateLocalKill("BotB_Target");
            var result = _math.Evaluate(ev, 10.5f, 4, 0);

            // 同时满足 Wipeout (6)、MultiSplat (5)、Revenge (3)
            Assert.AreEqual(CalloutKind.Wipeout, result.Kind, "Wipeout 优先级应盖过 MultiSplat 和 Revenge");
        }

        [Test]
        public void SameFrameArbitration_SelectHighestPriority()
        {
            var resStreak = new CalloutResult { Kind = CalloutKind.Streak, Priority = (int)CalloutKind.Streak };
            var resMulti = new CalloutResult { Kind = CalloutKind.MultiSplat, Priority = (int)CalloutKind.MultiSplat };
            var resWipe = new CalloutResult { Kind = CalloutKind.Wipeout, Priority = (int)CalloutKind.Wipeout };

            Assert.AreEqual(CalloutKind.MultiSplat, MatchCalloutMath.SelectHighestPriority(resStreak, resMulti).Kind);
            Assert.AreEqual(CalloutKind.Wipeout, MatchCalloutMath.SelectHighestPriority(resMulti, resWipe).Kind);
            Assert.AreEqual(CalloutKind.Wipeout, MatchCalloutMath.SelectHighestPriority(resWipe, resStreak).Kind);
        }

        [Test]
        public void Reset_ClearsAllState()
        {
            _math.Evaluate(CreateLocalKill("BotB_1"), 1.0f, 4, 3);
            _math.Evaluate(CreateLocalDeath("BotB_2"), 5.0f, 4, 4);
            _math.Evaluate(CreateBotKill("BotB_Ace", "BotA_1"), 8.0f, 4, 3);

            _math.Reset();

            Assert.AreEqual(0, _math.LocalStreak);
            Assert.IsFalse(_math.HasPlayedFirstSplat);
            Assert.IsNull(_math.LastKillerName);
            Assert.AreEqual(0, _math.LocalKillCountInWindow);
            Assert.AreEqual(0, _math.GetActorStreak("BotB_Ace"));

            // Reset 后首杀应重新可用
            var r = _math.Evaluate(CreateLocalKill("BotB_1"), 1.0f, 4, 3);
            Assert.AreEqual(CalloutKind.FirstSplat, r.Kind);
        }
    }

    [TestFixture]
    public class MatchCalloutViewLayoutTests
    {
        private GameObject _go;
        private MatchCalloutView _view;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestCalloutView", typeof(RectTransform), typeof(CanvasRenderer), typeof(CanvasGroup));
            _view = _go.AddComponent<MatchCalloutView>();
            _view.EnsureBuilt();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)
            {
                Object.DestroyImmediate(_go);
            }
        }

        [Test]
        public void MultiSplat_IsCompact_DoesNotExceed360Width()
        {
            var callout = new CalloutResult
            {
                Kind = CalloutKind.MultiSplat,
                MainText = "双杀",
                Count = 2,
                Priority = 5,
                IconKey = "mark_splat"
            };

            _view.PlayCallout(callout);
            var rt = (RectTransform)_go.transform;

            Assert.LessOrEqual(rt.sizeDelta.x, 340f, "双杀连杀播报必须紧凑利落，杜绝旧版 620px 空洞长条");
            Assert.GreaterOrEqual(rt.sizeDelta.x, 280f, "双杀连杀播报宽度应能饱满容纳图标、中文大字与徽章");
            Assert.LessOrEqual(rt.sizeDelta.y, 90f, "高度应收拢在 80-84 之间");
        }

        [Test]
        public void SubtitledCallout_IsCompact_DoesNotExceed440Width()
        {
            var callout = new CalloutResult
            {
                Kind = CalloutKind.Shutdown,
                MainText = MatchCalloutMath.TextShutdown,
                SubText = MatchCalloutMath.SubShutdown,
                Priority = 2,
                IconKey = "glyph_bolt"
            };

            _view.PlayCallout(callout);
            var rt = (RectTransform)_go.transform;

            Assert.LessOrEqual(rt.sizeDelta.x, 430f, "带副标题播报必须收敛，杜绝旧版 700px 巨大黑条");
            Assert.GreaterOrEqual(rt.sizeDelta.x, 380f, "带副标题播报宽度需容纳完整说明句");
        }

        [Test]
        public void StreakCallout_PipsAreCenteredAndCompact()
        {
            var callout = new CalloutResult
            {
                Kind = CalloutKind.Streak,
                MainText = "连杀",
                Count = 4,
                Priority = 1,
                IconKey = "glyph_star"
            };

            _view.PlayCallout(callout);

            var pipsTr = _go.transform.Find("StreakPips");
            Assert.IsNotNull(pipsTr, "应存在 StreakPips 容器");
            var pipsRt = (RectTransform)pipsTr;
            Assert.AreEqual(0.5f, pipsRt.anchorMin.x, 0.01f, "连杀方块容器应水平居中");
            Assert.AreEqual(0.5f, pipsRt.anchorMax.x, 0.01f, "连杀方块容器应水平居中");

            var pip0 = pipsTr.Find("Pip0");
            Assert.IsNotNull(pip0);
            var pip0Rt = (RectTransform)pip0;
            Assert.LessOrEqual(pip0Rt.sizeDelta.x, 10f, "方块尺寸应为紧凑 9x9");
        }

        [Test]
        public void StreakCallout_LitPipsAreCenteredAsARun()
        {
            var callout = new CalloutResult
            {
                Kind = CalloutKind.Streak,
                MainText = "连杀",
                Count = 5,
                Priority = 1,
                IconKey = "glyph_star"
            };

            _view.PlayCallout(callout);

            var pipsTr = _go.transform.Find("StreakPips");
            Assert.IsNotNull(pipsTr);

            // 5 连：点亮段应围绕父节点中心对称（-26 / 0 / +26 …），而不是贴在最左 5 格
            var p0 = (RectTransform)pipsTr.Find("Pip0");
            var p2 = (RectTransform)pipsTr.Find("Pip2");
            var p4 = (RectTransform)pipsTr.Find("Pip4");

            Assert.AreEqual(0f, p2.anchoredPosition.x, 0.5f, "5 连的中颗方块应居中");
            Assert.AreEqual(-p4.anchoredPosition.x, p0.anchoredPosition.x, 0.5f, "点亮段应左右对称");
            Assert.Less(p0.anchoredPosition.x, 0f, "首颗应在中心左侧");
        }
    }
}
