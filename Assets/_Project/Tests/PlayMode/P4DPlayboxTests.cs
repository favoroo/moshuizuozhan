using System.Collections;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using P1.Weapons;
using UnityEngine;
using UnityEngine.TestTools;

namespace P1.Tests
{
    [TestFixture]
    [Category("UI")]
    public class P4DPlayboxTests
    {
        private GameObject _canvasGo;
        private HudController _hud;
        private MatchBannerView _banner;
        private KillFeedView _killFeed;
        private MinimapView _minimap;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _canvasGo = new GameObject("TestHUDCanvas", typeof(Canvas), typeof(CanvasGroup));
            var canvas = _canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            _hud = _canvasGo.AddComponent<HudController>();

            // 等待一帧让 HudController Awake / Start 自动装配
            yield return null;

            _killFeed = _hud.KillFeed;
            _minimap = _hud.Minimap;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_canvasGo != null)
            {
                Object.Destroy(_canvasGo);
                _canvasGo = null;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator HudController_EnsureMinimapAndKillFeed_AutoInstantiated()
        {
            Assert.IsNotNull(_killFeed, "KillFeedView should be automatically built by HudController");
            Assert.IsNotNull(_minimap, "MinimapView should be automatically built by HudController");
            yield return null;
        }

        [UnityTest]
        public IEnumerator KillFeedView_ReceivesKillEvent_SpawnsAnimatedCard()
        {
            Assert.IsNotNull(_killFeed);

            KillFeedEventHub.TriggerMockKill(Team.TeamA, Team.TeamB, WeaponKind.Roller, "SPLAT_MASTER", "OCTO_BOT");

            yield return null;

            Assert.AreEqual(1, _killFeed.ActiveCards.Count);
            var card = _killFeed.ActiveCards[0];
            Assert.IsTrue(card.gameObject.activeSelf);

            // 验证经过几帧动画后仍稳定存在
            for (int i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(card.IsActive);
        }

        [UnityTest]
        public IEnumerator MatchBannerView_ShowGoAndFinish_CyclesSmoothly()
        {
            var bannerGo = new GameObject("TestBanner", typeof(RectTransform), typeof(MatchBannerView));
            bannerGo.transform.SetParent(_canvasGo.transform, false);
            _banner = bannerGo.GetComponent<MatchBannerView>();
            _banner.EnsureBuilt();

            _banner.ShowGo();
            yield return null;

            Assert.AreEqual(MatchBannerType.Go, _banner.CurrentType);
            Assert.IsTrue(_banner.IsActive);

            _banner.ShowFinish();
            yield return null;

            Assert.AreEqual(MatchBannerType.Finish, _banner.CurrentType);
            Assert.IsTrue(_banner.IsActive);

            _banner.HideImmediate();
            yield return null;

            Assert.AreEqual(MatchBannerType.None, _banner.CurrentType);
            Assert.IsFalse(_banner.IsActive);
        }
    }
}
