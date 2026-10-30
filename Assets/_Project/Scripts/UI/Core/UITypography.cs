using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 全局字号系统（几何大胆风）：所有界面文本的字号统一从这里取。
    ///
    /// 为什么需要它：历史 bug 是「移动端字号反而比桌面小」（计时器 80→68、墨水罐 22→14、
    /// 大招球 22→17），用户在手机上根本看不清。规范（Docs/UI-GeometricBold.md）要求：
    /// 移动端整体放大一档（×1.28），且正文永远不低于 18（移动端 20）。
    ///
    /// 用法：`text.fontSize = UITypography.Font(ComicStyle.FontH2);`
    /// 纯装饰性超大数字（Hero 标题）也走这里，保持"手机上更大"的一致性。
    /// </summary>
    public static class UITypography
    {
        /// <summary>移动端/触摸模拟都按"移动端观感"放大，桌面编辑器也能预览到真机效果。</summary>
        public static bool IsMobileLike =>
            Application.isMobilePlatform || MobileTouchUI.IsTouchSimulationActive;

        /// <summary>
        /// 基础字号 → 平台字号：移动端 ×1.28，桌面原样；最后按正文底线兜底（桌面 18 / 移动 20）。
        /// </summary>
        public static float Font(float baseSize)
        {
            float size = IsMobileLike ? baseSize * ComicStyle.MobileFontBoost : baseSize;
            return Mathf.Max(IsMobileLike ? 20f : ComicStyle.MinReadableFontSize, size);
        }

        /// <summary>需要严格对齐栅格时的取整版本（字号取偶数，避免 TMP 半像素模糊）。</summary>
        public static float FontSnapped(float baseSize)
        {
            float size = Font(baseSize);
            return Mathf.Round(size * 0.5f) * 2f;
        }

        /// <summary>正文默认字号（22 → 移动端 28）。</summary>
        public static float Body => FontSnapped(ComicStyle.FontBody);

        /// <summary>小字默认字号（18 → 移动端 24），不允许再小。</summary>
        public static float Small => FontSnapped(ComicStyle.FontSmall);

        /// <summary>大标题字号（44 → 移动端 56）。</summary>
        public static float H2 => FontSnapped(ComicStyle.FontH2);

        /// <summary>页面主标题（72 → 移动端 92）。</summary>
        public static float H1 => FontSnapped(ComicStyle.FontH1);
    }
}
