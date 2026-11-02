using UnityEngine;

namespace P1.AI
{
    /// <summary>
    /// Bot 难度档位（0 轻松 / 1 普通 / 2 硬核）与参数缩放。
    ///
    /// 参考 INKWAVE（inkwave-aah-research/src/config.js 的 DIFFICULTY + src/game/bots.js）：
    /// 三档只改「像人一样的枪法 / 反应 / 感知」这几个维度，行为状态机完全共用：
    ///   easy   reaction 0.55s, aimError 0.11rad, aimOmega 9,  aimTurn 7,  awareness 16
    ///   normal reaction 0.32s, aimError 0.06rad, aimOmega 13, aimTurn 10, awareness 21
    ///   hard   reaction 0.17s, aimError 0.03rad, aimOmega 18, aimTurn 14, awareness 26
    ///
    /// 本项目的 BotConfigSO 默认值已经接近 INKWAVE 的 normal/hard 之间（omega 13、反应 0.22s），
    /// 因此把「普通」定义为恒等档：不缩放、不克隆，玩家不切难度时行为与旧版完全一致。
    /// 倍率直接乘在 BotConfigSO 副本上，策划改 BotConfig.asset 后三档同步生效。
    /// </summary>
    public static class BotDifficultyMath
    {
        public const int MinLevel = 0;
        public const int MaxLevel = 2;
        public const int DefaultLevel = 1;

        public const int LevelEasy = 0;
        public const int LevelNormal = 1;
        public const int LevelHard = 2;

        /// <summary>单一难度档位的参数倍率；普通档全部为 1（恒等）。</summary>
        public struct SkillProfile
        {
            public float reactionTime;
            public float aimError;
            public float aimWander;
            public float aimAcquireOvershoot;
            public float aimSpringOmega;
            public float aimMaxTurnRate;
            public float aimIdleSpringOmega;
            public float aimIdleTurnRate;
            public float sightRange;
            public float aimTolerance;
            public float perceptionInterval;
            public float retreatHealthThreshold;
            public float bombUseChance;
            public float stealthDetectRange;
            public int specialMinEnemiesDelta;
        }

        public static int Normalize(int level)
        {
            return Mathf.Clamp(level, MinLevel, MaxLevel);
        }

        /// <summary>UI 展示名（只使用项目字集里已有的汉字，避免真机缺字回退）。</summary>
        public static string GetLabel(int level)
        {
            switch (Normalize(level))
            {
                case LevelEasy:
                    return "轻松";
                case LevelHard:
                    return "硬核";
                default:
                    return "普通";
            }
        }

        public static SkillProfile GetProfile(int level)
        {
            switch (Normalize(level))
            {
                case LevelEasy:
                    // 反应慢 70%、误差与漂移放大、弹簧更软、甩枪更慢、视野更短、感知节拍更慢；
                    // 更晚撤退（0.8 倍血线）＝更容易被打死，炸弹与大招也更保守。
                    return new SkillProfile
                    {
                        reactionTime = 1.7f,
                        aimError = 1.8f,
                        aimWander = 1.6f,
                        aimAcquireOvershoot = 1.25f,
                        aimSpringOmega = 0.7f,
                        aimMaxTurnRate = 0.68f,
                        aimIdleSpringOmega = 0.75f,
                        aimIdleTurnRate = 0.75f,
                        sightRange = 0.8f,
                        aimTolerance = 1.3f,
                        perceptionInterval = 1.6f,
                        retreatHealthThreshold = 0.8f,
                        bombUseChance = 0.6f,
                        stealthDetectRange = 0.7f,
                        specialMinEnemiesDelta = 1,
                    };

                case LevelHard:
                    // 反应快 38%、误差减半、弹簧更硬、甩枪更快、视野更远；
                    // 撤退血线抬高（更惜命）、炸弹更勤。
                    return new SkillProfile
                    {
                        reactionTime = 0.62f,
                        aimError = 0.55f,
                        aimWander = 0.65f,
                        aimAcquireOvershoot = 0.8f,
                        aimSpringOmega = 1.35f,
                        aimMaxTurnRate = 1.3f,
                        aimIdleSpringOmega = 1.15f,
                        aimIdleTurnRate = 1.15f,
                        sightRange = 1.2f,
                        aimTolerance = 0.85f,
                        perceptionInterval = 0.75f,
                        retreatHealthThreshold = 1.15f,
                        bombUseChance = 1.25f,
                        stealthDetectRange = 1.25f,
                        specialMinEnemiesDelta = 0,
                    };

                default:
                    return new SkillProfile
                    {
                        reactionTime = 1f,
                        aimError = 1f,
                        aimWander = 1f,
                        aimAcquireOvershoot = 1f,
                        aimSpringOmega = 1f,
                        aimMaxTurnRate = 1f,
                        aimIdleSpringOmega = 1f,
                        aimIdleTurnRate = 1f,
                        sightRange = 1f,
                        aimTolerance = 1f,
                        perceptionInterval = 1f,
                        retreatHealthThreshold = 1f,
                        bombUseChance = 1f,
                        stealthDetectRange = 1f,
                        specialMinEnemiesDelta = 0,
                    };
            }
        }

        /// <summary>
        /// 生成按难度缩放后的运行时配置副本；「普通」直接返回原资产（零克隆、零行为变化）。
        /// 返回的副本由调用方负责销毁（<see cref="Object.Destroy"/> / <see cref="Object.DestroyImmediate"/>）。
        /// </summary>
        public static BotConfigSO CreateRuntimeConfig(BotConfigSO source, int level)
        {
            if (source == null) return null;

            int normalized = Normalize(level);
            if (normalized == DefaultLevel) return source;

            BotConfigSO copy = Object.Instantiate(source);
            copy.name = source.name + " (" + GetLabel(normalized) + ")";
            copy.hideFlags = HideFlags.DontSave;
            ApplyTo(copy, normalized);
            return copy;
        }

        /// <summary>把难度倍率乘到配置副本上；恒等档不修改任何字段。</summary>
        public static void ApplyTo(BotConfigSO config, int level)
        {
            if (config == null) return;

            int normalized = Normalize(level);
            if (normalized == DefaultLevel) return;

            SkillProfile profile = GetProfile(normalized);

            config.reactionTime = Mathf.Max(0.05f, config.reactionTime * profile.reactionTime);
            config.aimErrorDegrees = Mathf.Max(0.1f, config.aimErrorDegrees * profile.aimError);
            config.aimWanderDegrees = Mathf.Max(0.05f, config.aimWanderDegrees * profile.aimWander);
            config.aimAcquireOvershoot = Mathf.Max(0.2f, config.aimAcquireOvershoot * profile.aimAcquireOvershoot);
            config.aimSpringOmega = Mathf.Max(1f, config.aimSpringOmega * profile.aimSpringOmega);
            config.aimMaxTurnRateDegrees = Mathf.Max(45f, config.aimMaxTurnRateDegrees * profile.aimMaxTurnRate);
            config.aimIdleSpringOmega = Mathf.Max(1f, config.aimIdleSpringOmega * profile.aimIdleSpringOmega);
            config.aimIdleTurnRateDegrees = Mathf.Max(30f, config.aimIdleTurnRateDegrees * profile.aimIdleTurnRate);
            config.sightRange = Mathf.Max(2f, config.sightRange * profile.sightRange);
            config.aimToleranceDegrees = Mathf.Max(1f, config.aimToleranceDegrees * profile.aimTolerance);
            config.perceptionInterval = Mathf.Clamp(config.perceptionInterval * profile.perceptionInterval,
                BotConfigSO.MinPerceptionInterval, BotConfigSO.MaxPerceptionInterval);
            config.retreatHealthThreshold = Mathf.Clamp(
                config.retreatHealthThreshold * profile.retreatHealthThreshold, 5f, 59f);
            config.bombUseChance = Mathf.Clamp01(config.bombUseChance * profile.bombUseChance);
            config.stealthDetectRange = Mathf.Max(0.5f, config.stealthDetectRange * profile.stealthDetectRange);
            config.specialMinEnemies = Mathf.Max(1, config.specialMinEnemies + profile.specialMinEnemiesDelta);
        }
    }
}
