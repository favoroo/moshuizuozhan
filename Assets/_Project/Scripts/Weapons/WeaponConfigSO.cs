using UnityEngine;

namespace P1.Weapons
{
    [CreateAssetMenu(fileName = "WeaponConfig", menuName = "P1/Weapon Config")]
    public class WeaponConfigSO : ScriptableObject
    {
        [Header("武器分类")]
        [Tooltip("决定使用哪套开火策略；默认 Spritzer（小枪）")]
        public WeaponKind weaponKind = WeaponKind.Shooter;

        [Tooltip("显示名称（HUD / 击杀卡片用）")]
        public string displayName = "Spritzer";

        [Header("主武器：标准连发喷射枪")]
        public float fireInterval = 0.1f;
        public float damage = 36f;
        public float projectileSpeed = 21f;
        public float maxRange = 7.2f;

        [Tooltip("持续开火发散到最大时的散布半角（度）")]
        public float spreadDegrees = 5.5f;

        [Tooltip("首发/静止时的散布半角（度）：初始射击高精度")]
        public float spreadMinDegrees = 1.2f;

        [Tooltip("每发增加的 Bloom 量（0~1，累加到 1 即达到最大散布）")]
        public float bloomPerShot = 0.3f;

        [Tooltip("松开扳机后从满 Bloom 恢复到首发精度所需秒数")]
        public float bloomRecoverSeconds = 0.3f;

        public float inkCostPerShot = 0.95f;
        public float projectileRadius = 0.18f;

        [Header("主武器弹道：重力与发射仰角反解")]
        [Tooltip("弹丸飞行重力加速度（m/s²）。弹道反解与逐帧飞行积分共用同一数值，改动后落点仍与准星重合")]
        public float projectileGravity = 28f;

        [Header("主武器涂地：沿途滴落 + 终点爆斑")]
        [Tooltip("从枪口起飞行多少米后开始第一次滴墨（避免把墨滴在自己脚下）")]
        public float trailStartDistance = 2.5f;

        [Tooltip("子弹每飞行多少米在下方地面滴落一个小墨点（trailEvery）；两次滴落之间会自动补间成连续墨道")]
        public float trailDropDistance = 1.05f;

        [Tooltip("沿途滴墨的半径（米）")]
        public float trailSplatRadius = 0.44f;

        [Tooltip("终点/撞击爆斑的基准半径（米）；实际半径在 Min~Max 间随机")]
        public float endSplatRadius = 1.0f;

        [Tooltip("撞击爆斑最小半径（米）")]
        public float endSplatRadiusMin = 0.85f;

        [Tooltip("撞击爆斑最大半径（米）")]
        public float endSplatRadiusMax = 1.2f;

        [Tooltip("沿入射方向的拉伸系数：1 = 圆斑，越大墨斑越长")]
        [Range(1f, 2.4f)]
        public float endSplatStretch = 1.6f;

        [Tooltip("爆斑边缘星状小射线的数量（0 = 关闭）")]
        [Range(0, 8)]
        public int endSplatRayCount = 5;

        [Header("主武器：滚筒 (Swell Roller)")]
        [Tooltip("推地墨带宽度（米）：横向圆心止于 ±(W/2 - PaintRadius)，外包络严格等于该宽度")]
        public float rollWidth = 1.9f;

        [Tooltip("滚筒前方的碾压判定长度（米）")]
        public float rollForwardReach = 1.35f;

        [Tooltip("碾压伤害：默认 140，碰触即秒杀 100 HP 目标")]
        public float rollDamage = 140f;

        [Tooltip("同一目标两次碾压伤害的最小间隔（秒）")]
        public float rollDamageInterval = 0.5f;

        [Tooltip("每推行 1 米消耗的墨水量")]
        public float rollInkPerMeter = 1.1f;

        [Tooltip("低于该墨水量时无法开始推地")]
        public float rollMinInkToStart = 0.5f;

        [Tooltip("横向每一枚墨印的半径（米）：0.34 时 5 枚圆印拼出平整的 1.9m 墨带")]
        public float rollPaintRadius = 0.34f;

        [Tooltip("挥击间隔（秒，从按下扳机算起）")]
        public float flickInterval = 0.62f;

        [Tooltip("挥击前摇（秒）")]
        public float flickWindup = 0.22f;

        [Tooltip("一次挥击消耗的墨水量")]
        public float flickInkCost = 9f;

        [Tooltip("一次挥击抛出的墨滴数量（扇形）")]
        public int flickDrops = 9;

        [Tooltip("挥击近距离伤害（米）")]
        public float flickDamageNear = 125f;

        [Tooltip("挥击最远距离伤害")]
        public float flickDamageFar = 30f;

        [Tooltip("开始向远距离伤害衰减的飞行距离（米）")]
        public float flickFalloffStart = 1.5f;

        [Tooltip("挥击墨滴初速（m/s）")]
        public float flickSpeed = 17f;

        [Tooltip("挥击墨滴重力（m/s²）")]
        public float flickGravity = 26f;

        [Tooltip("挥击抛射仰角（度）")]
        public float flickPitchDegrees = 18f;

        [Tooltip("扇形总张角（度）")]
        public float flickSpreadDegrees = 34f;

        [Tooltip("挥击墨滴的最大飞行距离（米）")]
        public float flickRange = 8.5f;

        [Tooltip("挥击墨滴的命中半径（米）")]
        public float flickHitRadius = 0.85f;

        [Header("主武器：蓄力狙 (Glint Charger)")]
        [Tooltip("从按下到满蓄力所需时间（秒）")]
        public float chargeTime = 1.0f;

        [Tooltip("最短蓄力射程（米）")]
        public float chargeRangeMin = 11f;

        [Tooltip("满蓄力射程（米）")]
        public float chargeRangeMax = 27f;

        [Tooltip("最短蓄力直击伤害")]
        public float chargeDamageMin = 40f;

        [Tooltip("满蓄力直击伤害：160 一枪秒杀")]
        public float chargeDamageMax = 160f;

        [Tooltip("满蓄力消耗的墨水量（蓄力过程中按比例结算）")]
        public float chargeInkCostFull = 18f;

        [Tooltip("起手蓄力所需的最低墨水比例（0~1）")]
        [Range(0f, 1f)]
        public float chargeMinInkFraction = 0.2f;

        [Tooltip("沿光束每隔多少米在地面补一个墨点")]
        public float chargeLineSplatEvery = 1.2f;

        [Tooltip("光束地面墨线的基础半径（米），随蓄力放大到 1.2 倍")]
        public float chargeLineRadius = 0.55f;

        [Tooltip("光束终点撞击墨斑的基础半径（米）")]
        public float chargeImpactRadius = 1.2f;

        [Tooltip("发射后的硬直（秒）")]
        public float chargeReleaseCooldown = 0.28f;

        [Header("主武器：双枪 (Twinfin Dualies)")]
        [Tooltip("战术翻滚耗墨量")]
        public float dualiesRollInkCost = 7f;

        [Tooltip("翻滚持续时间（秒）")]
        public float dualiesRollDuration = 0.3f;

        [Tooltip("翻滚位移距离（米）")]
        public float dualiesRollDistance = 2.8f;

        [Tooltip("最大连续翻滚次数")]
        public int dualiesMaxRolls = 2;

        [Tooltip("翻滚后原地站桩架枪持续时间（秒）")]
        public float dualiesTurretDuration = 0.5f;

        [Tooltip("架枪状态下的开火间隔（秒）：射速提升 20%")]
        public float dualiesTurretFireInterval = 0.0664f;

        [Tooltip("架枪状态下的散布半角（度）：弹道散布清零")]
        public float dualiesTurretSpreadDegrees = 0f;

        [Tooltip("双手交替射击时枪口相对中心左右横向偏移量（米）")]
        public float dualiesMuzzleOffset = 0.22f;

        [Header("主武器：泼桶 (Tidebucket Slosher)")]
        [Tooltip("泼墨前摇时间（秒）")]
        public float slosherWindup = 0.13f;

        [Tooltip("一次泼出的墨滴总数量")]
        public int slosherDrops = 8;

        [Tooltip("头滴直击基准伤害：70 伤，确保 2 桶稳定击杀")]
        public float slosherHeadDamage = 70f;

        [Tooltip("尾滴基准伤害")]
        public float slosherTailDamage = 34f;

        [Tooltip("头滴落地溅射半径（米）")]
        public float slosherSplashRadius = 1.1f;

        [Tooltip("头滴落地溅射伤害")]
        public float slosherSplashDamage = 26f;

        [Tooltip("抛射仰角（度）")]
        public float slosherPitchDegrees = 18f;

        [Tooltip("墨滴依次泼出的微小时间差（秒）")]
        public float slosherDropDelay = 0.012f;

        [Header("主武器：爆破枪 (Popper Blaster)")]
        [Tooltip("空爆范围溅射半径（米）")]
        public float blasterSplashRadius = 2.6f;

        [Tooltip("中心溅射最高伤害")]
        public float blasterSplashDamageMax = 70f;

        [Tooltip("边缘溅射最低伤害")]
        public float blasterSplashDamageMin = 30f;

        [Tooltip("爆裂视觉与地面爆斑半径（米）")]
        public float blasterBurstRadius = 1.9f;

        [Header("主武器：加特林 (Gyre Splatling)")]
        [Tooltip("蓄满所需时间（秒）")]
        public float splatlingChargeTime = 0.85f;

        [Tooltip("最短爆发持续时间（秒）")]
        public float splatlingBurstMinDuration = 0.3f;

        [Tooltip("满蓄力爆发持续时间（秒）")]
        public float splatlingBurstMaxDuration = 1.7f;

        [Tooltip("蓄力时移动速度（米/秒）")]
        public float splatlingMoveSpeedCharging = 2.4f;

        [Tooltip("扫射时移动速度（米/秒）")]
        public float splatlingMoveSpeedFiring = 3.4f;

        [Header("副武器：抛物线延时墨水手雷 (Splat Bomb)")]
        [Tooltip("投掷一次消耗的墨水量：70% 墨水罐（满罐 100）")]
        public float bombInkCost = 70f;
        public float bombCooldown = 0.6f;
        public float bombMaxThrowDistance = 7.5f;
        public float bombArcHeight = 2.6f;
        public float bombFlightDuration = 0.38f;
        [Tooltip("落地后的警报引信时间（秒）：0.95s")]
        public float bombFuseDelay = 0.95f;
        [Tooltip("爆炸涂地半径（米）：2.7m")]
        public float bombSplatRadius = 2.7f;
        [Tooltip("爆炸伤害判定半径（米）：3.1m")]
        public float bombDamageRadius = 3.1f;
        [Tooltip("核心秒杀半径（米）：圈内 180 伤")]
        public float bombInnerKillRadius = 1.2f;
        public float bombInnerDamage = 180f;
        [Tooltip("外缘伤害：圈外按平方曲线衰减到 35")]
        public float bombOuterDamage = 35f;

        [Header("大招：类型选择（P3-B）")]
        [Tooltip("Slam = Tidal Slam 超级降落；Storm = Ink Tempest 墨水风暴云。由当前装备的主武器决定")]
        public SpecialKind specialKind = SpecialKind.Slam;

        [Header("大招：Tidal Slam 超级降落")]
        [Tooltip("攒满大招所需的涂地积分（每新涂/改写 1 格得 1 分）")]
        public float specialPointsRequired = 180f;
        [Tooltip("起跳升空时间（秒）：0.55s，期间无敌")]
        public float specialWindupDuration = 0.55f;
        [Tooltip("顶点滞空时间（秒）")]
        public float specialHangDuration = 0.25f;
        [Tooltip("下砸落地时间（秒）：越短冲击感越强")]
        public float specialFallDuration = 0.1f;
        [Tooltip("起跳高度（米）")]
        public float specialJumpHeight = 4.2f;
        [Tooltip("冲击波伤害判定半径（米）：5.2m")]
        public float specialSplatRadius = 5.2f;
        [Tooltip("落地巨大墨斑半径（米）：5m")]
        public float specialPaintRadius = 5.0f;
        [Tooltip("核心秒杀半径（米）：圈内 180 伤")]
        public float specialInnerKillRadius = 3.2f;
        public float specialInnerDamage = 180f;
        [Tooltip("外缘伤害：55")]
        public float specialOuterDamage = 55f;
        [Tooltip("核心圈外侧的伤害台阶权重：0.3 时刚出核心圈仍有 92.5 伤")]
        [Range(0f, 1f)]
        public float specialOuterFalloffWeight = 0.3f;
        [Tooltip("落地震屏 Trauma")]
        public float specialImpactTrauma = 1f;

        [Header("大招：Ink Tempest 墨水风暴云")]
        [Tooltip("云层存在时间（秒）：6.5s")]
        public float stormDuration = 6.5f;
        [Tooltip("降雨/伤害半径（米）：3.4m")]
        public float stormRadius = 3.4f;
        [Tooltip("对范围内敌方每秒造成的持续伤害：34 DPS")]
        public float stormDps = 34f;
        [Tooltip("云层水平漂移速度（米/秒）")]
        public float stormDriftSpeed = 1.1f;
        [Tooltip("云层悬浮高度（米）")]
        public float stormCloudHeight = 4.0f;
        [Tooltip("发生器最远投掷距离（米）")]
        public float stormThrowDistance = 9f;
        [Tooltip("发生器飞行时间（秒）")]
        public float stormThrowFlightDuration = 0.45f;
        [Tooltip("发生器抛物线弧高（米）")]
        public float stormThrowArcHeight = 3.2f;
        [Tooltip("雨点生成间隔（秒）")]
        public float stormRainInterval = 0.06f;
        [Tooltip("单颗雨点的最小涂地半径（米）")]
        public float stormRainSplatRadiusMin = 0.42f;
        [Tooltip("单颗雨点的最大涂地半径（米）")]
        public float stormRainSplatRadiusMax = 0.72f;
        [Tooltip("DOT 结算间隔（秒）：34 DPS 按 0.5s 一次结算 17 点")]
        public float stormDamageTickInterval = 0.5f;
        [Tooltip("云层展开时间（秒）")]
        public float stormGrowTime = 0.5f;
        [Tooltip("云层消散时间（秒）")]
        public float stormFadeTime = 0.6f;
        [Tooltip("释放后锁枪/锁定移动的投掷前摇（秒）")]
        public float stormThrowLockDuration = 0.35f;

        private void OnValidate()
        {
            fireInterval = Mathf.Max(0.02f, fireInterval);
            spreadMinDegrees = Mathf.Max(0f, spreadMinDegrees);
            spreadDegrees = Mathf.Max(spreadMinDegrees, spreadDegrees);
            bloomRecoverSeconds = Mathf.Max(0.05f, bloomRecoverSeconds);

            rollWidth = Mathf.Max(0.2f, rollWidth);
            rollForwardReach = Mathf.Max(0.1f, rollForwardReach);
            rollPaintRadius = Mathf.Max(0.05f, rollPaintRadius);
            flickDrops = Mathf.Max(1, flickDrops);
            flickWindup = Mathf.Max(0f, flickWindup);
            flickInterval = Mathf.Max(flickWindup, flickInterval);

            chargeTime = Mathf.Max(0.05f, chargeTime);
            chargeRangeMax = Mathf.Max(chargeRangeMin, chargeRangeMax);
            chargeDamageMax = Mathf.Max(chargeDamageMin, chargeDamageMax);
            chargeLineSplatEvery = Mathf.Max(0.2f, chargeLineSplatEvery);

            dualiesRollDuration = Mathf.Max(0.05f, dualiesRollDuration);
            dualiesTurretDuration = Mathf.Max(0.05f, dualiesTurretDuration);
            dualiesMaxRolls = Mathf.Max(1, dualiesMaxRolls);
            dualiesTurretFireInterval = Mathf.Max(0.02f, dualiesTurretFireInterval);

            slosherDrops = Mathf.Max(1, slosherDrops);
            slosherWindup = Mathf.Max(0f, slosherWindup);
            slosherSplashRadius = Mathf.Max(0.1f, slosherSplashRadius);

            blasterSplashRadius = Mathf.Max(0.1f, blasterSplashRadius);
            blasterBurstRadius = Mathf.Max(0.1f, blasterBurstRadius);

            splatlingChargeTime = Mathf.Max(0.05f, splatlingChargeTime);
            splatlingBurstMaxDuration = Mathf.Max(splatlingBurstMinDuration, splatlingBurstMaxDuration);

            bombFuseDelay = Mathf.Max(0.05f, bombFuseDelay);
            bombDamageRadius = Mathf.Max(bombSplatRadius, bombDamageRadius);
            bombInnerKillRadius = Mathf.Clamp(bombInnerKillRadius, 0.1f, bombDamageRadius);

            specialWindupDuration = Mathf.Max(0.05f, specialWindupDuration);
            specialHangDuration = Mathf.Max(0f, specialHangDuration);
            specialFallDuration = Mathf.Max(0.03f, specialFallDuration);
            specialPaintRadius = Mathf.Max(0.5f, specialPaintRadius);
            specialSplatRadius = Mathf.Max(specialInnerKillRadius, specialSplatRadius);
            specialOuterFalloffWeight = Mathf.Clamp01(specialOuterFalloffWeight);

            stormDuration = Mathf.Max(0.5f, stormDuration);
            stormDamageTickInterval = Mathf.Max(0.05f, stormDamageTickInterval);
            stormRainInterval = Mathf.Max(0.012f, stormRainInterval);
            stormRainSplatRadiusMax = Mathf.Max(stormRainSplatRadiusMin, stormRainSplatRadiusMax);
            stormGrowTime = Mathf.Max(0.05f, stormGrowTime);
            stormFadeTime = Mathf.Max(0.05f, stormFadeTime);
        }

#if UNITY_EDITOR
        /// <summary>
        /// 确定性生成/同步首发三武器配置：
        /// 1. 旧 WeaponConfig.asset 同步为 Spritzer 规范（默认配备，向后兼容 GameConfigSO）；
        /// 2. ScriptableObjects/Weapons/ 下生成 SwellRoller / GlintCharger 两份独立配置，供 EquipWeapon 切换。
        /// 菜单：P1/构建/5. 生成首发三武器配置 (Weapons)
        /// </summary>
        [UnityEditor.MenuItem("P1/构建/5. 生成首发三武器配置 (Weapons)", false, 14)]
        public static void GenerateLoadoutAssets()
        {
            GenerateAllSevenWeaponsAssets();
        }

        /// <summary>
        /// 确定性生成/同步全部 7 类主武器配置（小枪/滚筒/蓄力狙/双枪/泼桶/爆破枪/加特林）。
        /// 菜单：P1/构建/6. 生成全部7类主武器配置 (Weapons)
        /// </summary>
        [UnityEditor.MenuItem("P1/构建/6. 生成全部7类主武器配置 (Weapons)", false, 15)]
        public static void GenerateAllSevenWeaponsAssets()
        {
            const string runtimeFolder = "Assets/_Project/ScriptableObjects";
            const string weaponsFolder = runtimeFolder + "/Weapons";

            if (!UnityEditor.AssetDatabase.IsValidFolder(weaponsFolder))
            {
                UnityEditor.AssetDatabase.CreateFolder(runtimeFolder, "Weapons");
            }

            // 1. Spritzer (小枪)
            WeaponConfigSO spritzer = LoadOrCreate($"{runtimeFolder}/WeaponConfig.asset");
            ApplySpritzerSpec(spritzer);

            // 2. Swell Roller (滚筒)
            WeaponConfigSO roller = LoadOrCreate($"{weaponsFolder}/Weapon_SwellRoller.asset");
            ApplySpritzerSpec(roller);
            roller.weaponKind = WeaponKind.Roller;
            roller.displayName = "Swell Roller";
            roller.maxRange = 6f;
            UnityEditor.EditorUtility.SetDirty(roller);

            // 3. Glint Charger (蓄力狙)
            WeaponConfigSO charger = LoadOrCreate($"{weaponsFolder}/Weapon_GlintCharger.asset");
            ApplySpritzerSpec(charger);
            charger.weaponKind = WeaponKind.Charger;
            charger.displayName = "Glint Charger";
            charger.maxRange = charger.chargeRangeMax;
            UnityEditor.EditorUtility.SetDirty(charger);

            // 4. Twinfin Dualies (双枪)
            WeaponConfigSO dualies = LoadOrCreate($"{weaponsFolder}/Weapon_TwinfinDualies.asset");
            ApplySpritzerSpec(dualies);
            ApplyDualiesSpec(dualies);

            // 5. Tidebucket Slosher (泼桶)
            WeaponConfigSO slosher = LoadOrCreate($"{weaponsFolder}/Weapon_TidebucketSlosher.asset");
            ApplySpritzerSpec(slosher);
            ApplySlosherSpec(slosher);

            // 6. Popper Blaster (爆破枪)
            WeaponConfigSO blaster = LoadOrCreate($"{weaponsFolder}/Weapon_PopperBlaster.asset");
            ApplySpritzerSpec(blaster);
            ApplyBlasterSpec(blaster);

            // 7. Gyre Splatling (加特林)
            WeaponConfigSO splatling = LoadOrCreate($"{weaponsFolder}/Weapon_GyreSplatling.asset");
            ApplySpritzerSpec(splatling);
            ApplySplatlingSpec(splatling);

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log("[WeaponConfigSO] 全部7类主武器配置已就绪：Spritzer / Roller / Charger / Dualies / Slosher / Blaster / Splatling");
        }

        public static void ApplyDualiesSpec(WeaponConfigSO config)
        {
            config.weaponKind = WeaponKind.Dualies;
            config.displayName = "Twinfin Dualies";
            config.fireInterval = 0.083f;
            config.damage = 30f;
            config.inkCostPerShot = 0.85f;
            config.projectileSpeed = 32f;
            config.maxRange = 11f;
            config.spreadMinDegrees = 0.5f;
            config.spreadDegrees = 6.5f;
            config.bloomPerShot = 0.25f;
            config.bloomRecoverSeconds = 0.28f;
            config.trailStartDistance = 2.0f;
            config.trailDropDistance = 1.15f;
            config.trailSplatRadius = 0.38f;
            config.endSplatRadiusMin = 0.65f;
            config.endSplatRadiusMax = 0.85f;
            config.endSplatRadius = 0.75f;

            config.dualiesRollInkCost = 7f;
            config.dualiesRollDuration = 0.3f;
            config.dualiesRollDistance = 2.8f;
            config.dualiesMaxRolls = 2;
            config.dualiesTurretDuration = 0.5f;
            config.dualiesTurretFireInterval = 0.0664f; // 射速提升 20%
            config.dualiesTurretSpreadDegrees = 0f;    // 散布清零
            config.dualiesMuzzleOffset = 0.22f;
            UnityEditor.EditorUtility.SetDirty(config);
        }

        public static void ApplySlosherSpec(WeaponConfigSO config)
        {
            config.weaponKind = WeaponKind.Slosher;
            config.displayName = "Tidebucket Slosher";
            config.fireInterval = 0.62f;
            config.damage = 70f;
            config.inkCostPerShot = 7.5f;
            config.projectileSpeed = 15f;
            config.projectileGravity = 22f;
            config.maxRange = 9.5f;
            config.slosherWindup = 0.13f;
            config.slosherDrops = 8;
            config.slosherHeadDamage = 70f;
            config.slosherTailDamage = 34f;
            config.slosherSplashRadius = 1.1f;
            config.slosherSplashDamage = 26f;
            config.slosherPitchDegrees = 18f;
            config.slosherDropDelay = 0.012f;
            config.trailDropDistance = 1.4f;
            config.trailSplatRadius = 0.5f;
            config.endSplatRadius = 1.05f;
            config.endSplatRadiusMin = 0.95f;
            config.endSplatRadiusMax = 1.2f;
            UnityEditor.EditorUtility.SetDirty(config);
        }

        public static void ApplyBlasterSpec(WeaponConfigSO config)
        {
            config.weaponKind = WeaponKind.Blaster;
            config.displayName = "Popper Blaster";
            config.fireInterval = 0.78f;
            config.damage = 125f; // 直击秒杀
            config.inkCostPerShot = 9f;
            config.projectileSpeed = 23f;
            config.projectileGravity = 0f; // 直线飞行空爆
            config.maxRange = 10.5f;
            config.spreadMinDegrees = 1.2f;
            config.spreadDegrees = 1.2f;
            config.blasterSplashRadius = 2.6f;
            config.blasterSplashDamageMax = 70f;
            config.blasterSplashDamageMin = 30f;
            config.blasterBurstRadius = 1.9f;
            config.endSplatRadius = 1.5f;
            config.endSplatRadiusMin = 1.3f;
            config.endSplatRadiusMax = 1.7f;
            UnityEditor.EditorUtility.SetDirty(config);
        }

        public static void ApplySplatlingSpec(WeaponConfigSO config)
        {
            config.weaponKind = WeaponKind.Splatling;
            config.displayName = "Gyre Splatling";
            config.fireInterval = 0.066f; // 15发/秒
            config.damage = 28f;
            config.inkCostPerShot = 0.6f;
            config.projectileSpeed = 40f;
            config.maxRange = 15f;
            config.spreadMinDegrees = 0.6f;
            config.spreadDegrees = 3.2f;
            config.bloomPerShot = 0.05f;
            config.bloomRecoverSeconds = 0.28f;
            config.trailDropDistance = 1.2f;
            config.trailSplatRadius = 0.42f;
            config.endSplatRadius = 0.8f;
            config.endSplatRadiusMin = 0.7f;
            config.endSplatRadiusMax = 0.95f;
            config.splatlingChargeTime = 0.85f;
            config.splatlingBurstMinDuration = 0.3f;
            config.splatlingBurstMaxDuration = 1.7f;
            config.splatlingMoveSpeedCharging = 2.4f;
            config.splatlingMoveSpeedFiring = 3.4f;
            UnityEditor.EditorUtility.SetDirty(config);
        }

        private static WeaponConfigSO LoadOrCreate(string path)
        {
            WeaponConfigSO asset = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponConfigSO>(path);
            if (asset != null)
                return asset;

            asset = CreateInstance<WeaponConfigSO>();
            UnityEditor.AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>
        /// P3-B：只同步「副武器手雷 + 双大招」相关字段，绝不触碰各武器的射击参数（避免覆盖 P2-B/P3-A 的调优）。
        /// 大招按 Inkwave 配置分配：小枪/滚筒/双枪/泼桶 → Tidal Slam；蓄力狙/爆破枪/加特林 → Ink Tempest。
        /// 菜单：P1/构建/7. 同步大招与副武器配置 (Specials)
        /// </summary>
        [UnityEditor.MenuItem("P1/构建/7. 同步大招与副武器配置 (Specials)", false, 16)]
        public static void SyncSpecialAndSubWeaponConfigs()
        {
            const string runtimeFolder = "Assets/_Project/ScriptableObjects";
            const string weaponsFolder = runtimeFolder + "/Weapons";

            string[] paths =
            {
                $"{runtimeFolder}/WeaponConfig.asset",
                $"{weaponsFolder}/Weapon_SwellRoller.asset",
                $"{weaponsFolder}/Weapon_GlintCharger.asset",
                $"{weaponsFolder}/Weapon_TwinfinDualies.asset",
                $"{weaponsFolder}/Weapon_TidebucketSlosher.asset",
                $"{weaponsFolder}/Weapon_PopperBlaster.asset",
                $"{weaponsFolder}/Weapon_GyreSplatling.asset"
            };
            SpecialKind[] kinds =
            {
                SpecialKind.Slam, SpecialKind.Slam, SpecialKind.Storm, SpecialKind.Slam,
                SpecialKind.Slam, SpecialKind.Storm, SpecialKind.Storm
            };
            float[] specialCosts = { 190f, 170f, 180f, 180f, 175f, 180f, 195f };

            int applied = 0;
            for (int i = 0; i < paths.Length; i++)
            {
                WeaponConfigSO config = UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponConfigSO>(paths[i]);
                if (config == null)
                    continue;

                ApplyBombSpec(config);
                ApplySlamSpec(config);
                ApplyStormSpec(config);
                config.specialKind = kinds[i];
                config.specialPointsRequired = specialCosts[i];
                UnityEditor.EditorUtility.SetDirty(config);
                applied++;
            }

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"[WeaponConfigSO] P3-B 大招/副武器配置已同步到 {applied} 份武器资产");
        }

        /// <summary>副武器 Splat Bomb 规范：耗墨 70、落地 0.95s 引信、核心 180 秒杀、涂地 2.7m。</summary>
        public static void ApplyBombSpec(WeaponConfigSO config)
        {
            config.bombInkCost = 70f;
            config.bombCooldown = 0.6f;
            config.bombMaxThrowDistance = 7.5f;
            config.bombArcHeight = 2.6f;
            config.bombFlightDuration = 0.38f;
            config.bombFuseDelay = 0.95f;
            config.bombSplatRadius = 2.7f;
            config.bombDamageRadius = 3.1f;
            config.bombInnerKillRadius = 1.2f;
            config.bombInnerDamage = 180f;
            config.bombOuterDamage = 35f;
        }

        /// <summary>Tidal Slam 规范：0.55s 升空 + 0.25s 滞空 + 0.1s 下砸；5.2m 冲击波，核心 3.2m 180 伤，外缘 55。</summary>
        public static void ApplySlamSpec(WeaponConfigSO config)
        {
            config.specialWindupDuration = 0.55f;
            config.specialHangDuration = 0.25f;
            config.specialFallDuration = 0.1f;
            config.specialJumpHeight = 4.2f;
            config.specialSplatRadius = 5.2f;
            config.specialPaintRadius = 5.0f;
            config.specialInnerKillRadius = 3.2f;
            config.specialInnerDamage = 180f;
            config.specialOuterDamage = 55f;
            config.specialOuterFalloffWeight = 0.3f;
            config.specialImpactTrauma = 1f;
        }

        /// <summary>Ink Tempest 规范：持续 6.5s / 半径 3.4m / 34 DPS / 漂移 1.1m/s。</summary>
        public static void ApplyStormSpec(WeaponConfigSO config)
        {
            config.stormDuration = 6.5f;
            config.stormRadius = 3.4f;
            config.stormDps = 34f;
            config.stormDriftSpeed = 1.1f;
            config.stormCloudHeight = 4.0f;
            config.stormThrowDistance = 9f;
            config.stormThrowFlightDuration = 0.45f;
            config.stormThrowArcHeight = 3.2f;
            config.stormRainInterval = 0.06f;
            config.stormRainSplatRadiusMin = 0.42f;
            config.stormRainSplatRadiusMax = 0.72f;
            config.stormDamageTickInterval = 0.5f;
            config.stormGrowTime = 0.5f;
            config.stormFadeTime = 0.6f;
            config.stormThrowLockDuration = 0.35f;
        }

        private static void ApplySpritzerSpec(WeaponConfigSO config)
        {
            config.weaponKind = WeaponKind.Shooter;
            config.displayName = "Spritzer";
            config.fireInterval = 0.1f;
            config.damage = 36f;
            config.inkCostPerShot = 0.95f;
            config.spreadMinDegrees = 1.2f;
            config.spreadDegrees = 5.5f;
            config.bloomPerShot = 0.3f;
            config.bloomRecoverSeconds = 0.3f;
            config.trailStartDistance = 2.5f;
            config.trailDropDistance = 1.05f;
            config.trailSplatRadius = 0.44f;
            UnityEditor.EditorUtility.SetDirty(config);
        }
#endif
    }
}
