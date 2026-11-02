using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

namespace P1.Editor
{
    /// <summary>
    /// 品牌资产接线（菜单 P1/构建/13）：把 tools/bake_brand.py 烘出的 PNG 写进 PlayerSettings。
    ///
    /// 为什么必须走 Editor API 而不是手改 ProjectSettings.asset：
    /// - Android 自适应图标是「背景层 + 前景层」两层结构，YAML 里是嵌套数组，手改容易错位；
    /// - 图标 Texture 必须按 Default 导入（Sprite 导入会把 maxTextureSize 钳到 512，
    ///   1024 主图标直接废掉），导入策略统一在 MediaImportPostprocessor 里维护；
    /// - 构建时 ApplyDesktop/ApplyAndroidPlayerSettings 也会调用本类，
    ///   保证「谁出包都不会漏图标」，菜单只是手动重刷入口。
    /// </summary>
    public static class BrandAssetInstaller
    {
        public const string BrandRoot = "Assets/_Project/Art/Brand/";
        public const string SpriteRoot = "Assets/_Project/Art/Sprites/Icons/Brand/";

        public const string IconPath = BrandRoot + "app_icon_1024.png";
        public const string AdaptiveBackgroundPath = BrandRoot + "android_adaptive_background_432.png";
        public const string AdaptiveForegroundPath = BrandRoot + "android_adaptive_foreground_432.png";
        public const string SplashLogoPath = SpriteRoot + "splash_logo.png";

        /// <summary>启动画面底色 = 几何大胆风舞台墨黑（#0B0B0B），与游戏内启动动画无缝衔接。</summary>
        public static readonly Color SplashBackground = new Color(0.043f, 0.043f, 0.043f, 1f);

        [MenuItem("P1/构建/13. 应用 App 图标与启动画面 (App Icon & Splash)", false, 85)]
        public static void ApplyBrandAssetsMenu()
        {
            if (!ApplyBrandAssets(out string report))
            {
                Debug.LogError("[P1][Brand] App 图标与启动画面接线失败：\n" + report);
                return;
            }

            Debug.Log("[P1][Brand] App 图标与启动画面接线完成：\n" + report);
        }

        /// <summary>把图标与启动画面写入 PlayerSettings；返回 false 表示关键资产缺失。</summary>
        public static bool ApplyBrandAssets(out string report)
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var adaptiveBg = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveBackgroundPath);
            var adaptiveFg = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveForegroundPath);
            var splashLogo = AssetDatabase.LoadAssetAtPath<Sprite>(SplashLogoPath);

            var lines = new System.Text.StringBuilder();
            if (icon == null || adaptiveBg == null || adaptiveFg == null)
            {
                report = $"缺少 App 图标（先跑 python3 tools/bake_brand.py）：\n" +
                         $"{IconPath}: {icon != null}\n" +
                         $"{AdaptiveBackgroundPath}: {adaptiveBg != null}\n{AdaptiveForegroundPath}: {adaptiveFg != null}";
                return false;
            }

            // ---------- Standalone（macOS/Windows 出包时生成 .icns/.ico） ----------
            // 槽位数必须与 GetIcons 返回的一致：少给会报
            // "Attempting to set an incorrect number of icons for Standalone: 8 expected"。
            // 8 个槽位全部填同一张 1024 母版，由 Unity 在出包时按槽位缩放。
            Texture2D[] standaloneSlots = PlayerSettings.GetIcons(
                NamedBuildTarget.Standalone, IconKind.Application);
            int standaloneCount = standaloneSlots != null && standaloneSlots.Length > 0
                ? standaloneSlots.Length
                : 8;
            var standaloneIcons = new Texture2D[standaloneCount];
            for (int i = 0; i < standaloneCount; i++) standaloneIcons[i] = icon;
            PlayerSettings.SetIcons(NamedBuildTarget.Standalone, standaloneIcons, IconKind.Application);
            lines.AppendLine($"Standalone: {Path.GetFileName(IconPath)} × {standaloneCount} 槽位");

            // ---------- Android：Unity 6 只保留 Adaptive（Legacy/Round 已是 CS0619 硬错误） ----------
            // 背景层负责墨黑 + 橙圆，前景层负责乌贼 + 蓝墨点，由系统遮罩裁成圆形/圆角方形。
            ApplyAdaptiveIcons(adaptiveBg, adaptiveFg, lines);

            // ---------- Unity 启动画面 ----------
            // Personal 版无法关闭 Unity logo，但背景与自有 logo 仍可配置：
            // 黑底 + 橙圆标，进游戏后由 LaunchIntroView 接管同一套视觉。
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = true;
            PlayerSettings.SplashScreen.backgroundColor = SplashBackground;
            PlayerSettings.SplashScreen.overlayOpacity = 1f;
            PlayerSettings.SplashScreen.animationBackgroundZoom = 1f;
            PlayerSettings.SplashScreen.animationLogoZoom = 1f;
            PlayerSettings.SplashScreen.drawMode = PlayerSettings.SplashScreen.DrawMode.AllSequential;
            if (splashLogo != null)
            {
                PlayerSettings.SplashScreen.logos = new[]
                {
                    PlayerSettings.SplashScreenLogo.Create(1.2f, splashLogo),
                };
                lines.AppendLine($"Splash: {Path.GetFileName(SplashLogoPath)}（1.2s，底 #0B0B0B）");
            }
            else
            {
                lines.AppendLine("Splash: 缺少 splash_logo.png（保留现有 logo 列表）");
            }

            AssetDatabase.SaveAssets();
            report = lines.ToString().TrimEnd();
            return true;
        }

        private static void ApplyAdaptiveIcons(Texture2D background, Texture2D foreground,
            System.Text.StringBuilder lines)
        {
            PlatformIcon[] slots = PlayerSettings.GetPlatformIcons(
                NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive);
            if (slots == null || slots.Length == 0)
            {
                lines.AppendLine("Android Adaptive: 没有可用槽位（Android 模块未安装？）");
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                // 自适应图标固定两层：layer 0 = 背景，layer 1 = 前景。
                slots[i].SetTextures(new[] { background, foreground });
            }

            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, AndroidPlatformIconKind.Adaptive, slots);
            lines.AppendLine($"Android Adaptive: {slots.Length} 槽位 × 2 层");
        }
    }
}