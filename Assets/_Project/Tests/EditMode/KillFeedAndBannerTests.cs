using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using P1.Weapons;
using UnityEngine;

namespace P1.Tests
{
    [TestFixture]
    public class KillFeedAndBannerTests
    {
        private GameObject _rootGo;

        [SetUp]
        public void SetUp()
        {
            _rootGo = new GameObject("TestRoot");
        }

        [TearDown]
        public void TearDown()
        {
            if (_rootGo != null)
            {
                Object.DestroyImmediate(_rootGo);
                _rootGo = null;
            }
        }

        [Test]
        public void KillFeedEventHub_ReportKill_TriggersListener()
        {
            KillFeedEvent received = default;
            bool called = false;

            System.Action<KillFeedEvent> listener = ev =>
            {
                received = ev;
                called = true;
            };

            KillFeedEventHub.OnKillReported += listener;
            try
            {
                KillFeedEventHub.TriggerMockKill(Team.TeamA, Team.TeamB, WeaponKind.Roller, "TestKiller", "TestVictim");

                Assert.IsTrue(called, "Listener should be invoked on ReportKill");
                Assert.AreEqual("TestKiller", received.KillerName);
                Assert.AreEqual("TestVictim", received.VictimName);
                Assert.AreEqual(Team.TeamA, received.KillerTeam);
                Assert.AreEqual(Team.TeamB, received.VictimTeam);
                Assert.AreEqual(WeaponKind.Roller, received.WeaponKind);
            }
            finally
            {
                KillFeedEventHub.OnKillReported -= listener;
            }
        }

        [Test]
        public void KillFeedView_PushKill_CreatesAndTracksActiveCard()
        {
            var kfGo = new GameObject("KillFeedView", typeof(RectTransform), typeof(KillFeedView));
            kfGo.transform.SetParent(_rootGo.transform, false);
            var feed = kfGo.GetComponent<KillFeedView>();
            feed.EnsureBuilt();

            Assert.AreEqual(0, feed.ActiveCards.Count);

            var ev = new KillFeedEvent
            {
                KillerName = "OCTO",
                KillerTeam = Team.TeamB,
                VictimName = "YOU",
                VictimTeam = Team.TeamA,
                WeaponKind = WeaponKind.Shooter,
                IsLocalPlayerVictim = true
            };

            var card = feed.PushKill(ev);

            Assert.IsNotNull(card);
            Assert.AreEqual(1, feed.ActiveCards.Count);
            Assert.IsTrue(card.IsActive);
        }

        [Test]
        public void MatchBannerView_FlowTransitions_UpdatesCurrentType()
        {
            var bannerGo = new GameObject("MatchBannerView", typeof(RectTransform), typeof(MatchBannerView));
            bannerGo.transform.SetParent(_rootGo.transform, false);
            var banner = bannerGo.GetComponent<MatchBannerView>();
            banner.EnsureBuilt();

            Assert.AreEqual(MatchBannerType.None, banner.CurrentType);

            banner.ShowCountdown(3);
            Assert.AreEqual(MatchBannerType.Countdown, banner.CurrentType);
            Assert.IsTrue(banner.IsActive);

            banner.ShowGo();
            Assert.AreEqual(MatchBannerType.Go, banner.CurrentType);

            banner.ShowOneMinute();
            Assert.AreEqual(MatchBannerType.OneMinute, banner.CurrentType);

            banner.ShowFinish();
            Assert.AreEqual(MatchBannerType.Finish, banner.CurrentType);

            banner.HideImmediate();
            Assert.AreEqual(MatchBannerType.None, banner.CurrentType);
            Assert.IsFalse(banner.IsActive);
        }

        [Test]
        public void KillFeedView_MaxCardsExceeded_DismissesOldest()
        {
            var kfGo = new GameObject("KillFeedView", typeof(RectTransform), typeof(KillFeedView));
            kfGo.transform.SetParent(_rootGo.transform, false);
            var feed = kfGo.GetComponent<KillFeedView>();
            feed.EnsureBuilt();

            for (int i = 0; i < 5; i++)
            {
                var ev = new KillFeedEvent
                {
                    KillerName = $"Killer_{i}",
                    KillerTeam = Team.TeamA,
                    VictimName = $"Victim_{i}",
                    VictimTeam = Team.TeamB,
                    WeaponKind = WeaponKind.Shooter
                };
                feed.PushKill(ev);
            }

            // 最多保留 4 个活跃
            Assert.LessOrEqual(feed.ActiveCards.Count, 4);
        }

        [Test]
        public void MatchFlowUI_EnsureBannerView_CreatesBannerIfMissing()
        {
            var flowGo = new GameObject("MatchFlowUI", typeof(RectTransform), typeof(MatchFlowUI));
            flowGo.transform.SetParent(_rootGo.transform, false);
            var flow = flowGo.GetComponent<MatchFlowUI>();

            Assert.IsNotNull(flow.BannerView);
            Assert.AreEqual(MatchBannerType.None, flow.BannerView.CurrentType);
        }

        /// <summary>
        /// 回归：击杀卡第二行「标签 + 受害者名」不得重叠。
        /// 旧版标签框锁死 106、受害者从 196 起；移动端 30 号 "CRUSHED!" 实测 181 宽，
        /// 会压到受害者名上。现在按 preferredWidth 动态排布，两者之间必须留出间距。
        /// </summary>
        [Test]
        public void KillCard_ActionTagAndVictimName_DoNotOverlap()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(RectTransform));
            canvasGo.transform.SetParent(_rootGo.transform, false);

            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(canvasGo.transform, false);
            ((RectTransform)cardGo.transform).sizeDelta = new Vector2(400f, 88f);

            var card = cardGo.AddComponent<KillCardView>();
            card.EnsureBuilt();
            card.Bind(new KillFeedEvent
            {
                KillerName = "ALLY 03",
                KillerTeam = Team.TeamA,
                VictimName = "ENEMY 03",
                VictimTeam = Team.TeamB,
                WeaponKind = WeaponKind.Shooter,
                IsLocalPlayerKiller = true,
                Timestamp = 1f
            }, null, null, null);

            Canvas.ForceUpdateCanvases();

            var tag = cardGo.transform.Find("ActionTag").GetComponent<TMPro.TMP_Text>();
            var victim = cardGo.transform.Find("VictimText").GetComponent<TMPro.TMP_Text>();
            Assert.IsNotNull(tag);
            Assert.IsNotNull(victim);

            float tagRight = tag.rectTransform.anchoredPosition.x + tag.rectTransform.rect.width;
            Assert.LessOrEqual(tagRight + 0.5f, victim.rectTransform.anchoredPosition.x,
                "标签右缘必须停在受害者名起点之前（10px 间距）");
            Assert.LessOrEqual(tag.preferredWidth, tag.rectTransform.rect.width + 0.5f,
                "标签框必须容纳实测文字宽度，不能靠溢出画到别人身上");
            Assert.LessOrEqual(victim.preferredWidth, victim.rectTransform.rect.width + 0.5f,
                "受害者名框必须容纳常见名字（超长才允许 Truncate）");

            Object.DestroyImmediate(canvasGo);
        }

        /// <summary>
        /// 回归：最后 1 分钟横幅的文字必须完整落在红底面板内，且与计时球留出呼吸位。
        /// 旧版 380×60 面板塞 407×74 的文字（移动端更严重），并固定 -190 在移动端压住计时球。
        /// </summary>
        [Test]
        public void CenterTimer_FinalMinuteBanner_TextFitsInsidePanel_AndClearsTimerBlob()
        {
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(RectTransform));
            canvasGo.transform.SetParent(_rootGo.transform, false);

            var timerGo = new GameObject("Timer", typeof(RectTransform));
            timerGo.transform.SetParent(canvasGo.transform, false);
            var timer = timerGo.AddComponent<CenterTimerView>();
            timer.EnsureTimerBuilt(true);
            timer.SetTime(61f);
            timer.SetTime(59f);

            Canvas.ForceUpdateCanvases();

            var banner = timerGo.transform.Find("FinalMinuteBanner") as RectTransform;
            var blob = timerGo.transform.Find("TimerBlob") as RectTransform;
            Assert.IsNotNull(banner, "最后 1 分钟横幅应已构建");
            Assert.IsNotNull(blob, "计时球应已构建");
            Assert.IsTrue(banner.gameObject.activeSelf, "跨过 60s 后横幅应显示");

            var text = banner.Find("BannerText").GetComponent<TMPro.TMP_Text>();
            Assert.IsNotNull(text);
            Assert.LessOrEqual(text.preferredWidth, text.rectTransform.rect.width + 0.5f,
                "横幅文字宽度必须落在内边距内");
            Assert.LessOrEqual(text.preferredHeight, text.rectTransform.rect.height + 0.5f,
                "横幅文字高度必须落在面板内");

            var bannerCorners = new Vector3[4];
            var blobCorners = new Vector3[4];
            banner.GetWorldCorners(bannerCorners);
            blob.GetWorldCorners(blobCorners);
            Assert.GreaterOrEqual(blobCorners[0].y - bannerCorners[2].y, 12f,
                "横幅顶边与计时球底边至少留 12px（Hero 硬影 7px 也不得压住）");

            Object.DestroyImmediate(canvasGo);
        }

        [Test]
        public void MinimapView_EnsureBuilt_InstantiatesMarkers()
        {
            var mapGo = new GameObject("MinimapView", typeof(RectTransform), typeof(MinimapView));
            mapGo.transform.SetParent(_rootGo.transform, false);
            var map = mapGo.GetComponent<MinimapView>();

            map.EnsureBuilt();

            Assert.IsNotNull(map.PlayerMarker);
            Assert.AreEqual(3, map.AllyMarkerCount, "Should pre-instantiate 3 ally markers");
            Assert.AreEqual(4, map.EnemyMarkerCount, "Should pre-instantiate 4 enemy markers");
        }
    }
}
