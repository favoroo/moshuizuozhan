using System;
using UnityEngine;
using UnityEngine.UI;

namespace P1.UI
{
    /// <summary>
    /// 移动端触控层 DPI 与物理尺寸自适应计算器（Comic P10）。
    ///
    /// 核心目标：
    /// 1. 基于设备的 Screen.dpi 与 CanvasScaler.scaleFactor 计算真实设备像素与物理毫米比例；
    /// 2. 保证主流移动设备上：主键（射击）物理直径在 11~13mm，副键在 9~11mm；
    /// 3. 整体自适应缩放严格钳制在 0.85~1.25 范围内，避免在平板上占满屏幕或在超高密屏上过小；
    /// 4. 纯函数设计，独立可单测；且仅影响移动端触控层，绝不缩放 HUD。
    /// </summary>
    public static class MobileUiMetrics
    {
        public const float MillimetersPerInch = 25.4f;
        public const float DefaultDpi = 400f;
        public const float MinScale = 0.85f;
        public const float MaxScale = 1.25f;

        public const float PrimaryTargetMm = 12.0f;           // 主键目标直径：11~13mm 的中心值
        public const float PrimaryReferenceUiSize = 176.0f;    // 1920x1080 参考分辨率下的射击主键直径
        public const float SecondaryMinTargetMm = 9.0f;        // 副键目标直径下限
        public const float SecondaryMaxTargetMm = 11.0f;       // 副键目标直径上限

        /// <summary>
        /// 纯函数：根据设备 DPI 与 Canvas 的 scaleFactor 计算触控层自适应缩放比。
        /// </summary>
        /// <param name="screenDpi">设备的物理每英寸像素数；小于等于 10 或非法时按 400 兜底。</param>
        /// <param name="scaleFactor">CanvasScaler 当前的缩放比例（1 UI 像素 = scaleFactor 设备像素）。</param>
        /// <param name="targetMm">目标物理毫米直径（默认 12mm）。</param>
        /// <param name="referenceUiSize">参考 UI 单位尺寸（默认 176）。</param>
        /// <returns>钳制在 0.85 ~ 1.25 之间的最终比例系数。</returns>
        public static float ComputeScale(
            float screenDpi,
            float scaleFactor,
            float targetMm = PrimaryTargetMm,
            float referenceUiSize = PrimaryReferenceUiSize)
        {
            float effectiveDpi = (screenDpi > 10f && !float.IsNaN(screenDpi) && !float.IsInfinity(screenDpi))
                ? screenDpi
                : DefaultDpi;

            float safeScaleFactor = (scaleFactor > 0.001f && !float.IsNaN(scaleFactor) && !float.IsInfinity(scaleFactor))
                ? scaleFactor
                : 1.0f;

            float safeRefSize = referenceUiSize > 0.001f ? referenceUiSize : PrimaryReferenceUiSize;
            float safeTargetMm = targetMm > 0.001f ? targetMm : PrimaryTargetMm;

            // 1 UI 单位 = safeScaleFactor 设备像素
            // 1 物理毫米 = effectiveDpi / MillimetersPerInch 设备像素
            // 故 1 UI 单位 = safeScaleFactor * (MillimetersPerInch / effectiveDpi) 毫米
            float mmPerUiUnit = safeScaleFactor * (MillimetersPerInch / effectiveDpi);
            float currentPhysicalMm = safeRefSize * mmPerUiUnit;

            if (currentPhysicalMm <= 0.001f)
            {
                return 1.0f;
            }

            float idealScale = safeTargetMm / currentPhysicalMm;
            return Mathf.Clamp(idealScale, MinScale, MaxScale);
        }

        /// <summary>
        /// 从 Canvas 读取 scaleFactor 并结合当前屏幕 DPI 计算缩放比。
        /// </summary>
        public static float ComputeScale(Canvas canvas)
        {
            float scaleFactor = canvas != null ? canvas.scaleFactor : 1.0f;
            return ComputeScale(Screen.dpi, scaleFactor);
        }

        /// <summary>
        /// 将 UI 逻辑像素单位转换为设备的物理毫米尺寸。
        /// </summary>
        public static float UiUnitsToMillimeters(float uiUnits, float scaleFactor, float screenDpi)
        {
            float effectiveDpi = (screenDpi > 10f && !float.IsNaN(screenDpi) && !float.IsInfinity(screenDpi))
                ? screenDpi
                : DefaultDpi;
            float safeScaleFactor = (scaleFactor > 0.001f && !float.IsNaN(scaleFactor) && !float.IsInfinity(scaleFactor))
                ? scaleFactor
                : 1.0f;

            return uiUnits * safeScaleFactor * (MillimetersPerInch / effectiveDpi);
        }

        /// <summary>
        /// 将物理毫米尺寸转换为 UI 逻辑像素单位。
        /// </summary>
        public static float MillimetersToUiUnits(float mm, float scaleFactor, float screenDpi)
        {
            float effectiveDpi = (screenDpi > 10f && !float.IsNaN(screenDpi) && !float.IsInfinity(screenDpi))
                ? screenDpi
                : DefaultDpi;
            float safeScaleFactor = (scaleFactor > 0.001f && !float.IsNaN(scaleFactor) && !float.IsInfinity(scaleFactor))
                ? scaleFactor
                : 1.0f;

            float mmPerUiUnit = safeScaleFactor * (MillimetersPerInch / effectiveDpi);
            return mmPerUiUnit > 0.0001f ? (mm / mmPerUiUnit) : mm;
        }

        /// <summary>
        /// 几何大胆风：计算动作按键在移动端上的最小可读标签字号（保证物理可读性）。
        /// 基准为 max(24, buttonSize * 0.20)，并走 UITypography 统一放大。
        /// </summary>
        public static float ComputeButtonLabelFontSize(float buttonDiameter)
        {
            float baseSize = Mathf.Max(24f, buttonDiameter * 0.20f);
            return UITypography.FontSnapped(baseSize);
        }

        /// <summary>
        /// 应用 DPI 缩放到移动端触控交互层（仅影响触控层，绝不触碰 HUD）。
        /// </summary>
        public static float ApplyToTouchUI(MobileTouchUI touchUI)
        {
            if (touchUI == null) return 1.0f;
            float scale = ComputeScale(Screen.dpi, touchUI.CanvasScaleFactor);
            touchUI.ApplyDpiScale(scale);
            return scale;
        }
    }
}
