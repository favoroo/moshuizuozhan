using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using P1.UI;
using UnityEditor;
using UnityEngine;

namespace P1.Tests
{
    /// <summary>
    /// 首页地图选择的数据层与版面表门禁：
    /// 默认图不变、非法存档回退、选择写盘、两张图都在 Build Settings，以及浮层字号/安全区下限。
    /// </summary>
    public class MapCatalogTests
    {
        private string _savedPrefsValue;
        private bool _hadSavedValue;

        [SetUp]
        public void SetUp()
        {
            // EditMode 测试写的是真实 PlayerPrefs：先快照，结束后还原，避免清掉玩家自己的选图。
            _hadSavedValue = PlayerPrefs.HasKey(MapCatalogService.SelectedMapPrefsKey);
            _savedPrefsValue = PlayerPrefs.GetString(MapCatalogService.SelectedMapPrefsKey, string.Empty);
            PlayerPrefs.DeleteKey(MapCatalogService.SelectedMapPrefsKey);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hadSavedValue)
            {
                PlayerPrefs.SetString(MapCatalogService.SelectedMapPrefsKey, _savedPrefsValue);
            }
            else
            {
                PlayerPrefs.DeleteKey(MapCatalogService.SelectedMapPrefsKey);
            }
            PlayerPrefs.Save();
        }

        [Test]
        public void DefaultSelection_IsPhaseA_WithoutWritingPrefs()
        {
            Assert.AreEqual(MapCatalogService.PhaseAScenePath, MapCatalogService.Selected.ScenePath);
            Assert.AreEqual(MapCatalogService.DefaultMap.Id, MapCatalogService.SelectedId);

            // 老玩家首进不能被自动写盘：没有存档时应保持「无 key」。
            Assert.IsFalse(PlayerPrefs.HasKey(MapCatalogService.SelectedMapPrefsKey));
        }

        [Test]
        public void InvalidSavedId_FallsBackToPhaseA()
        {
            PlayerPrefs.SetString(MapCatalogService.SelectedMapPrefsKey, "not_a_real_map");
            PlayerPrefs.Save();

            Assert.AreEqual(MapCatalogService.DefaultMap.Id, MapCatalogService.SelectedId);
            Assert.AreEqual(MapCatalogService.PhaseAScenePath, MapCatalogService.Selected.ScenePath);
        }

        [Test]
        public void TrySelect_PersistsAndReadsBack()
        {
            Assert.IsTrue(MapCatalogService.TrySelect("kelp_terminal"));
            Assert.AreEqual("kelp_terminal", PlayerPrefs.GetString(MapCatalogService.SelectedMapPrefsKey));
            Assert.AreEqual(MapCatalogService.KelpScenePath, MapCatalogService.Selected.ScenePath);

            // 已选同一张且存档存在时不再重复写盘（返回 false，但仍指向同一张图）。
            Assert.IsFalse(MapCatalogService.TrySelect("kelp_terminal"));
            Assert.AreEqual(MapCatalogService.KelpScenePath, MapCatalogService.Selected.ScenePath);
        }

        [Test]
        public void TrySelect_RejectsUnknownId()
        {
            Assert.IsFalse(MapCatalogService.TrySelect("unknown"));
            Assert.AreEqual(MapCatalogService.DefaultMap.Id, MapCatalogService.SelectedId);
            Assert.IsFalse(PlayerPrefs.HasKey(MapCatalogService.SelectedMapPrefsKey));
        }

        [Test]
        public void ResetForTests_RemovesSelection()
        {
            MapCatalogService.TrySelect("kelp_terminal");
            MapCatalogService.ResetForTests();

            Assert.IsFalse(PlayerPrefs.HasKey(MapCatalogService.SelectedMapPrefsKey));
            Assert.AreEqual(MapCatalogService.DefaultMap.Id, MapCatalogService.SelectedId);
        }

        [Test]
        public void Maps_HaveUniqueIdsAndScenePaths()
        {
            var ids = new HashSet<string>();
            var paths = new HashSet<string>();

            Assert.GreaterOrEqual(MapCatalogService.Maps.Count, 2);
            for (int i = 0; i < MapCatalogService.Maps.Count; i++)
            {
                MapDefinition map = MapCatalogService.Maps[i];
                Assert.IsNotEmpty(map.Id, "地图 Id 不能为空");
                Assert.IsNotEmpty(map.DisplayName, "地图显示名不能为空");
                Assert.IsNotEmpty(map.EnglishName, "地图英文名不能为空");
                Assert.IsNotEmpty(map.Descriptor, "地图差异说明不能为空");
                Assert.IsTrue(ids.Add(map.Id), "地图 Id 重复：" + map.Id);
                Assert.IsTrue(paths.Add(map.ScenePath), "地图场景路径重复：" + map.ScenePath);

                string sceneName = Path.GetFileNameWithoutExtension(map.ScenePath);
                StringAssert.Contains(sceneName, map.ScenePath, "场景名与路径不一致：" + map.Id);
            }

            Assert.AreNotEqual(MapCatalogService.Maps[0], MapCatalogService.Maps[1]);
        }

        [Test]
        public void BothMapScenes_AreEnabledInBuildSettings()
        {
            var enabledPaths = new HashSet<string>();
            for (int i = 0; i < EditorBuildSettings.scenes.Length; i++)
            {
                EditorBuildSettingsScene scene = EditorBuildSettings.scenes[i];
                if (scene.enabled) enabledPaths.Add(scene.path);
            }

            for (int i = 0; i < MapCatalogService.Maps.Count; i++)
            {
                string path = MapCatalogService.Maps[i].ScenePath;
                Assert.IsTrue(enabledPaths.Contains(path), "地图场景未在 Build Settings 启用：" + path);
                Assert.IsTrue(File.Exists(path), "地图场景文件不存在：" + path);
            }
        }

        [Test]
        public void MapForScene_AndOtherThan_RoundTrip()
        {
            MapDefinition phaseA = MapCatalogService.MapForScene(MapCatalogService.PhaseAScenePath);
            MapDefinition kelp = MapCatalogService.MapForScene(MapCatalogService.KelpScenePath);

            Assert.IsNotNull(phaseA);
            Assert.IsNotNull(kelp);
            Assert.IsNull(MapCatalogService.MapForScene("Assets/_Project/Scenes/SampleScene.unity"));

            Assert.AreEqual(kelp, MapCatalogService.OtherThan(phaseA));
            Assert.AreEqual(phaseA, MapCatalogService.OtherThan(kelp));
        }

        [Test]
        public void LayoutFonts_RespectReadableFloor()
        {
            AssertFontFloor(MapSelectLayoutMath.Metrics(false), 18f, "桌面");
            AssertFontFloor(MapSelectLayoutMath.Metrics(true), 18f, "移动端");
        }

        private static void AssertFontFloor(MapSelectMetrics m, float floor, string label)
        {
            Assert.GreaterOrEqual(m.TagFont, floor, label + " Tag 字号");
            Assert.GreaterOrEqual(m.TitleFont, floor, label + " 标题字号");
            Assert.GreaterOrEqual(m.SubtitleFont, floor, label + " 副标题字号");
            Assert.GreaterOrEqual(m.CloseFont, floor, label + " 关闭键字号");
            Assert.GreaterOrEqual(m.CardTitleFont, floor, label + " 卡标题字号");
            Assert.GreaterOrEqual(m.CardEnglishFont, floor, label + " 卡英文字号");
            Assert.GreaterOrEqual(m.CardDescriptorFont, floor, label + " 卡描述字号");
            Assert.GreaterOrEqual(m.CardBadgeFont, floor, label + " 角签字号");
            Assert.GreaterOrEqual(m.CardButtonFont, floor, label + " 卡按钮字号");
            Assert.GreaterOrEqual(m.NoteFont, floor, label + " 页脚字号");
        }

        [Test]
        public void ResolvePanelSize_StaysInsideSafeArea()
        {
            // 桌面 1920×1080 与移动端 2340×1080（横屏安全区）都要落在安全区内。
            AssertPanelWithinSafeArea(false, 1920f, 1080f);
            AssertPanelWithinSafeArea(true, 2340f, 1080f);
            AssertPanelWithinSafeArea(false, 1280f, 720f);
        }

        private static void AssertPanelWithinSafeArea(bool mobile, float safeWidth, float safeHeight)
        {
            Vector2 size = MapSelectLayoutMath.ResolvePanelSize(mobile, safeWidth, safeHeight);
            Assert.LessOrEqual(size.x, safeWidth, "面板宽度超出安全区");
            Assert.LessOrEqual(size.y, safeHeight, "面板高度超出安全区");
        }

        [Test]
        public void PreviewSize_KeepsArenaAspectRatio()
        {
            Vector2 desktop = MapSelectLayoutMath.PreviewSize(false, 420f, 620f);
            Vector2 mobile = MapSelectLayoutMath.PreviewSize(true, 760f, 720f);

            Assert.AreEqual(40f / 56f, desktop.x / desktop.y, 0.01f, "桌面预览比例应保持 40×56");
            Assert.AreEqual(40f / 56f, mobile.x / mobile.y, 0.01f, "移动端预览比例应保持 40×56");
            Assert.Greater(desktop.x, 0f);
            Assert.Greater(desktop.y, 0f);
        }

        /// <summary>
        /// 横版卡片（预览在左、文字在右、按钮通栏）：预览既不能横向吃掉文字列，
        /// 也不能纵向压到底部按钮——这两条是「手机端卡片文字被遮挡」的回归门禁。
        /// </summary>
        [Test]
        public void PreviewSize_LeavesRoomForTextColumnAndButton()
        {
            AssertPreviewLeavesRoom(false, 420f, 620f, "桌面");
            AssertPreviewLeavesRoom(true, 760f, 720f, "移动端 16:9");
            AssertPreviewLeavesRoom(true, 925f, 572f, "移动端 20:9");

            Vector2 phone = MapSelectLayoutMath.PreviewSize(true, 925f, 572f);
            Assert.Greater(phone.y, 300f, "20:9 手机上预览应撑满卡片左列，避免右半边留白");
        }

        private static void AssertPreviewLeavesRoom(bool mobile, float cardWidth, float cardHeight, string label)
        {
            MapSelectMetrics m = MapSelectLayoutMath.Metrics(mobile);
            Vector2 preview = MapSelectLayoutMath.PreviewSize(mobile, cardWidth, cardHeight);
            float innerWidth = cardWidth - m.CardInnerPadding * 2f;

            Assert.LessOrEqual(preview.x, innerWidth * 0.46f + 0.5f,
                label + " 预览宽度超过卡片内宽一半，右侧留不出文字列");

            float buttonTop = cardHeight - m.CardInnerPadding - m.CardButtonHeight;
            float previewBottom = m.CardBandHeight + m.CardInnerPadding * 0.55f + preview.y;
            Assert.Less(previewBottom, buttonTop, label + " 预览压到底部按钮，文字块无处安放");

            Assert.LessOrEqual(preview.y, cardHeight - m.CardInnerPadding * 2f - m.CardBandHeight - m.CardButtonHeight,
                label + " 预览高度超出卡片可用高度");
        }
    }
}
