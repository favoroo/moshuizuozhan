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

        /// <summary>后坐力/镜头冲击弹簧角频率（rad/s）：越大回弹越快，连发越「震」。</summary>
        public readonly float SpringOmega;

        /// <summary>镜头冲击热积累衰减率（/s）：越小持续连发时视轴后拉越明显。</summary>
        public readonly float CameraKickDecayRate;

        /// <summary>
        /// 飞行拖尾墨雾间距（米）：弹丸每飞这么远在弹道后方留下一团低透明度队色墨雾。
        /// ≤ 0 关闭拖尾墨雾（撞击墨花 / 沿途滴墨不受影响）。
        /// </summary>
        [Tooltip("飞行拖尾墨雾间距（米）：每隔这么远留下一团低透明度队色墨雾；≤0 关闭拖尾雾")]
        public readonly float TrailPuffIntervalMeters;

        /// <summary>每团拖尾墨雾同时甩出一颗小卫星墨珠的概率（0~1，沿速度反方向）。</summary>
        [Tooltip("每团拖尾墨雾同时甩出一颗小卫星墨珠的概率（0~1，沿速度反方向）")]
        public readonly float TrailSatelliteChance;

        /// <summary>单团拖尾墨雾的存活时间（秒）：越短越像「喷溅」，越长越像「烟」。建议 0.18~0.45。</summary>
        [Tooltip("单团拖尾墨雾的存活时间（秒）：越短越像喷溅，越长越像烟雾")]
        public readonly float TrailPuffLifetime;

        /// <summary>拖尾墨雾尺寸倍率：1 = 标准（约 0.38m），重武器可放大到 1.3+。</summary>
        [Tooltip("拖尾墨雾尺寸倍率：1 = 标准（约 0.38m），重武器可放大到 1.3+")]
        public readonly float TrailPuffScale;

        private const float DefaultTrailPuffIntervalMeters = 2.5f;
        private const float DefaultTrailSatelliteChance = 0.25f;
        private const float DefaultTrailPuffLifetime = 0.30f;
        private const float DefaultTrailPuffScale = 1f;

        /// <summary>旧 9 参构造保留（跨系统调用方/测试无需迁移），弹簧参数回落小枪预设。</summary>
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
            : this(
                recoilKickMeters,
                recoilPitchDegrees,
                cameraKickMeters,
                cameraKickRollDegrees,
                cameraKickFovDegrees,
                muzzleFlashScale,
                projectileVisualScale,
                trailVisualScale,
                impactRippleScale,
                DefaultSpringOmega,
                DefaultCameraKickDecayRate)
        {
        }

        private const float DefaultSpringOmega = 30f;
        private const float DefaultCameraKickDecayRate = 8.5f;

        public WeaponFeelParams(
            float recoilKickMeters,
            float recoilPitchDegrees,
            float cameraKickMeters,
            float cameraKickRollDegrees,
            float cameraKickFovDegrees,
            float muzzleFlashScale,
            float projectileVisualScale,
            float trailVisualScale,
            float impactRippleScale,
            float springOmega,
            float cameraKickDecayRate)
            : this(
                recoilKickMeters,
                recoilPitchDegrees,
                cameraKickMeters,
                cameraKickRollDegrees,
                cameraKickFovDegrees,
                muzzleFlashScale,
                projectileVisualScale,
                trailVisualScale,
                impactRippleScale,
                springOmega,
                cameraKickDecayRate,
                DefaultTrailPuffIntervalMeters,
                DefaultTrailSatelliteChance,
                DefaultTrailPuffLifetime,
                DefaultTrailPuffScale)
        {
        }

        /// <summary>完整构造（含 P30 弹丸拖尾参数）：类别预设与配置解析都走这里。</summary>
        public WeaponFeelParams(
            float recoilKickMeters,
            float recoilPitchDegrees,
            float cameraKickMeters,
            float cameraKickRollDegrees,
            float cameraKickFovDegrees,
            float muzzleFlashScale,
            float projectileVisualScale,
            float trailVisualScale,
            float impactRippleScale,
            float springOmega,
            float cameraKickDecayRate,
            float trailPuffIntervalMeters,
            float trailSatelliteChance,
            float trailPuffLifetime,
            float trailPuffScale)
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
            SpringOmega = springOmega;
            CameraKickDecayRate = cameraKickDecayRate;
            TrailPuffIntervalMeters = Mathf.Max(0f, trailPuffIntervalMeters);
            TrailSatelliteChance = Mathf.Clamp01(trailSatelliteChance);
            TrailPuffLifetime = Mathf.Clamp(trailPuffLifetime, 0.05f, 1.2f);
            TrailPuffScale = Mathf.Clamp(trailPuffScale, 0.2f, 3f);
        }

        /// <summary>只替换拖尾参数（预设里逐个武器微调用，避免重复抄 11 个字段）。</summary>
        internal WeaponFeelParams(
            WeaponFeelParams source,
            float trailPuffIntervalMeters,
            float trailSatelliteChance,
            float trailPuffLifetime,
            float trailPuffScale)
            : this(
                source.RecoilKickMeters,
                source.RecoilPitchDegrees,
                source.CameraKickMeters,
                source.CameraKickRollDegrees,
                source.CameraKickFovDegrees,
                source.MuzzleFlashScale,
                source.ProjectileVisualScale,
                source.TrailVisualScale,
                source.ImpactRippleScale,
                source.SpringOmega,
                source.CameraKickDecayRate,
                trailPuffIntervalMeters,
                trailSatelliteChance,
                trailPuffLifetime,
                trailPuffScale)
        {
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
                    // 挥击是单次大动作：武器模型明显下沉，镜头推一下。
                    // 一次挥击会甩出 9 颗墨滴，拖尾雾间距拉大 + 卫星珠概率压低，避免扇面糊成一团。
                    return Trail(new WeaponFeelParams(0.075f, 5.6f, 0.050f, 0.30f, 0.90f, 1.15f, 1.15f, 1.10f, 1.25f, 21f, 8.0f),
                        3.2f, 0.12f, 0.26f, 0.85f);

                case WeaponKind.Charger:
                    // 一枪定生死：最重的镜头冲击 + 最长枪口火光（实际幅度按蓄力刻度缩放）。
                    // 单发重墨：拖尾最稀疏但每团最大、卫星珠概率最高，读作「一道粗墨柱」。
                    return Trail(new WeaponFeelParams(0.105f, 8.5f, 0.090f, 0.55f, 1.60f, 1.45f, 1.20f, 1.15f, 1.35f, 19f, 6.5f),
                        3.4f, 0.50f, 0.40f, 1.35f);

                case WeaponKind.Dualies:
                    // 12~15 发/秒：单发冲击小、弹簧频率高，连发读作「震」而不是「漂」。
                    // 高射速下拖尾雾间距略拉大，避免同屏弹丸叠出一串雾球。
                    return Trail(new WeaponFeelParams(0.032f, 3.0f, 0.025f, 0.12f, 0.40f, 0.85f, 0.90f, 0.90f, 0.90f, 39f, 9.0f),
                        3.0f, 0.18f, 0.26f, 0.85f);

                case WeaponKind.Slosher:
                    return Trail(new WeaponFeelParams(0.060f, 5.0f, 0.040f, 0.22f, 0.70f, 1.10f, 1.25f, 1.15f, 1.15f, 28f, 7.5f),
                        2.8f, 0.20f, 0.28f, 0.90f);

                case WeaponKind.Blaster:
                    // 慢速重弹：后坐与镜头冲击最接近「炮」，拖尾雾也最浓。
                    return Trail(new WeaponFeelParams(0.095f, 7.5f, 0.085f, 0.50f, 1.50f, 1.40f, 1.35f, 1.20f, 1.40f, 20f, 7.0f),
                        2.2f, 0.35f, 0.35f, 1.20f);

                case WeaponKind.Splatling:
                    // 15 发/秒弹雨：单发幅度仍然低，但弹簧够快 + 镜头热积累，读作「持续压制」。
                    // 弹雨太密，拖尾雾压到最稀最小，只保留一层薄薄的墨雾感。
                    return Trail(new WeaponFeelParams(0.022f, 2.2f, 0.018f, 0.07f, 0.22f, 0.80f, 0.85f, 0.95f, 0.75f, 40f, 9.0f),
                        3.0f, 0.15f, 0.24f, 0.80f);

                default:
                    // Spritzer：10 发/秒的基准手感
                    return Trail(new WeaponFeelParams(0.045f, 3.8f, 0.035f, 0.16f, 0.55f, 1.00f, 1.00f, 1.00f, 1.00f, 30f, 8.5f),
                        2.5f, 0.25f, 0.30f, 1.00f);
            }
        }

        /// <summary>给 11 参预设补上拖尾参数（只改表现层，不动后坐/镜头/涂地）。</summary>
        private static WeaponFeelParams Trail(
            WeaponFeelParams preset,
            float intervalMeters,
            float satelliteChance,
            float puffLifetime,
            float puffScale)
        {
            return new WeaponFeelParams(preset, intervalMeters, satelliteChance, puffLifetime, puffScale);
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
                Pick(config.impactRippleScale, preset.ImpactRippleScale),
                Pick(config.feelSpringOmega, preset.SpringOmega),
                Pick(config.cameraKickDecayRate, preset.CameraKickDecayRate),
                // 拖尾雾参数没有对应的配置字段（P30 新增，旧资产无需迁移），始终跟随类别预设
                preset.TrailPuffIntervalMeters,
                preset.TrailSatelliteChance,
                preset.TrailPuffLifetime,
                preset.TrailPuffScale);
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
        /// <summary>武器模型后坐力弹簧阻尼比：略低于临界，保留约 1.5% 的过冲让「这一下」更脆。</summary>
        public const float RecoilDampingRatio = 0.8f;

        /// <summary>镜头冲击弹簧阻尼比：临界阻尼，只冲不回弹，避免准星周围出现二次晃动。</summary>
        public const float CameraKickDampingRatio = 1f;

        /// <summary>
        /// 蓄力刻度 → 手感强度倍率：点按（0.12）约 0.428，满蓄 1。
        /// 对齐 INKWAVE 的 charger 后坐 `0.005 + charge*0.013` 的分层方式。
        /// </summary>
        public static float ShotStrengthScale(float strength01)
        {
            return 0.35f + 0.65f * Mathf.Clamp01(strength01);
        }

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
