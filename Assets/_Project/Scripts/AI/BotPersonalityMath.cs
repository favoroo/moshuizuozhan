using UnityEngine;

namespace P1.AI
{
    /// <summary>
    /// Bot 的「风格个性」倍率：叠在难度缩放结果之上的另一层乘算。
    /// 难度（<see cref="BotDifficultyMath"/>）管硬实力（反应/枪法/感知），
    /// 个性管风格倾向（交火距离、扔雷频率、撤退血线、回墨阈值、巡涂扫速、蓄力松手点），
    /// 因此同一种武器、同一难度下的两个 Bot 也不再是复制人。
    /// 对标 INKWAVE src/game/bots.js reset() 的 per-bot 随机内部状态。
    /// 默认（未逐 Bot 抽取时）为恒等倍率：行为与旧版完全一致。
    /// </summary>
    public struct BotPersonality
    {
        public float reactionTime;
        public float aimError;
        public float aimWander;
        /// <summary>理想交火距离倍率：乘在 shooter/charger 交战比例、滚筒射程与兜底间距上。</summary>
        public float engageDistance;
        public float bombUseChance;
        public float retreatHealth;
        public float refillInk;
        public float sightRange;
        public float paintSweepSpeed;
        /// <summary>蓄力松手阈值倍率（狙击/加特林）。</summary>
        public float chargeRelease;

        /// <summary>恒等个性：全部倍率 1，等于不改变配置。</summary>
        public static BotPersonality Identity => new BotPersonality
        {
            reactionTime = 1f,
            aimError = 1f,
            aimWander = 1f,
            engageDistance = 1f,
            bombUseChance = 1f,
            retreatHealth = 1f,
            refillInk = 1f,
            sightRange = 1f,
            paintSweepSpeed = 1f,
            chargeRelease = 1f,
        };

        public bool IsIdentity()
        {
            return Mathf.Abs(reactionTime - 1f) < 0.001f
                && Mathf.Abs(aimError - 1f) < 0.001f
                && Mathf.Abs(aimWander - 1f) < 0.001f
                && Mathf.Abs(engageDistance - 1f) < 0.001f
                && Mathf.Abs(bombUseChance - 1f) < 0.001f
                && Mathf.Abs(retreatHealth - 1f) < 0.001f
                && Mathf.Abs(refillInk - 1f) < 0.001f
                && Mathf.Abs(sightRange - 1f) < 0.001f
                && Mathf.Abs(paintSweepSpeed - 1f) < 0.001f
                && Mathf.Abs(chargeRelease - 1f) < 0.001f;
        }
    }

    /// <summary>
    /// Bot 个性纯逻辑：按种子生成倍率、把倍率乘算到 BotConfigSO 副本上。
    /// 写法与收口方式对齐 <see cref="BotDifficultyMath.ApplyTo"/>：
    /// 每个字段乘算后立即钳回 BotConfigSO Inspector Range 的同款合法区间，
    /// 防止个性 × 难度两层叠乘把参数推出可用域。
    /// </summary>
    public static class BotPersonalityMath
    {
        /// <summary>按种子生成一套风格倍率（同种子同结果；倍率区间见各字段）。</summary>
        public static BotPersonality Create(int seed)
        {
            var rng = new System.Random(seed);
            return new BotPersonality
            {
                reactionTime = Range(rng, 0.75f, 1.30f),
                aimError = Range(rng, 0.60f, 1.45f),
                aimWander = Range(rng, 0.60f, 1.45f),
                engageDistance = Range(rng, 0.82f, 1.18f),
                bombUseChance = Range(rng, 0.45f, 1.15f),
                retreatHealth = Range(rng, 0.75f, 1.30f),
                refillInk = Range(rng, 0.70f, 1.30f),
                sightRange = Range(rng, 0.85f, 1.15f),
                paintSweepSpeed = Range(rng, 0.70f, 1.30f),
                chargeRelease = Range(rng, 0.95f, 1.00f),
            };
        }

        /// <summary>
        /// 把个性倍率乘到配置副本上。<b>调用方必须传入克隆件</b>，
        /// 绝不能拿共享的 BotConfig.asset 直接乘（会污染所有 Bot 与后续对局）。
        /// </summary>
        public static void ApplyTo(BotConfigSO config, in BotPersonality personality)
        {
            if (config == null) return;

            config.reactionTime = Mathf.Max(0.05f, config.reactionTime * personality.reactionTime);
            config.aimErrorDegrees = Mathf.Max(0.1f, config.aimErrorDegrees * personality.aimError);
            config.aimWanderDegrees = Mathf.Max(0.05f, config.aimWanderDegrees * personality.aimWander);

            config.shooterEngageFraction = Mathf.Clamp(
                config.shooterEngageFraction * personality.engageDistance, 0.3f, 1f);
            config.chargerEngageFraction = Mathf.Clamp(
                config.chargerEngageFraction * personality.engageDistance, 0.3f, 1f);
            config.rollerFireRange = Mathf.Clamp(
                config.rollerFireRange * personality.engageDistance, 2f, 8f);
            config.keepDistance = Mathf.Max(1.5f, config.keepDistance * personality.engageDistance);

            config.bombUseChance = Mathf.Clamp01(config.bombUseChance * personality.bombUseChance);
            config.retreatHealthThreshold = Mathf.Clamp(
                config.retreatHealthThreshold * personality.retreatHealth, 5f, 59f);
            config.refillInkThreshold = Mathf.Clamp(
                config.refillInkThreshold * personality.refillInk, 5f, 45f);
            config.sightRangeScale = Mathf.Clamp(
                config.sightRangeScale * personality.sightRange, 0.2f, 3f);
            config.paintSweepSpeed = Mathf.Max(0.2f, config.paintSweepSpeed * personality.paintSweepSpeed);

            config.chargerReleaseCharge = Mathf.Clamp(
                config.chargerReleaseCharge * personality.chargeRelease, 0.5f, 1f);
            config.splatlingReleaseCharge = Mathf.Clamp(
                config.splatlingReleaseCharge * personality.chargeRelease, 0.5f, 1f);
        }

        private static float Range(System.Random rng, float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }
    }
}
