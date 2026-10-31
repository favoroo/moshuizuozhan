using System.IO;
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
        public void ParseReleaseJson_HandlesGiteePayloadWithoutAssetSize()
        {
            // Gitee 的 Release API 只给 name / browser_download_url，**不给 size**（线上 v0.1.6 实测）。
            // 旧实现因此拿到 fileSize = 0，进度分母退化成「已下载量」，百分比恒为 99%。
            string giteeJson = @"
            {
                ""id"": 1257810,
                ""tag_name"": ""v0.1.6"",
                ""prerelease"": false,
                ""name"": ""墨水作战 v0.1.6"",
                ""body"": ""## 墨水作战 v0.1.6\n\n### 新增内容\n- 首页与武器整备界面全新改版"",
                ""html_url"": ""https://gitee.com/favo9/moshuizuozhan/releases/tag/v0.1.6"",
                ""assets"": [
                    {
                        ""browser_download_url"": ""https://gitee.com/favo9/moshuizuozhan/releases/download/v0.1.6/moshuizuozhan-v0.1.6-arm64-v8a.apk"",
                        ""name"": ""moshuizuozhan-v0.1.6-arm64-v8a.apk""
                    },
                    {
                        ""browser_download_url"": ""https://gitee.com/favo9/moshuizuozhan/archive/refs/tags/v0.1.6.zip"",
                        ""name"": ""v0.1.6.zip""
                    }
                ]
            }";

            UpdateInfo info = UpdateManager.ParseReleaseJson(giteeJson);

            Assert.IsNotNull(info);
            Assert.AreEqual("v0.1.6", info.tagName);
            Assert.AreEqual("0.1.6", info.version);
            Assert.IsTrue(info.hasApk, "应从 assets 里认出 .apk 附件");

            // 元数据没有体积时必须如实为 0（= 未知），交给 HEAD 探测 / 响应头补齐，而不是猜一个数
            Assert.AreEqual(0, info.fileSize);
            Assert.AreEqual("", info.fileSizeText);

            // 更新日志里的 \n 必须被正确反转义（旧实现用 Regex.Unescape，非法转义会抛异常）
            StringAssert.Contains("首页与武器整备界面全新改版", info.releaseNotes);
            StringAssert.Contains("\n", info.releaseNotes);

            // 候选源顺序：Gitee 直连优先，其余为 GitHub 直连 + 代理镜像
            Assert.AreEqual(
                "https://gitee.com/favo9/moshuizuozhan/releases/download/v0.1.6/moshuizuozhan-v0.1.6-arm64-v8a.apk",
                info.candidateDownloadUrls[0]);
            Assert.AreEqual(info.candidateDownloadUrls[0], info.downloadUrl);
            StringAssert.Contains(
                "https://github.com/favoroo/moshuizuozhan/releases/download/v0.1.6/moshuizuozhan-v0.1.6-arm64-v8a.apk",
                info.candidateDownloadUrls[info.candidateDownloadUrls.Count - 1]);
        }

        [Test]
        public void ParseReleaseJson_WithoutApkDoesNotOfferReleasePageAsDownload()
        {
            // 没有 APK 附件时，发布页 URL 只能用于「浏览器下载」逃生通道，
            // 绝不能再进候选下载源 —— 否则会把发布页 HTML 存成 .apk 并报告"下载完成"。
            string noApkJson = @"
            {
                ""tag_name"": ""v0.1.7"",
                ""name"": ""墨水作战 v0.1.7"",
                ""html_url"": ""https://gitee.com/favo9/moshuizuozhan/releases/tag/v0.1.7"",
                ""assets"": []
            }";

            UpdateInfo info = UpdateManager.ParseReleaseJson(noApkJson);

            Assert.IsNotNull(info);
            Assert.IsFalse(info.hasApk);
            Assert.AreEqual(0, info.candidateDownloadUrls.Count);
            Assert.AreEqual("https://gitee.com/favo9/moshuizuozhan/releases/tag/v0.1.7", info.releaseUrl);
        }

        [Test]
        public void ComputeDownloadPercent_UnknownTotalDoesNotReport99()
        {
            // 回归锁：总量未知时绝不能编出百分比 —— 这正是线上「进度一直卡在 99%」的成因。
            Assert.AreEqual(0f, UpdateManager.ComputeDownloadPercent(12_345_678, 0));
            Assert.AreEqual(0f, UpdateManager.ComputeDownloadPercent(0, 0));
            Assert.AreEqual(0f, UpdateManager.ComputeDownloadPercent(-1, -1));

            // 总量已知时按真实比例，且封顶 100
            Assert.AreEqual(50f, UpdateManager.ComputeDownloadPercent(50, 100), 0.01f);
            Assert.AreEqual(100f, UpdateManager.ComputeDownloadPercent(100, 100), 0.01f);
            Assert.AreEqual(100f, UpdateManager.ComputeDownloadPercent(120, 100), 0.01f);
        }

        [Test]
        public void ResolveDownloadTotal_PrefersHeaderThenMetadata()
        {
            // 响应头（本次连接真实字节数）> Release 元数据 > 未知(0)
            Assert.AreEqual(57_402_487, UpdateManager.ResolveDownloadTotal(57_402_487, 36_700_160));
            Assert.AreEqual(36_700_160, UpdateManager.ResolveDownloadTotal(0, 36_700_160));
            Assert.AreEqual(0, UpdateManager.ResolveDownloadTotal(0, 0));
            Assert.AreEqual(0, UpdateManager.ResolveDownloadTotal(-5, -5));
        }

        [Test]
        public void ParseContentLength_OnlyAcceptsPositiveNumbers()
        {
            Assert.AreEqual(57_402_487, UpdateManager.ParseContentLength("57402487"));
            Assert.AreEqual(57_402_487, UpdateManager.ParseContentLength(" 57402487 "));
            Assert.AreEqual(0, UpdateManager.ParseContentLength(null));
            Assert.AreEqual(0, UpdateManager.ParseContentLength(""));
            Assert.AreEqual(0, UpdateManager.ParseContentLength("chunked"));
            Assert.AreEqual(0, UpdateManager.ParseContentLength("0"));
        }

        [Test]
        public void ApkPayloadChecks_RejectHtmlAndTruncatedFiles()
        {
            string dir = Path.Combine(Application.temporaryCachePath, "p1_update_tests");
            Directory.CreateDirectory(dir);
            string apkPath = Path.Combine(dir, "fake.apk");
            string htmlPath = Path.Combine(dir, "page.apk");

            try
            {
                File.WriteAllBytes(apkPath, new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 });
                File.WriteAllText(htmlPath, "<!DOCTYPE html><html><body>404 Not Found</body></html>");

                Assert.IsTrue(UpdateManager.LooksLikeApkHeader(new byte[] { 0x50, 0x4B, 0x03, 0x04 }));
                Assert.IsFalse(UpdateManager.LooksLikeApkHeader(new byte[] { 0x3C, 0x21, 0x44, 0x4F })); // "<!DO"
                Assert.IsFalse(UpdateManager.LooksLikeApkHeader(new byte[] { 0x50, 0x4B }));
                Assert.IsFalse(UpdateManager.LooksLikeApkHeader(null));

                Assert.IsTrue(UpdateManager.IsApkPayload(apkPath));
                Assert.IsFalse(UpdateManager.IsApkPayload(htmlPath));
                Assert.IsFalse(UpdateManager.IsApkPayload(Path.Combine(dir, "missing.apk")));

                // 校验通过
                Assert.IsNull(UpdateManager.ValidateDownloadedApk(apkPath, 6, 0));
                // 传输被截断（响应头说 99999，实际只落了 6 字节）
                StringAssert.Contains("体积不符", UpdateManager.ValidateDownloadedApk(apkPath, 0, 99999));
                // 下到的其实是网页
                StringAssert.Contains("APK", UpdateManager.ValidateDownloadedApk(htmlPath, 0, 0));
                // 文件不存在
                StringAssert.Contains("未生成", UpdateManager.ValidateDownloadedApk(Path.Combine(dir, "missing.apk"), 0, 0));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
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
