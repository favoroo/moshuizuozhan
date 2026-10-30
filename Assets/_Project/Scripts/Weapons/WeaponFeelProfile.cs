using UnityEngine;

namespace P1.Weapons
{
    /// <summary>
    /// 一把武器的「手感表现」参数（后坐力 / 镜头冲击 / 枪口闪光 / 弹丸视觉 / 涂地涟漪）。
    /// 只影响表现层，不参与伤害、射程、耗墨等平衡结算。
    /// </summary>
    public readonly struct WeaponFeelParams
    {
        public readonly float RecoilKickMeters;
        public readonly float RecoilPitchDegrees;
        public readonly float CameraKickMeters;
        public readonly float CameraKickRollDegrees;
        public readonly float CameraKickFovDegrees;
        public readonly float MuzzleFlashScale;
        public readonly float ProjectileVisualScale;
        public readonly float TrailVisualScale;
        public readonly float ImpactRippleScale;

        public WeaponFeelParams(
            float recoilKickMeters,
            float recoilPitchDegrees,
            float cameraKickMeters,
            float cameraKickRollDegrees,
            float cameraKickFovDegrees,
            float muzzleFlashScale,
            float projectileVisualScale,
            float trailVisualScale,
            float impactRippleScale)
        {
            RecoilKickMeters = recoilKickMeters;
            RecoilPitchDegrees = recoilPitchDegrees;
            CameraKickMeters = cameraKickMeters;
            CameraKickRollDegrees = cameraKickRollDegrees;
            CameraKickFovDegrees = cameraKickFovDegrees;
            MuzzleFlashScale = muzzleFlashScale;
            ProjectileVisualScale = projectileVisualScale;
            TrailVisualScale = trailVisualScale;
            ImpactRippleScale = impactRippleScale;
        }
    }

    /// <summary>
    /// 武器手感预设（参考 INKWAVE 的发射器 / 重武器差异）：
    /// 高射速武器（加特林 / 双枪）后坐力压到很低，否则连发时会变成持续抖动；
    /// 单发重武器（爆破枪 / 蓄力狙 / 滚筒挥击）给足一次性冲击，读作「这一下很重」。
    ///
    /// 每个字段都可以被 <see cref="WeaponConfigSO"/> 上对应的同名手感字段覆盖：
    /// 配置里填 &gt; 0 的值优先，填 0（默认，旧资产无需迁移）时回落到这里的类别预设。
    /// </summary>
    public static class WeaponFeelProfile
    {
        /// <summary>按武器类别取默认手感。未知类别回落到小枪。</summary>
        public static WeaponFeelParams ForKind(WeaponKind kind)
        {
            switch (kind)
            {
                case WeaponKind.Roller:
                    // 挥击是单次大动作：武器模型明显下沉，镜头推一下
                    return new WeaponFeelParams(0.075f, 5.6f, 0.050f, 0.30f, 0.90f, 1.15f, 1.15f, 1.10f, 1.25f);

                case WeaponKind.Charger:
                    // 一枪定生死：最重的镜头冲击 + 最长枪口火光
                    return new WeaponFeelParams(0.105f, 8.5f, 0.090f, 0.55f, 1.60f, 1.45f, 1.20f, 1.15f, 1.35f);

                case WeaponKind.Dualies:
                    // 12~15 发/秒：冲击必须小到不干扰连续瞄准
                    return new WeaponFeelParams(0.032f, 3.0f, 0.025f, 0.12f, 0.40f, 0.85f, 0.90f, 0.90f, 0.90f);

                case WeaponKind.Slosher:
                    return new WeaponFeelParams(0.060f, 5.0f, 0.040f, 0.22f, 0.70f, 1.10f, 1.25f, 1.15f, 1.15f);

                case WeaponKind.Blaster:
                    // 慢速重弹：后坐与镜头冲击最接近「炮」
                    return new WeaponFeelParams(0.095f, 7.5f, 0.085f, 0.50f, 1.50f, 1.40f, 1.35f, 1.20f, 1.40f);

                case WeaponKind.Splatling:
                    // 15 发/秒弹雨：几乎没有单发后坐，靠弹丸速度拉伸表达火力
                    return new WeaponFeelParams(0.018f, 1.8f, 0.012f, 0.07f, 0.22f, 0.80f, 0.85f, 0.95f, 0.75f);

                default:
                    // Spritzer：10 发/秒的基准手感
                    return new WeaponFeelParams(0.045f, 3.8f, 0.035f, 0.16f, 0.55f, 1.00f, 1.00f, 1.00f, 1.00f);
            }
        }

        /// <summary>
        /// 解析一把武器的最终手感：类别预设打底，配置里 &gt; 0 的字段覆盖。
        /// <paramref name="config"/> 为 null 时直接返回小枪预设，保证裸测试/兜底不抛异常。
        /// </summary>
        public static WeaponFeelParams Resolve(WeaponConfigSO config)
        {
            if (config == null)
                return ForKind(WeaponKind.Shooter);

            WeaponFeelParams preset = ForKind(config.weaponKind);
            return new WeaponFeelParams(
                Pick(config.recoilKickMeters, preset.RecoilKickMeters),
                Pick(config.recoilPitchDegrees, preset.RecoilPitchDegrees),
                Pick(config.cameraKickMeters, preset.CameraKickMeters),
                Pick(config.cameraKickRollDegrees, preset.CameraKickRollDegrees),
                Pick(config.cameraKickFovDegrees, preset.CameraKickFovDegrees),
                Pick(config.muzzleFlashScale, preset.MuzzleFlashScale),
                Pick(config.projectileVisualScale, preset.ProjectileVisualScale),
                Pick(config.trailVisualScale, preset.TrailVisualScale),
                Pick(config.impactRippleScale, preset.ImpactRippleScale));
        }

        private static float Pick(float overrideValue, float presetValue)
        {
            return overrideValue > 0.0001f ? overrideValue : presetValue;
        }
    }

    /// <summary>
    /// 纯数学部分（EditMode 可直接断言）：弹丸视觉拉伸与出生弹跳。
    /// </summary>
    public static class WeaponFeelMath
    {
        /// <summary>拉伸下限：低速抛物弹保持接近球形。</summary>
        public const float MinStretch = 0.9f;

        /// <summary>拉伸上限：加特林 40m/s 也不能拉成一根针。</summary>
        public const float MaxStretch = 2.6f;

        /// <summary>出生弹跳时长（秒）：从 65% 尺寸弹到 100%，读作「墨滴被挤出来」。</summary>
        public const float SpawnPopSeconds = 0.04f;

        /// <summary>
        /// 按飞行速度求沿速度方向的拉伸倍率：速度越快尾巴越长（对齐 INKWAVE 的 aShape 拉伸观感）。
        /// 21m/s 小枪约 1.5 倍，40m/s 加特林约 2.1 倍，15m/s 泼桶约 1.3 倍。
        /// </summary>
        public static float ProjectileStretch(float speed)
        {
            float s = 0.75f + Mathf.Max(0f, speed) * 0.035f;
            return Mathf.Clamp(s, MinStretch, MaxStretch);
        }

        /// <summary>出生弹跳：按已飞行距离换算时间进度，0.04s 内从 0.65 弹到 1。</summary>
        public static float SpawnPopScale(float distanceTraveled, float speed)
        {
            if (speed <= 0.01f)
                return 1f;

            float t = Mathf.Clamp01(distanceTraveled / (speed * SpawnPopSeconds));
            float eased = 1f - (1f - t) * (1f - t);
            return Mathf.Lerp(0.65f, 1f, eased);
        }

        /// <summary>
        /// 撞击涟漪的振幅：由墨斑半径决定，再乘武器手感倍率。
        /// 小滴墨（0.4m）约 0.065，大爆斑（1.2m）约 0.11，读得出「这一下更大」。
        /// </summary>
        public static float ImpactRippleAmplitude(float splatRadius, float feelScale)
        {
            float amp = 0.045f + Mathf.Clamp(splatRadius, 0f, 3f) * 0.055f;
            return amp * Mathf.Max(0.1f, feelScale);
        }
    }
}
