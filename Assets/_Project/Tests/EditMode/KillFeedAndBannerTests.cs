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
