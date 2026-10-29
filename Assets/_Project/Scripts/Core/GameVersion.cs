using System;

namespace P1.Core
{
    /// <summary>
    /// 游戏版本定义与语义化版本比较工具。
    /// 对标 Godot 项目的 Version.gd。
    /// </summary>
    public static class GameVersion
    {
        public const string APP_NAME = "墨水作战";
        public const string APP_NAME_EN = "InkTurf";
        public const string APP_VERSION = "0.1.1";
        public const string APP_VERSION_NAME = "v" + APP_VERSION;

        public const string GITHUB_OWNER = "favoroo";
        public const string GITHUB_REPO = "moshuizuozhan";
        public const string GITEE_OWNER = "favo9";
        public const string GITEE_REPO = "moshuizuozhan";

        public static readonly string[] GH_PROXIES = new string[]
        {
            "https://ghfast.top/",
            "https://gh-proxy.com/",
            "https://ghproxy.net/"
        };

        /// <summary>
        /// 解析语义化版本字符串为三元整数数组，如 "v0.1.2-beta" -> [0, 1, 2]
        /// </summary>
        public static int[] ParseVersion(string versionStr)
        {
            if (string.IsNullOrWhiteSpace(versionStr))
                return new[] { 0, 0, 0 };

            string clean = versionStr.Trim();
            if (clean.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(1);

            int hyphenIdx = clean.IndexOf('-');
            if (hyphenIdx >= 0) clean = clean.Substring(0, hyphenIdx);
            int plusIdx = clean.IndexOf('+');
            if (plusIdx >= 0) clean = clean.Substring(0, plusIdx);

            string[] parts = clean.Split('.');
            int[] nums = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (i < parts.Length && int.TryParse(parts[i], out int n))
                    nums[i] = n;
                else
                    nums[i] = 0;
            }
            return nums;
        }

        /// <summary>
        /// 比较候选版本 candidate 是否高于基准版本 current（默认本地当前版本）
        /// </summary>
        public static bool IsVersionNewer(string candidate, string current = APP_VERSION)
        {
            int[] v1 = ParseVersion(candidate);
            int[] v2 = ParseVersion(current);
            for (int i = 0; i < 3; i++)
            {
                if (v1[i] > v2[i]) return true;
                if (v1[i] < v2[i]) return false;
            }
            return false;
        }

        /// <summary>
        /// 获取发布页面链接（浏览器逃生通道）
        /// </summary>
        public static string GetReleasePageUrl(string tagName = "")
        {
            string baseGitee = $"https://gitee.com/{GITEE_OWNER}/{GITEE_REPO}/releases";
            if (!string.IsNullOrEmpty(tagName))
                return $"{baseGitee}/tag/{tagName}";
            return $"{baseGitee}/latest";
        }

        /// <summary>
        /// 格式化字节大小为可读文本（B / KB / MB）
        /// </summary>
        public static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024f:F0} KB";
            return $"{bytes / (1024f * 1024f):F1} MB";
        }
    }
}
