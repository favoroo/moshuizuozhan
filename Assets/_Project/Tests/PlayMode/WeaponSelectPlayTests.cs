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
    /// 2. Bot 的武器配置完全不受玩家换装影响；
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
        public IEnumerator WeaponSelectView_LoadoutCard_ReplacesFlatTab_AndShowsCurrentWeapon()
        {
            yield return null;
            var view = WeaponSelectView.Instance;
            Assert.IsNotNull(view, "场景中应已自动就绪 WeaponSelectView.Instance");

            var toggle = view.transform.Find("SafeAreaContainer/DrawerToggleButton");
            Assert.IsNotNull(toggle, "抽屉收起时应有左下角「武器整备」整备卡");
            var toggleRect = (RectTransform)toggle;
            Assert.AreEqual(Vector2.zero, toggleRect.anchorMin, "整备卡应锚定左下角");
            Assert.AreEqual(Vector2.zero, toggleRect.anchorMax, "整备卡应锚定左下角");
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
            Assert.IsNotNull(start, "开始按钮应在安全区内，锚定右下角拇指区");
            var startRect = (RectTransform)start;
            Assert.AreEqual(new Vector2(1f, 0f), startRect.anchorMin, "开始按钮应锚定右下角");
            Assert.AreEqual(new Vector2(1f, 0f), startRect.anchorMax, "开始按钮应锚定右下角");

            var home = MenuLayoutMath.Home(false);
            Assert.AreEqual(home.CtaSize.x, startRect.sizeDelta.x, 0.5f, "开始按钮宽度应来自版面表");
            Assert.AreEqual(home.CtaSize.y, startRect.sizeDelta.y, 0.5f, "开始按钮高度应来自版面表");
            Assert.GreaterOrEqual(startRect.sizeDelta.y, 140f, "开赛按钮必须是超大触控目标");
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
    }
}
