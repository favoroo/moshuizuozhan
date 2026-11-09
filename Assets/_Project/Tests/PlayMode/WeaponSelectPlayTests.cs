using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using P1.Core;
using P1.Player;
using P1.UI;
using P1.UI.WeaponSelect;
using P1.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace P1.Tests
{
    /// <summary>
    /// [P8] 战前武器选择与换装运行时测试（PlayMode）：
    /// 1. Ready→Countdown 时本地玩家装备被准确替换；
    /// 2. Bot 的武器不受玩家换装影响（Bot 自己的武器由 BotLoadoutService 只在每局
    ///    Countdown 抽一次卡，玩家中途换枪绝不能连带改动 Bot；见用例 2 的新语义）；
    /// 3. 非法索引（负数/超范围）安全回落为合法武器（默认 Shooter）。
    /// </summary>
    [Category("UI")]
    public class WeaponSelectPlayTests
    {
        private readonly List<GameObject> _createdObjects = new List<GameObject>();
        private readonly List<WeaponConfigSO> _createdConfigs = new List<WeaponConfigSO>();
        private int _originalPrefsIndex;

        [SetUp]
        public void SetUp()
        {
            _originalPrefsIndex = PlayerPrefs.GetInt(WeaponSelectMath.PrefsKeySelectedWeaponIndex, 0);
        }

        [TearDown]
        public void TearDown()
        {
            PlayerPrefs.SetInt(WeaponSelectMath.PrefsKeySelectedWeaponIndex, _originalPrefsIndex);
            PlayerPrefs.Save();

            for (int i = 0; i < _createdObjects.Count; i++)
            {
                if (_createdObjects[i] != null)
                {
                    var health = _createdObjects[i].GetComponent<CombatantHealth>();
                    if (health != null)
                    {
                        CombatantHealthRegistry.Unregister(health);
                    }
                    Object.Destroy(_createdObjects[i]);
                }
            }
            _createdObjects.Clear();

            for (int i = 0; i < _createdConfigs.Count; i++)
            {
                if (_createdConfigs[i] != null)
                {
                    Object.Destroy(_createdConfigs[i]);
                }
            }
            _createdConfigs.Clear();
        }

        private WeaponConfigSO CreateConfig(WeaponKind kind, string name)
        {
            var cfg = ScriptableObject.CreateInstance<WeaponConfigSO>();
            cfg.weaponKind = kind;
            cfg.displayName = name;
            _createdConfigs.Add(cfg);
            return cfg;
        }

        private GameObject CreateCombatantActor(string name, Team team, WeaponConfigSO config, bool isLocalPlayer)
        {
            var go = new GameObject(name, typeof(CharacterController), typeof(InkCharacterMotor), typeof(WeaponController), typeof(CombatantHealth));
            _createdObjects.Add(go);

            var health = go.GetComponent<CombatantHealth>();
            health.Configure(team, null);
            CombatantHealthRegistry.Register(health);

            var weapon = go.GetComponent<WeaponController>();
            weapon.EquipWeapon(config);

            if (isLocalPlayer)
            {
                go.AddComponent<PlayerController>();
            }

            return go;
        }

        [UnityTest]
        public IEnumerator LocalPlayerWeaponReplaced_WhenCountdownStarts()
        {
            var shooterCfg = CreateConfig(WeaponKind.Shooter, "Spritzer");
            var chargerCfg = CreateConfig(WeaponKind.Charger, "Glint Charger");

            // 1. 创建玩家角色，初始配备 Shooter
            GameObject playerGo = CreateCombatantActor("TestPlayer", Team.TeamA, shooterCfg, isLocalPlayer: true);
            yield return null;

            Assert.IsNotNull(PlayerController.LocalPlayer, "LocalPlayer 应该已被正确注册");
            Assert.AreEqual(WeaponKind.Shooter, PlayerController.LocalPlayer.Weapon.Config.weaponKind);

            // 2. 创建 Bootstrap 实例并模拟换装应用
            var bootGo = new GameObject("TestBootstrap", typeof(WeaponSelectBootstrap));
            _createdObjects.Add(bootGo);
            var boot = bootGo.GetComponent<WeaponSelectBootstrap>();

            boot.ApplyWeaponToLocalPlayer(chargerCfg);
            yield return null;

            // 3. 验证玩家武器成功被替换为 Charger
            Assert.AreEqual(WeaponKind.Charger, PlayerController.LocalPlayer.Weapon.Config.weaponKind, "玩家武器应已被替换为 Glint Charger");
        }

        [UnityTest]
        public IEnumerator BotWeaponRemainsUntouched_WhenPlayerWeaponChanges()
        {
            var shooterCfg = CreateConfig(WeaponKind.Shooter, "Spritzer");
            var rollerCfg = CreateConfig(WeaponKind.Roller, "Swell Roller");
            var dualiesCfg = CreateConfig(WeaponKind.Dualies, "Twinfin Dualies");

            // 1. 创建玩家（初始 Shooter）与 Bot（初始 Roller）
            GameObject playerGo = CreateCombatantActor("Player", Team.TeamA, shooterCfg, isLocalPlayer: true);
            GameObject botGo = CreateCombatantActor("BotB_01", Team.TeamB, rollerCfg, isLocalPlayer: false);
            yield return null;

            var botWeapon = botGo.GetComponent<WeaponController>();
            Assert.AreEqual(WeaponKind.Roller, botWeapon.Config.weaponKind);

            // 2. 玩家换装为 Dualies
            var bootGo = new GameObject("TestBootstrap", typeof(WeaponSelectBootstrap));
            _createdObjects.Add(bootGo);
            var boot = bootGo.GetComponent<WeaponSelectBootstrap>();

            boot.ApplyWeaponToLocalPlayer(dualiesCfg);
            yield return null;

            // 3. 验证玩家成功换成 Dualies，而 Bot 的武器依然是 Roller，完全不受影响
            Assert.AreEqual(WeaponKind.Dualies, PlayerController.LocalPlayer.Weapon.Config.weaponKind);
            Assert.AreEqual(WeaponKind.Roller, botWeapon.Config.weaponKind, "Bot 的武器绝对不应受玩家换装影响");
        }

        [Test]
        public void InvalidIndex_FallsBackToShooterSafely()
        {
            // 负数越界
            int clampedNegative = WeaponSelectMath.ClampIndex(-99);
            Assert.AreEqual(0, clampedNegative);
            Assert.AreEqual(WeaponKind.Shooter, WeaponSelectMath.IndexToKind(-99));

            // 正数越界
            int clampedPositive = WeaponSelectMath.ClampIndex(999);
            Assert.AreEqual(6, clampedPositive);
            Assert.AreEqual(WeaponKind.Splatling, WeaponSelectMath.IndexToKind(999));
        }

        [UnityTest]
        [Category("UI")]
        public IEnumerator WeaponSelectView_BackdropAndHeaderLayout_WorkCorrectly()
        {
            yield return null;
            var view = WeaponSelectView.Instance;
            Assert.IsNotNull(view, "场景中应已自动就绪 WeaponSelectView.Instance");
            var viewGo = view.gameObject;

            // 1. 验证标题排版与文字规范
            var titleTrans = viewGo.transform.Find("SafeAreaContainer/DrawerPanel/Header/TitleBubble/Title");
            Assert.IsNotNull(titleTrans, "应存在 TitleBubble/Title 节点");
            var titleText = titleTrans.GetComponent<TextMeshProUGUI>();
            Assert.IsNotNull(titleText, "应存在标题文本");
            StringAssert.Contains("武器整备", titleText.text, "标题应包含中文武器整备");
            StringAssert.Contains("ARSENAL", titleText.text, "标题应包含英文 ARSENAL");
            Assert.AreEqual(TextWrappingModes.NoWrap, titleText.textWrappingMode, "标题必须禁止折行以防挤压重叠");

            // 2. 初始抽屉默认收起，Backdrop 应处于非激活状态
            var backdrop = viewGo.transform.Find("DrawerBackdrop");
            Assert.IsNotNull(backdrop, "抽屉外部 Backdrop 应已创建");
            Assert.IsFalse(backdrop.gameObject.activeSelf, "抽屉收起时 Backdrop 应隐藏");

            // 3. 展开抽屉时 Backdrop 激活
            view.SetDrawerOpen(true);
            yield return null;
            Assert.IsTrue(view.IsDrawerOpen, "抽屉应处于打开状态");
            Assert.IsTrue(backdrop.gameObject.activeSelf, "抽屉打开时 Backdrop 应激活");

            // 4. 点击 Backdrop 时抽屉收回
            var backdropBtn = backdrop.GetComponent<Button>();
            Assert.IsNotNull(backdropBtn, "Backdrop 上应挂载 Button 捕获外部点击");
            backdropBtn.onClick.Invoke();
            yield return null;
            Assert.IsFalse(view.IsDrawerOpen, "点击 Backdrop 后抽屉必须自动收回");
            Assert.IsFalse(backdrop.gameObject.activeSelf, "抽屉收回后 Backdrop 应隐藏");
        }

        [UnityTest]
        [Category("UI")]
        public IEnumerator WeaponCardView_AccentBand_NeverOverflowsRoundedCorner()
        {
            // 不依赖场景里的 WeaponSelectView（单条过滤运行时会话里没有它），直接装配一张卡：
            // 这里要验的是卡片的身份色带几何，常态与选中态都必须内缩到圆角直线段内。
            var cardGo = new GameObject("AccentBandTestCard", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(UIInkShapeGraphic), typeof(Button), typeof(UISpringMotion), typeof(WeaponCardView));
            _createdObjects.Add(cardGo);
            var card = cardGo.GetComponent<WeaponCardView>();
            card.Initialize(0, WeaponKind.Shooter, CreateConfig(WeaponKind.Shooter, "测试喷枪"), null, null, Team.TeamA, null);

            yield return null;

            // 1. 常态：3px 墨框 + 5px 色带。
            AssertAccentBandInsideRoundedCard(card, "常态");

            // 2. 选中态会重算边框与色带高度（3px/5px → 4px/8px），内缩量必须同步重算。
            card.SetSelected(true, animate: false);
            yield return null;
            AssertAccentBandInsideRoundedCard(card, "选中态");

            // 3. 取消选中必须回到常态尺寸，而不是停留在选中态的加宽值。
            card.SetSelected(false, animate: false);
            yield return null;
            AssertAccentBandInsideRoundedCard(card, "取消选中后");

            Object.Destroy(cardGo);
        }

        /// <summary>
        /// 断言单张卡的顶部身份色带完全落在卡片圆角的直线段内：
        /// 横向内缩量必须 ≥ 圆角半径 + 当前墨框宽度（口径同 Docs/UI-GeometricBold.md「卡片身份色带」）。
        /// </summary>
        private static void AssertAccentBandInsideRoundedCard(WeaponCardView card, string phase)
        {
            var cardRect = (RectTransform)card.transform;
            var band = cardRect.Find("AccentBand") as RectTransform;
            Assert.IsNotNull(band, $"{phase}：每张武器卡都应有顶部身份色带");

            float radius = ComicStyle.CornerFor(cardRect.rect.width, cardRect.rect.height);
            float border = card.IsSelected ? WeaponCardView.CardBorderWidthSelected : WeaponCardView.CardBorderWidth;
            float bandWidth = cardRect.rect.width + band.sizeDelta.x;
            float inset = (cardRect.rect.width - bandWidth) * 0.5f;

            Assert.GreaterOrEqual(inset + 0.01f, radius + border,
                $"{phase}：{card.name} 的身份色带必须内缩到圆角直线段内（实际 {inset:0.#}px，要求 ≥ {radius + border:0.#}px），否则横线会溢出卡片轮廓");
            Assert.AreEqual(-border, band.anchoredPosition.y, 0.01f,
                $"{phase}：{card.name} 的身份色带应紧贴墨框内侧");
        }

        [UnityTest]
        [Category("UI")]
        public IEnumerator WeaponSelectView_LoadoutCard_ReplacesFlatTab_AndShowsCurrentWeapon()
        {
            yield return null;
            var view = WeaponSelectView.Instance;
            Assert.IsNotNull(view, "场景中应已自动就绪 WeaponSelectView.Instance");

            var toggle = view.transform.Find("SafeAreaContainer/DrawerToggleButton");
            Assert.IsNotNull(toggle, "抽屉收起时应有嵌入主卡的「武器整备」整备卡");
            var toggleRect = (RectTransform)toggle;
            Assert.AreEqual(new Vector2(0f, 1f), toggleRect.anchorMin, "整备卡应左上锚定以嵌入主卡");
            Assert.AreEqual(new Vector2(0f, 1f), toggleRect.anchorMax, "整备卡应左上锚定以嵌入主卡");
            Assert.AreEqual(MenuLayoutMath.LoadoutCard(view.IsMobileLayout).Size.y, toggleRect.sizeDelta.y, 0.5f,
                "整备卡高度应与版面表一致");
            Assert.AreEqual(view.DrawerWidth, MenuLayoutMath.Drawer(view.IsMobileLayout).Width, 0.5f,
                "抽屉宽度应与版面表一致");

            // 图标是正圆色盘，而不是「直愣愣的方块」
            var plate = toggle.Find("LoadoutIconPlate");
            Assert.IsNotNull(plate, "整备卡应有武器圆形色盘");
            var plateShape = plate.GetComponent<UIInkShapeGraphic>();
            Assert.IsNotNull(plateShape);
            Assert.AreEqual(UIInkShape.Blob, plateShape.Shape, "武器图标底色必须是正圆（Blob）");

            var title = toggle.Find("Text")?.GetComponent<TextMeshProUGUI>();
            Assert.IsNotNull(title);
            StringAssert.Contains("武器整备", title.text);

            var weaponName = toggle.Find("WeaponName")?.GetComponent<TextMeshProUGUI>();
            Assert.IsNotNull(weaponName);

            view.SelectWeapon(2, silent: true);
            yield return null;
            StringAssert.Contains(WeaponSelectMath.GetWeaponChineseName(WeaponKind.Charger), weaponName.text,
                "整备卡应显示当前选中的武器名");

            // 抽屉打开时整备卡隐藏，收起后回来
            view.SetDrawerOpen(true);
            yield return null;
            Assert.IsFalse(toggle.gameObject.activeSelf, "抽屉展开时整备卡应隐藏");
            view.SetDrawerOpen(false);
            yield return null;
            Assert.IsTrue(toggle.gameObject.activeSelf, "抽屉收起后整备卡应重新出现");

            view.SelectWeapon(0, silent: true);
            yield return null;

            // 确认整备卡严格处于 HeroTag 之下，绝不发生叠字与边框重叠
            var flow = Object.FindAnyObjectByType<MatchFlowUI>();
            var heroTag = flow?.transform.Find("ReadyPanel/MenuSafeArea/HomeHeroCard/HeroTag") as RectTransform;
            if (heroTag != null)
            {
                Vector3[] tagCorners = new Vector3[4];
                Vector3[] toggleCorners = new Vector3[4];
                heroTag.GetWorldCorners(tagCorners);
                toggleRect.GetWorldCorners(toggleCorners);
                Assert.Greater(tagCorners[0].y, toggleCorners[1].y,
                    "整备卡顶部必须在 HeroTag 底部下方（不得重叠），世界坐标: HeroTag 底 " + tagCorners[0].y + " vs 整备卡 顶 " + toggleCorners[1].y);
            }
        }

        [UnityTest]
        [Category("UI")]
        public IEnumerator MatchFlowUI_ReadyHome_PutsContentInSafeAreaWithLargeCta()
        {
            yield return null;
            var flow = Object.FindAnyObjectByType<MatchFlowUI>();
            Assert.IsNotNull(flow, "场景中应有 MatchFlowUI");

            var ready = flow.transform.Find("ReadyPanel");
            Assert.IsNotNull(ready);
            var safe = ready.Find("MenuSafeArea");
            Assert.IsNotNull(safe, "首页内容必须放进 SafeArea 容器（刘海/圆角不吃字）");

            var start = safe.Find("StartButton");
            Assert.IsNotNull(start, "开始按钮应在安全区内");
            var startRect = (RectTransform)start;
            // 波次3a：首页改 t-1「左侧 Hero 大卡 + 底部 CTA」，开始按钮锚定 Hero 卡左下（原为屏幕右下拇指区）。
            Assert.AreEqual(new Vector2(0f, 1f), startRect.anchorMin, "开始按钮应锚定左下（Hero 卡内）");
            Assert.AreEqual(new Vector2(0f, 1f), startRect.anchorMax, "开始按钮应锚定左下（Hero 卡内）");

            var home = MenuLayoutMath.Home(false);
            Assert.AreEqual(home.CtaSize.y, startRect.sizeDelta.y, 0.5f, "开始按钮高度应来自版面表");
            Assert.GreaterOrEqual(startRect.sizeDelta.y, 140f, "开赛按钮必须是超大触控目标");

            // Hero 大卡与 2×3 卡阵必须存在（t-1 深色舞台版式）
            var hero = safe.Find("HomeHeroCard");
            Assert.IsNotNull(hero, "首页必须有左侧 Hero 大卡");
            Assert.IsNotNull(safe.Find("HomeTile_RECORD"), "首页必须有战绩卡");
            Assert.IsNotNull(safe.Find("HomeTile_LOOK"), "首页必须有衣柜卡");
            Assert.IsNotNull(safe.Find("HomeTile_ACHIEVE"), "首页必须有成就卡");
            Assert.IsNotNull(safe.Find("HomeTile_MAP"), "首页必须有地图卡");
            Assert.IsNotNull(safe.Find("HomeTile_SETTINGS"), "首页必须有系统设置卡");
            Assert.IsNotNull(safe.Find("HomeTile_LAYOUT"), "首页必须有修改按钮位置卡");

            // CTA 宽度取版面表设计值，但在非 16:9 的 Game View 里会被 Hero 卡内宽夹住——
            // 用实际 Hero 卡宽反推期望值，断言不受测试分辨率影响。
            var heroRect = (RectTransform)hero;
            float expectedCtaWidth = Mathf.Min(home.CtaSize.x, heroRect.sizeDelta.x - home.HeroPadding.x * 2f);
            Assert.AreEqual(expectedCtaWidth, startRect.sizeDelta.x, 0.5f, "开始按钮宽度应来自版面表（Hero 卡内宽内）");
            Assert.LessOrEqual(startRect.sizeDelta.x, heroRect.sizeDelta.x, "CTA 不得超出 Hero 卡");
            Assert.AreEqual(heroRect.anchoredPosition.x + home.HeroPadding.x, startRect.anchoredPosition.x, 0.5f,
                "CTA 左缘必须跟随 Hero 卡实际左缘（宽屏 Hero 居中后仍贴合内边距）");

            // Hero 与右侧 2×3 的列间距必须恒为版面表 ColumnGap（宽屏多余宽度均分到左右外边距）。
            // 用 RectTransform 布局坐标（anchoredPosition / sizeDelta）而不是世界角点：
            // 入场 UISpringMotion 只改 localScale，世界角点会把「入场缩放进行到一半」的 Hero 卡
            // 误算进间距（曾因此误报 24 vs 32）；布局坐标不受缩放动画影响。
            var firstTile = (RectTransform)safe.Find("HomeTile_RECORD");
            Assert.IsNotNull(firstTile, "首页必须有战绩卡");
            float heroRight = heroRect.anchoredPosition.x + heroRect.sizeDelta.x;
            float columnGap = firstTile.anchoredPosition.x - heroRight;
            Assert.AreEqual(home.ColumnGap, columnGap, 0.5f,
                "Hero 与 2×3 卡阵之间的列间距应恒等于 ColumnGap（实际 " + columnGap.ToString("0.0") + "）");

            ScreenCapture.CaptureScreenshot(System.IO.Path.GetFullPath("Logs/menu_redesign_screenshot.png"));
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTest]
        [Category("UI")]
        public IEnumerator MatchFlowUI_HomeTileArrow_KeepsOutOfInfoLine()
        {
            yield return null;
            var flow = Object.FindAnyObjectByType<MatchFlowUI>();
            Assert.IsNotNull(flow, "场景中应有 MatchFlowUI");

            var safe = flow.transform.Find("ReadyPanel/MenuSafeArea");
            Assert.IsNotNull(safe, "首页内容必须在安全区内");

            // 旧实现把 `》`（双书名号 ≈2em 宽）按 1.2em 预留、还放在信息行上，
            // 移动端字号 ×1.28 后必然压字叠字 —— 现在箭头独占标题行右侧走廊。
            var tileNames = new[] { "HomeTile_RECORD", "HomeTile_LOOK", "HomeTile_ACHIEVE", "HomeTile_SETTINGS", "HomeTile_LAYOUT" };
            Transform content = null;
            RectTransform arrow = null;
            TextMeshProUGUI info = null;
            RectTransform infoRect = null;

            for (int i = 0; i < tileNames.Length; i++)
            {
                Transform tile = safe.Find(tileNames[i]);
                Assert.IsNotNull(tile, "首页必须有 " + tileNames[i]);

                content = tile.Find("Content");
                Assert.IsNotNull(content, tileNames[i] + " 应有 Content 容器");

                arrow = content.Find("TileArrow") as RectTransform;
                info = content.Find("TileInfo")?.GetComponent<TextMeshProUGUI>();
                Assert.IsNotNull(arrow, tileNames[i] + " 的可点卡应有 `》` 箭头");
                Assert.IsNotNull(info, tileNames[i] + " 应有信息行");
                infoRect = info.rectTransform;

                // 1) 箭头整段位于信息行上方（两者竖向不重叠）。
                float arrowBottom = arrow.anchoredPosition.y - arrow.rect.height;
                float infoTop = infoRect.anchoredPosition.y;
                Assert.Greater(arrowBottom, infoTop,
                    tileNames[i] + " 的 `》` 不得压在信息行上（箭头下沿 " + arrowBottom.ToString("0.0") + " 应高于信息行上沿 " + infoTop.ToString("0.0") + "）");

                // 2) 信息行拿整行宽度：不再为箭头让宽（让宽就是叠字的根因）。
                float titleWidth = ((RectTransform)content.Find("TileTitle")).sizeDelta.x;
                Assert.Greater(infoRect.sizeDelta.x, titleWidth, tileNames[i] + " 信息行宽度应大于标题行（标题行要让出箭头走廊）");

                // 3) 文案必须整行放得下（规范禁 Truncate，靠字号自适应兜底）。
                Assert.LessOrEqual(info.preferredWidth, infoRect.sizeDelta.x + 0.5f,
                    tileNames[i] + " 信息行文案溢出（宽 " + info.preferredWidth.ToString("0") + " / 可用 " + infoRect.sizeDelta.x.ToString("0") + "）");
                Assert.IsFalse(string.IsNullOrEmpty(info.text), tileNames[i] + " 信息行不能为空");

                // 4) 角签黑底必须包住标签文本（SETTINGS 自然宽度远大于 RECORD，固定宽会画出黑底）。
                var tagRect = content.Find("TileTag") as RectTransform;
                var tagText = tagRect != null ? tagRect.Find("Text")?.GetComponent<TextMeshProUGUI>() : null;
                Assert.IsNotNull(tagRect, tileNames[i] + " 应有角签");
                Assert.IsNotNull(tagText, tileNames[i] + " 角签应有文字");
                Assert.LessOrEqual(tagText.preferredWidth, tagRect.sizeDelta.x - 20f + 0.5f,
                    tileNames[i] + " 角签文字溢出黑底（宽 " + tagText.preferredWidth.ToString("0") +
                    " / 可用 " + (tagRect.sizeDelta.x - 20f).ToString("0") + "）");
            }
        }

        [UnityTest]
        [Category("UI")]
        public IEnumerator MatchFlowUI_HomeGeometryRebuild_KeepsStartCtaAlive()
        {
            yield return null;
            var flow = Object.FindAnyObjectByType<MatchFlowUI>();
            Assert.IsNotNull(flow, "场景中应有 MatchFlowUI");

            var ready = flow.transform.Find("ReadyPanel");
            Assert.IsNotNull(ready);
            Transform safeBefore = ready.Find("MenuSafeArea");
            Assert.IsNotNull(safeBefore);
            Assert.IsNotNull(safeBefore.Find("StartButton"), "重建前开始按钮应在安全区内");

            // 真机（Android）启动/旋转后安全区 settle 会触发私有的 RebuildHomeVisuals，
            // 这里直接反射调用复现：旧实现把挂在安全区里的场景 StartButton 一起 Destroy，
            // startButton 变 null 后首页再也建不出 CTA —— 玩家卡在首页开不了局。
            var rebuild = typeof(MatchFlowUI).GetMethod("RebuildHomeVisuals",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(rebuild, "MatchFlowUI 应保留几何重建入口（否则本测试失去意义）");
            rebuild.Invoke(flow, null);
            yield return null;

            Transform safeAfter = ready.Find("MenuSafeArea");
            Assert.IsNotNull(safeAfter, "重建后应存在新的安全区容器");
            Assert.AreNotSame(safeBefore, safeAfter, "重建应换用新的安全区容器");

            Transform startAfter = safeAfter.Find("StartButton");
            Assert.IsNotNull(startAfter, "首页几何重建后开始按钮必须存活，否则首页 CTA 与开赛入口全部消失");

            var home = MenuLayoutMath.Home(UITypography.IsMobileLike);
            var startRect = (RectTransform)startAfter;
            Assert.AreEqual(home.CtaSize.y, startRect.sizeDelta.y, 0.5f, "重建后 CTA 仍应按版面表摆位");

            var hero = safeAfter.Find("HomeHeroCard");
            Assert.IsNotNull(hero, "重建后首页卡片树应完整重建");
            Assert.IsNotNull(safeAfter.Find("HomeTile_LAYOUT"), "重建后 2×3 卡阵应完整重建");
        }

        [UnityTest]
        [Category("UI")]
        public IEnumerator MatchFlowUI_FullscreenPanelsAndUnderlay_FillScreen()
        {
            var flowGo = new GameObject("TestMatchFlowUI", typeof(RectTransform), typeof(Canvas), typeof(MatchFlowUI));
            _createdObjects.Add(flowGo);
            var flow = flowGo.GetComponent<MatchFlowUI>();

            var readyPanel = new GameObject("ReadyPanel", typeof(RectTransform), typeof(Image));
            readyPanel.transform.SetParent(flowGo.transform, false);

            var resultPanel = new GameObject("ResultPanel", typeof(RectTransform), typeof(Image));
            resultPanel.transform.SetParent(flowGo.transform, false);

            flow.BindReferences(
                readyPanel, null, null, null, null,
                null, null,
                resultPanel, null,
                null, null, null, null, null, null);

            yield return null;

            // 1. 验证全屏纸面底板已创建且处于最底层
            var underlay = flowGo.transform.Find("FullscreenPaperUnderlay");
            Assert.IsNotNull(underlay, "必须存在 FullscreenPaperUnderlay 全屏纸面底板");
            var underlayRt = underlay.GetComponent<RectTransform>();
            Assert.AreEqual(Vector2.zero, underlayRt.anchorMin, "底板 anchorMin 必须为 0 铺满屏幕");
            Assert.AreEqual(Vector2.one, underlayRt.anchorMax, "底板 anchorMax 必须为 1 铺满屏幕");

            // 2. 验证 ReadyPanel 与 ResultPanel 保持全屏铺满，不被安全区收缩截断
            var readyRt = (RectTransform)readyPanel.transform;
            Assert.AreEqual(Vector2.zero, readyRt.anchorMin, "ReadyPanel 必须铺满全屏，anchorMin 为 0");
            Assert.AreEqual(Vector2.one, readyRt.anchorMax, "ReadyPanel 必须铺满全屏，anchorMax 为 1");

            var resultRt = (RectTransform)resultPanel.transform;
            Assert.AreEqual(Vector2.zero, resultRt.anchorMin, "ResultPanel 必须铺满全屏，anchorMin 为 0");
            Assert.AreEqual(Vector2.one, resultRt.anchorMax, "ResultPanel 必须铺满全屏，anchorMax 为 1");
        }

        /// <summary>
        /// 运行中平台观感翻转（桌面 ↔ 移动端，例如从强制触屏预览的场景切回桌面场景）时，
        /// 抽屉必须按新度量整棵重建：只更新字段会让 DrawerPanel 保持旧宽度、收起位移却按新宽度算，
        /// 屏幕左缘残留一条白纸+黑边抽屉（回归：选枪抽屉 1574 宽 vs 位移 -1244）。
        /// 本用例自带视图与触屏组件，不依赖当前打开的场景。
        /// </summary>
        [UnityTest]
        [Category("UI")]
        public IEnumerator WeaponSelectView_PlatformFlip_RebuildsDrawerAtNewMetrics()
        {
            yield return null;

            var touch = Object.FindAnyObjectByType<MobileTouchUI>();
            if (touch == null)
            {
                var touchGo = new GameObject("TestMobileTouchUI");
                _createdObjects.Add(touchGo);
                touch = touchGo.AddComponent<MobileTouchUI>();
            }

            var view = WeaponSelectView.Instance;
            if (view == null)
            {
                var viewGo = new GameObject("TestWeaponSelectView", typeof(RectTransform), typeof(Canvas));
                _createdObjects.Add(viewGo);
                view = viewGo.AddComponent<WeaponSelectView>();

                var configs = new WeaponConfigSO[WeaponSelectMath.WeaponCount];
                for (int i = 0; i < configs.Length; i++)
                {
                    configs[i] = CreateConfig(WeaponSelectMath.IndexToKind(i), "TestWeapon" + i);
                }
                view.Configure(null, configs, null, 0);
                yield return null;
            }

            var drawer = view.transform.Find("SafeAreaContainer/DrawerPanel") as RectTransform;
            Assert.IsNotNull(drawer, "抽屉面板应存在");
            Assert.IsFalse(view.IsDrawerOpen, "默认收起，便于验证屏幕边缘是否残留");

            bool original = touch.ShowTouchUiInEditor;
            try
            {
                touch.ShowTouchUiInEditor = true;
                yield return null;
                yield return null;

                Assert.IsTrue(view.IsMobileLayout, "打开触屏模拟后应按移动端度量排版");
                var mobileDrawer = view.transform.Find("SafeAreaContainer/DrawerPanel") as RectTransform;
                Assert.AreEqual(MenuLayoutMath.Drawer(true).Width, mobileDrawer.sizeDelta.x, 0.5f,
                    "翻转后抽屉必须换成移动端宽度");
                Assert.Less(mobileDrawer.anchoredPosition.x + mobileDrawer.sizeDelta.x, -0.5f,
                    "收起的抽屉必须整块退出画布左缘，不得残留竖条");

                touch.ShowTouchUiInEditor = false;
                yield return null;
                yield return null;

                Assert.IsFalse(view.IsMobileLayout, "关掉触屏模拟后应回到桌面度量");
                var desktopDrawer = view.transform.Find("SafeAreaContainer/DrawerPanel") as RectTransform;
                Assert.AreEqual(MenuLayoutMath.Drawer(false).Width, desktopDrawer.sizeDelta.x, 0.5f,
                    "翻转回来抽屉必须换回桌面宽度");
                Assert.Less(desktopDrawer.anchoredPosition.x + desktopDrawer.sizeDelta.x, -0.5f,
                    "切回桌面后同样不得在屏幕左缘残留抽屉");
            }
            finally
            {
                touch.ShowTouchUiInEditor = original;
            }
        }
    }
}
