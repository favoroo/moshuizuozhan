using NUnit.Framework;
using P1.UI;

namespace P1.Tests
{
    /// <summary>
    /// 首页 / 对局内菜单 Esc 仲裁的纯逻辑用例：
    /// 覆盖「一个 Esc 同时开暂停菜单 + 收放武器抽屉」这个 v0.1.9 首页 bug 的判定基础
    /// （<see cref="MenuInputRouter"/>），以及「同一帧只能有一个菜单动作」的帧消费位。
    /// </summary>
    [TestFixture]
    public class MenuInputRouterTests
    {
        [SetUp]
        public void SetUp()
        {
            MenuInputRouter.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            MenuInputRouter.ResetForTests();
        }

        [Test]
        public void PriorityOrder_MatchesDocumentedLadder()
        {
            Assert.Greater((int)MenuEscLayer.HomeInfo, (int)MenuEscLayer.LayoutEditor,
                "首页详情浮层优先级最高");
            Assert.Greater((int)MenuEscLayer.LayoutEditor, (int)MenuEscLayer.PauseMenu,
                "布局编辑优先于暂停/设置");
            Assert.Greater((int)MenuEscLayer.PauseMenu, (int)MenuEscLayer.WeaponDrawer,
                "暂停/设置优先于武器抽屉");
            Assert.Greater((int)MenuEscLayer.WeaponDrawer, (int)MenuEscLayer.None);
        }

        [Test]
        public void ResolveEscOwner_HomeInfoOpen_WinsOverEverythingElse()
        {
            MenuEscLayer owner = MenuInputRouter.ResolveEscOwner(
                homeInfoOpen: true, layoutEditing: true, pauseExpanded: true, drawerExpanded: true);

            Assert.AreEqual(MenuEscLayer.HomeInfo, owner);
        }

        [Test]
        public void ResolveEscOwner_LayoutEditing_BeatsPauseAndDrawer()
        {
            MenuEscLayer owner = MenuInputRouter.ResolveEscOwner(
                homeInfoOpen: false, layoutEditing: true, pauseExpanded: true, drawerExpanded: true);

            Assert.AreEqual(MenuEscLayer.LayoutEditor, owner);
        }

        [Test]
        public void ResolveEscOwner_PauseExpanded_BeatsDrawer()
        {
            MenuEscLayer owner = MenuInputRouter.ResolveEscOwner(
                homeInfoOpen: false, layoutEditing: false, pauseExpanded: true, drawerExpanded: true);

            Assert.AreEqual(MenuEscLayer.PauseMenu, owner);
            Assert.AreNotEqual(MenuEscLayer.WeaponDrawer, owner, "同一帧绝不能既收抽屉又开暂停");
        }

        [Test]
        public void ResolveEscOwner_OnlyDrawerExpanded_DrawerOwnsEsc()
        {
            MenuEscLayer owner = MenuInputRouter.ResolveEscOwner(
                homeInfoOpen: false, layoutEditing: false, pauseExpanded: false, drawerExpanded: true);

            Assert.AreEqual(MenuEscLayer.WeaponDrawer, owner);
        }

        [Test]
        public void ResolveEscOwner_NothingExpanded_PauseMenuOwnsEsc()
        {
            // 桌面端暂停菜单只有 Esc 一个入口，因此「什么都没展开」时 Esc 归暂停菜单；
            // 抽屉只在已展开时才收 Esc（不会在同一帧抢两次）。
            MenuEscLayer owner = MenuInputRouter.ResolveEscOwner(
                homeInfoOpen: false, layoutEditing: false, pauseExpanded: false, drawerExpanded: false);

            Assert.AreEqual(MenuEscLayer.PauseMenu, owner);
        }

        [Test]
        public void TryConsume_SameFrame_OnlyFirstCallerWins()
        {
            const int frame = 101;

            Assert.IsTrue(MenuInputRouter.TryConsume(frame, MenuEscLayer.PauseMenu),
                "第一个消费者拿到本帧 Esc");
            Assert.IsFalse(MenuInputRouter.TryConsume(frame, MenuEscLayer.WeaponDrawer),
                "同一帧第二个消费者必须拿不到（否则就是首页双动作 bug）");
            Assert.IsFalse(MenuInputRouter.TryConsume(frame, MenuEscLayer.HomeInfo));
            Assert.IsTrue(MenuInputRouter.IsConsumed(frame));
        }

        [Test]
        public void TryConsume_NextFrame_IsFreeAgain()
        {
            Assert.IsTrue(MenuInputRouter.TryConsume(77, MenuEscLayer.PauseMenu));
            Assert.IsFalse(MenuInputRouter.IsConsumed(78), "消费位只对帧号相同的那一帧有效");
            Assert.IsTrue(MenuInputRouter.TryConsume(78, MenuEscLayer.WeaponDrawer));
        }

        [Test]
        public void CanLayerHandle_NonOwnerLayer_MustNotTouchFrameBit()
        {
            // 回归护栏：暂停菜单在归属为「已展开的抽屉」时若先 consume 再 return，
            // 抽屉随后的 TryConsumeEsc 会失败 —— 暂停菜单先 Update 时一个 Esc 在抽屉上完全没反应。
            Assert.IsFalse(MenuInputRouter.CanLayerHandle(MenuEscLayer.PauseMenu, MenuEscLayer.WeaponDrawer),
                "非归属层不得处理、也不得占用帧位");
            Assert.IsFalse(MenuInputRouter.CanLayerHandle(MenuEscLayer.WeaponDrawer, MenuEscLayer.PauseMenu));
            Assert.IsTrue(MenuInputRouter.CanLayerHandle(MenuEscLayer.PauseMenu, MenuEscLayer.PauseMenu));
            Assert.IsTrue(MenuInputRouter.CanLayerHandle(MenuEscLayer.WeaponDrawer, MenuEscLayer.WeaponDrawer));

            // 浮层 / 布局编辑归属：谁先跑谁代劳（动作与归属者完全一致），因此两层都能处理
            Assert.IsTrue(MenuInputRouter.CanLayerHandle(MenuEscLayer.PauseMenu, MenuEscLayer.HomeInfo));
            Assert.IsTrue(MenuInputRouter.CanLayerHandle(MenuEscLayer.WeaponDrawer, MenuEscLayer.HomeInfo));
            Assert.IsTrue(MenuInputRouter.CanLayerHandle(MenuEscLayer.PauseMenu, MenuEscLayer.LayoutEditor));
            Assert.IsTrue(MenuInputRouter.CanLayerHandle(MenuEscLayer.WeaponDrawer, MenuEscLayer.LayoutEditor));
        }

        [TestCase(MenuEscLayer.WeaponDrawer)]
        [TestCase(MenuEscLayer.PauseMenu)]
        [TestCase(MenuEscLayer.HomeInfo)]
        [TestCase(MenuEscLayer.LayoutEditor)]
        public void EscHandling_IsOrderIndependent_ExactlyOneActorPerFrame(MenuEscLayer owner)
        {
            // 两个消费者（暂停菜单 / 武器抽屉）按 CanLayerHandle → TryConsume 的顺序竞争，
            // 直接枚举两种 Update 顺序，断言「恰好一个执行者」，即顺序无关。
            var orders = new[]
            {
                new[] { MenuEscLayer.PauseMenu, MenuEscLayer.WeaponDrawer },
                new[] { MenuEscLayer.WeaponDrawer, MenuEscLayer.PauseMenu }
            };

            for (int o = 0; o < orders.Length; o++)
            {
                int frame = 500 + o;
                MenuInputRouter.ResetForTests();

                int actors = 0;
                MenuEscLayer actorLayer = MenuEscLayer.None;

                for (int i = 0; i < orders[o].Length; i++)
                {
                    MenuEscLayer layer = orders[o][i];
                    if (!MenuInputRouter.CanLayerHandle(layer, owner)) continue;
                    if (!MenuInputRouter.TryConsume(frame, layer)) continue;

                    actors++;
                    actorLayer = layer;
                }

                Assert.AreEqual(1, actors,
                    $"owner={owner} 顺序={orders[o][0]}→{orders[o][1]}：同一帧必须恰好一个执行者");
                Assert.AreNotEqual(MenuEscAction.None, MenuInputRouter.ActionForOwner(owner),
                    "归属层必须有唯一动作（动作由归属决定，与谁先 Update 无关）");
                Assert.IsTrue(MenuInputRouter.CanLayerHandle(actorLayer, owner),
                    "执行者必须是归属者本人或允许代劳的层");
            }
        }

        [Test]
        public void ActionForOwner_MatchesOwnership()
        {
            Assert.AreEqual(MenuEscAction.CloseHomeInfo, MenuInputRouter.ActionForOwner(MenuEscLayer.HomeInfo));
            Assert.AreEqual(MenuEscAction.CloseLayoutEditor, MenuInputRouter.ActionForOwner(MenuEscLayer.LayoutEditor));
            Assert.AreEqual(MenuEscAction.TogglePause, MenuInputRouter.ActionForOwner(MenuEscLayer.PauseMenu));
            Assert.AreEqual(MenuEscAction.CloseDrawer, MenuInputRouter.ActionForOwner(MenuEscLayer.WeaponDrawer));
            Assert.AreEqual(MenuEscAction.TogglePause, MenuInputRouter.ActionForOwner(MenuEscLayer.None),
                "什么都没展开时 Esc = 暂停菜单");
        }
    }
}
