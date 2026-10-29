using NUnit.Framework;
using P1.Core;
using P1.UI;
using UnityEngine;
using UnityEngine.UI;

namespace P1.Tests
{
    [TestFixture]
    public class UpdateSystemTests
    {
        [Test]
        public void ParseVersion_HandlesVariousFormats()
        {
            CollectionAssert.AreEqual(new[] { 0, 1, 0 }, GameVersion.ParseVersion("0.1.0"));
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, GameVersion.ParseVersion("v0.1.2"));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, GameVersion.ParseVersion("V1.2.3-beta.1"));
            CollectionAssert.AreEqual(new[] { 2, 0, 0 }, GameVersion.ParseVersion("v2.0.0+20261029"));
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, GameVersion.ParseVersion(""));
            CollectionAssert.AreEqual(new[] { 0, 0, 0 }, GameVersion.ParseVersion(null));
        }

        [Test]
        public void IsVersionNewer_ComparesCorrectly()
        {
            Assert.IsTrue(GameVersion.IsVersionNewer("v0.1.1", "0.1.0"));
            Assert.IsTrue(GameVersion.IsVersionNewer("v0.2.0", "0.1.9"));
            Assert.IsTrue(GameVersion.IsVersionNewer("v1.0.0", "0.9.9"));
            Assert.IsFalse(GameVersion.IsVersionNewer("v0.1.0", "0.1.0"));
            Assert.IsFalse(GameVersion.IsVersionNewer("v0.0.9", "0.1.0"));
            Assert.IsFalse(GameVersion.IsVersionNewer("0.1.0", "0.1.0"));
        }

        [Test]
        public void FormatBytes_ProducesReadableStrings()
        {
            Assert.AreEqual("512 B", GameVersion.FormatBytes(512));
            Assert.AreEqual("25 KB", GameVersion.FormatBytes(25 * 1024));
            Assert.AreEqual("35.0 MB", GameVersion.FormatBytes(35 * 1024 * 1024));
            Assert.AreEqual("", GameVersion.FormatBytes(0));
            Assert.AreEqual("", GameVersion.FormatBytes(-100));
        }

        [Test]
        public void ParseReleaseJson_ExtractsMetadataAndApk()
        {
            string sampleJson = @"
            {
                ""tag_name"": ""v0.1.1"",
                ""name"": ""墨水作战 v0.1.1"",
                ""body"": ""### 新增内容\n- 体验优化"",
                ""draft"": false,
                ""prerelease"": false,
                ""html_url"": ""https://gitee.com/favo9/moshuizuozhan/releases/tag/v0.1.1"",
                ""assets"": [
                    {
                        ""name"": ""moshuizuozhan-v0.1.1-arm64-v8a.apk"",
                        ""browser_download_url"": ""https://gitee.com/favo9/moshuizuozhan/releases/download/v0.1.1/moshuizuozhan-v0.1.1-arm64-v8a.apk"",
                        ""size"": 36700160
                    }
                ]
            }";

            UpdateInfo info = UpdateManager.ParseReleaseJson(sampleJson);
            Assert.IsNotNull(info);
            Assert.AreEqual("v0.1.1", info.tagName);
            Assert.AreEqual("0.1.1", info.version);
            Assert.AreEqual("墨水作战 v0.1.1", info.releaseName);
            Assert.IsTrue(info.hasApk);
            Assert.AreEqual(36700160, info.fileSize);
            Assert.AreEqual("35.0 MB", info.fileSizeText);
            Assert.IsTrue(info.candidateDownloadUrls.Count > 0);
            Assert.AreEqual("https://gitee.com/favo9/moshuizuozhan/releases/download/v0.1.1/moshuizuozhan-v0.1.1-arm64-v8a.apk", info.candidateDownloadUrls[0]);
        }

        [Test]
        public void ParseReleaseJson_IgnoresDraftOrPrerelease()
        {
            string draftJson = @"{ ""tag_name"": ""v9.9.9"", ""draft"": true, ""assets"": [] }";
            Assert.IsNull(UpdateManager.ParseReleaseJson(draftJson));

            string preJson = @"{ ""tag_name"": ""v9.9.9"", ""prerelease"": true, ""assets"": [] }";
            Assert.IsNull(UpdateManager.ParseReleaseJson(preJson));
        }

        [Test]
        public void MatchFlowUI_EnsureUpdateUI_BuildsComponents()
        {
            var go = new GameObject("TestMatchFlowUI", typeof(RectTransform), typeof(MatchFlowUI));
            var flow = go.GetComponent<MatchFlowUI>();

            var readyPanel = new GameObject("ReadyPanel", typeof(RectTransform));
            readyPanel.transform.SetParent(go.transform, false);

            flow.BindReferences(
                readyPanel, null, null, null, null,
                null, null,
                null, null,
                null, null, null, null, null, null);

            flow.EnsureUpdateUI();

            Assert.IsNotNull(flow.CheckUpdateButton, "CheckUpdateButton 应已自动构建");
            Assert.IsNotNull(flow.VersionText, "VersionText 应已自动构建");
            StringAssert.Contains(GameVersion.APP_VERSION_NAME, flow.VersionText.text);

            Object.DestroyImmediate(readyPanel);
            Object.DestroyImmediate(go);
        }
    }
}
