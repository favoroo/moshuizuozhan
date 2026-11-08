using System;
using System.Collections.Generic;
using UnityEngine;

namespace P1.Core
{
    /// <summary>成就指标的采集口径（单局 = 结算快照；生涯 = 档案累计）。</summary>
    public enum AchievementMetric
    {
        // ---- 单局指标（读 MatchSettleContext）----
        MatchKills,          // 单局击杀数 ≥ Target
        TeamCoverage,        // 本队终场覆盖率(%) ≥ Target
        ZeroDeathWithKill,   // 零阵亡且击杀 ≥ Target
        DeathsThenWin,       // 阵亡 ≥ Target 且获胜
        MatchMaxStreak,      // 单局最高连杀（4 秒窗口多杀）≥ Target
        SpecialCasts,        // 单局大招释放次数 ≥ Target
        FirstKill,           // 拿到全场首杀（Target 恒为 1）
        EnemyCoverageBelow,  // 获胜且敌方覆盖率(%) < Target
        NarrowWinMargin,     // 获胜且双方覆盖率差(%) ≤ Target
        SingleLifeKills,     // 单条命击杀数（两次阵亡之间）≥ Target
        RevengeKills,        // 复仇击杀（击杀本局击杀过自己的人）≥ Target
        ClutchKill,          // 压哨击杀：剩余 ≤ 10 秒完成击杀（Target 恒为 1）
        PacifistWin,         // 获胜且击杀 ≤ Target（佛系粉刷）
        IdleWin,             // 获胜且个人涂地 ≤ Target ㎡（甩手掌柜）
        MatchMvp,            // 本局获得 MVP 勋章（Target 恒为 1）

        // ---- 生涯指标（读 PlayerProfile）----
        CareerMatches,
        CareerKills,
        CareerWins,
        CareerTurf,          // 单位 ㎡
        CareerSpecials,
        CareerMvp,
        CareerDeaths,        // 累计阵亡 ≥ Target（花式阵亡图鉴）
    }

    /// <summary>成就分类（成就页分节与配色用）。</summary>
    public enum AchievementCategory
    {
        Splat,    // 击杀
        Turf,     // 涂地
        Survive,  // 生存
        Special,  // 大招
        Egg,      // 彩蛋
        Career,   // 生涯
    }

    /// <summary>单局结算快照：成就判定的全部输入（成就系统不回头查场景对象）。</summary>
    public struct MatchSettleContext
    {
        public CombatantMatchStats Local;   // Kills / Deaths / TurfSquareMeters / MaxStreak / MaxLifeKills / SpecialCasts / RevengeKills / ClutchKill
        public float TeamCoveragePercent;
        public float EnemyCoveragePercent;
        public bool Won;
        public bool LocalGotFirstKill;
        public int MvpAwards;               // 本局本地玩家获得的 MVP 勋章数（0/1）
    }

    /// <summary>成就定义（只读数据；文案为 t-1 式幽默风）。</summary>
    public readonly struct AchievementDef
    {
        public readonly string Id;
        public readonly AchievementCategory Category;
        public readonly string Name;
        public readonly string Description;
        public readonly string IconName;    // UIIconLibrary 键；UI 侧取不到时隐藏 Image
        public readonly AchievementMetric Metric;
        public readonly float Target;

        public AchievementDef(string id, AchievementCategory category, string name, string description,
            string iconName, AchievementMetric metric, float target)
        {
            Id = id;
            Category = category;
            Name = name;
            Description = description;
            IconName = iconName;
            Metric = metric;
            Target = target;
        }
    }

    /// <summary>
    /// 成就目录（唯一数据源，37 条：单局 21 + 生涯 13 + 彩蛋 5）。
    /// 与 MatchAwards 同思路：纯静态内容，不走 ScriptableObject（成就是内容不是调参旋钮）。
    /// 连杀口径说明：MaxStreak 是 4 秒窗口多杀（KillFeedbackState）；「一条命」口径走 MaxLifeKills。
    /// </summary>
    public static class AchievementCatalog
    {
        public static readonly AchievementDef[] All =
        {
            // ---- 单局 · 击杀 ----
            new AchievementDef("ach_first_blood", AchievementCategory.Splat, "一血先锋",
                "全场第一枪是你开的，气势这块拿捏了", "glyph_target", AchievementMetric.FirstKill, 1f),
            new AchievementDef("ach_kill_3", AchievementCategory.Splat, "染血新手",
                "第一次让整个码头充满颜料味", "mark_splat", AchievementMetric.MatchKills, 3f),
            new AchievementDef("ach_kill_10", AchievementCategory.Splat, "双位收割",
                "10个对手倒下，你的扳机开始冒烟", "mark_splat", AchievementMetric.MatchKills, 10f),
            new AchievementDef("ach_kill_20", AchievementCategory.Splat, "二十连破",
                "对面7个Bot集体写投诉信，署名都是你", "mark_splat", AchievementMetric.MatchKills, 20f),
            new AchievementDef("ach_kill_30", AchievementCategory.Splat, "杀意泛滥",
                "计分板建议你留点活口，给队友一点表现机会", "mark_splat", AchievementMetric.MatchKills, 30f),

            // ---- 单局 · 一条命连杀（两次阵亡之间，区别于 4 秒窗口多杀）----
            new AchievementDef("ach_life_5", AchievementCategory.Splat, "一命五连",
                "从复活到下次躺下，中间塞了五个倒霉蛋", "glyph_swords", AchievementMetric.SingleLifeKills, 5f),
            new AchievementDef("ach_life_8", AchievementCategory.Splat, "这条命超神",
                "一条命杀满八个，对面复活点开始拉警戒线", "glyph_swords", AchievementMetric.SingleLifeKills, 8f),
            new AchievementDef("ach_life_10", AchievementCategory.Splat, "命硬杀穿",
                "这条命的含金量，比对面全队的希望加起来还高", "glyph_crown", AchievementMetric.SingleLifeKills, 10f),

            // ---- 单局 · 连杀窗口 / 复仇 / 压哨 ----
            new AchievementDef("ach_streak_3", AchievementCategory.Splat, "三连威慑",
                "4秒内连收三个，对面聊天框齐刷刷打出问号", "glyph_swords", AchievementMetric.MatchMaxStreak, 3f),
            new AchievementDef("ach_streak_5", AchievementCategory.Splat, "连杀狂魔",
                "4秒内五杀，这波团战你就是回放镜头本人", "glyph_swords", AchievementMetric.MatchMaxStreak, 5f),
            new AchievementDef("ach_revenge", AchievementCategory.Splat, "血债血偿",
                "上一枪的账这一枪讨回来，连本带利绝不赊账", "glyph_target", AchievementMetric.RevengeKills, 1f),
            new AchievementDef("ach_clutch", AchievementCategory.Splat, "压哨绝杀",
                "最后10秒还在收人头，裁判都想给你颁个敬业奖", "glyph_clock", AchievementMetric.ClutchKill, 1f),

            // ---- 单局 · 涂地 ----
            new AchievementDef("ach_cover_50", AchievementCategory.Turf, "半壁江山",
                "一半码头已经是你的画布", "rule_turf", AchievementMetric.TeamCoverage, 50f),
            new AchievementDef("ach_cover_70", AchievementCategory.Turf, "颜色革命",
                "对面：这是我们的地盘！你：现在不是了", "rule_turf", AchievementMetric.TeamCoverage, 70f),
            new AchievementDef("ach_cover_80", AchievementCategory.Turf, "铺满全场",
                "裁判一度以为你把颜料桶打翻在了地图上", "rule_turf", AchievementMetric.TeamCoverage, 80f),

            // ---- 单局 · 生存 ----
            new AchievementDef("ach_untouched", AchievementCategory.Survive, "毫发无损",
                "整局下来，对面连你的墨水味都没闻到", "mark_squid", AchievementMetric.ZeroDeathWithKill, 1f),
            new AchievementDef("ach_undying", AchievementCategory.Survive, "不死小强",
                "复活点都快住成家了，最后还是你赢", "mark_squid", AchievementMetric.DeathsThenWin, 5f),

            // ---- 单局 · 大招 ----
            new AchievementDef("ach_special_3", AchievementCategory.Special, "大招连发",
                "大招能量条：我到底做错了什么", "special_storm", AchievementMetric.SpecialCasts, 3f),
            new AchievementDef("ach_special_5", AchievementCategory.Special, "大招永动机",
                "能量条刚满就被你放空，人形发电机实锤", "special_storm", AchievementMetric.SpecialCasts, 5f),

            // ---- 单局 · 彩蛋 ----
            new AchievementDef("ach_shutout", AchievementCategory.Egg, "完封全场",
                "对面大概以为这局比的是心灵美", "glyph_booyah", AchievementMetric.EnemyCoverageBelow, 10f),
            new AchievementDef("ach_narrow_win", AchievementCategory.Egg, "极限险胜",
                "赢的那一点点，是你用命涂出来的", "glyph_dice", AchievementMetric.NarrowWinMargin, 2f),
            new AchievementDef("ach_mvp_single", AchievementCategory.Egg, "首座奖杯",
                "把MVP奖杯抱回家，记得先擦干净再摆进柜子", "glyph_crown", AchievementMetric.MatchMvp, 1f),
            new AchievementDef("ach_pacifist", AchievementCategory.Egg, "佛系粉刷匠",
                "几乎没开枪纯靠刷墙赢，对面的枪成了摆设", "glyph_palette", AchievementMetric.PacifistWin, 1f),
            new AchievementDef("ach_lazy_win", AchievementCategory.Egg, "甩手掌柜",
                "全场没刷出十平米照样躺赢，队友含泪领功", "glyph_booyah", AchievementMetric.IdleWin, 10f),

            // ---- 生涯 ----
            new AchievementDef("ach_career_match_10", AchievementCategory.Career, "常客",
                "来都来了，不如再来一局", "glyph_clock", AchievementMetric.CareerMatches, 10f),
            new AchievementDef("ach_career_match_50", AchievementCategory.Career, "码头钉子户",
                "码头管理员已给你留了专属车位", "glyph_map", AchievementMetric.CareerMatches, 50f),
            new AchievementDef("ach_career_match_100", AchievementCategory.Career, "码头传奇",
                "你不在的日子，机器人都在传颂你的传说", "glyph_map", AchievementMetric.CareerMatches, 100f),
            new AchievementDef("ach_career_kill_100", AchievementCategory.Career, "百人斩",
                "你的战绩可以出一本《涂地兵法》了", "mark_splat", AchievementMetric.CareerKills, 100f),
            new AchievementDef("ach_career_kill_500", AchievementCategory.Career, "五百杀手",
                "对面的Bot看见你就想请年假", "glyph_swords", AchievementMetric.CareerKills, 500f),
            new AchievementDef("ach_career_kill_1000", AchievementCategory.Career, "千人斩",
                "Bot训练营连夜把你加进教材反面案例", "mark_splat", AchievementMetric.CareerKills, 1000f),
            new AchievementDef("ach_career_win_10", AchievementCategory.Career, "小有名气",
                "领奖台不高，但你已经站上去了", "glyph_star", AchievementMetric.CareerWins, 10f),
            new AchievementDef("ach_career_win_50", AchievementCategory.Career, "胜利收藏家",
                "奖杯柜扩容计划已经排到二期", "glyph_flex", AchievementMetric.CareerWins, 50f),
            new AchievementDef("ach_career_turf_1000", AchievementCategory.Career, "大地规划师",
                "市政厅想聘你当城市涂装顾问", "rule_turf", AchievementMetric.CareerTurf, 1000f),
            new AchievementDef("ach_career_turf_5000", AchievementCategory.Career, "颜料慈善家",
                "整个码头的油漆都被你承包了", "glyph_palette", AchievementMetric.CareerTurf, 5000f),
            new AchievementDef("ach_career_special_25", AchievementCategory.Career, "必杀爱好者",
                "大招键是你身上最累的一根手指", "special_slam", AchievementMetric.CareerSpecials, 25f),
            new AchievementDef("ach_career_mvp_10", AchievementCategory.Career, "全场焦点",
                "聚光灯已经焊在你身上了", "glyph_crown", AchievementMetric.CareerMvp, 10f),
            new AchievementDef("ach_career_death_100", AchievementCategory.Career, "花式阵亡图鉴",
                "阵亡姿势已集齐一百种，就差出一本写真集", "mark_squid", AchievementMetric.CareerDeaths, 100f),
        };

        public static int TotalCount => All.Length;

        public static bool TryGet(string id, out AchievementDef def)
        {
            for (int i = 0; i < All.Length; i++)
            {
                if (All[i].Id == id)
                {
                    def = All[i];
                    return true;
                }
            }
            def = default;
            return false;
        }

        public static string GetCategoryLabel(AchievementCategory category)
        {
            switch (category)
            {
                case AchievementCategory.Splat: return "击杀";
                case AchievementCategory.Turf: return "涂地";
                case AchievementCategory.Survive: return "生存";
                case AchievementCategory.Special: return "大招";
                case AchievementCategory.Egg: return "彩蛋";
                case AchievementCategory.Career: return "生涯";
                default: return string.Empty;
            }
        }
    }

    /// <summary>
    /// 成就判定纯函数（EditMode 可直接断言，不触碰 PlayerPrefs）。
    /// 口径对齐 MatchManager：平局按 PlayerWon 的「≥ 即胜」语义处理。
    /// </summary>
    public static class AchievementEvaluator
    {
        public static bool IsCareerMetric(AchievementMetric metric)
        {
            return metric >= AchievementMetric.CareerMatches;
        }

        /// <summary>单局指标判定；生涯指标恒返回 false（走 <see cref="IsCareerDefMet"/>）。</summary>
        public static bool IsMatchDefMet(in AchievementDef def, in MatchSettleContext ctx)
        {
            switch (def.Metric)
            {
                case AchievementMetric.MatchKills:
                    return Mathf.Max(0, ctx.Local.Kills) >= def.Target;
                case AchievementMetric.TeamCoverage:
                    return Mathf.Max(0f, ctx.TeamCoveragePercent) >= def.Target - 0.05f;
                case AchievementMetric.ZeroDeathWithKill:
                    return ctx.Local.Deaths <= 0 && Mathf.Max(0, ctx.Local.Kills) >= def.Target;
                case AchievementMetric.DeathsThenWin:
                    return ctx.Won && Mathf.Max(0, ctx.Local.Deaths) >= def.Target;
                case AchievementMetric.MatchMaxStreak:
                    return Mathf.Max(0, ctx.Local.MaxStreak) >= def.Target;
                case AchievementMetric.SpecialCasts:
                    return Mathf.Max(0, ctx.Local.SpecialCasts) >= def.Target;
                case AchievementMetric.FirstKill:
                    return ctx.LocalGotFirstKill;
                case AchievementMetric.EnemyCoverageBelow:
                    return ctx.Won && Mathf.Max(0f, ctx.EnemyCoveragePercent) < def.Target;
                case AchievementMetric.NarrowWinMargin:
                    if (!ctx.Won) return false;
                    float margin = ctx.TeamCoveragePercent - ctx.EnemyCoveragePercent;
                    return margin >= 0f && margin <= def.Target;
                case AchievementMetric.SingleLifeKills:
                    return Mathf.Max(0, ctx.Local.MaxLifeKills) >= def.Target;
                case AchievementMetric.RevengeKills:
                    return Mathf.Max(0, ctx.Local.RevengeKills) >= def.Target;
                case AchievementMetric.ClutchKill:
                    return ctx.Local.ClutchKill;
                case AchievementMetric.PacifistWin:
                    return ctx.Won && Mathf.Max(0, ctx.Local.Kills) <= def.Target;
                case AchievementMetric.IdleWin:
                    return ctx.Won && !float.IsNaN(ctx.Local.TurfSquareMeters)
                        && !float.IsInfinity(ctx.Local.TurfSquareMeters)
                        && Mathf.Max(0f, ctx.Local.TurfSquareMeters) <= def.Target;
                case AchievementMetric.MatchMvp:
                    return Mathf.Max(0, ctx.MvpAwards) >= def.Target;
                default:
                    return false;
            }
        }

        /// <summary>生涯指标判定；单局指标恒返回 false。</summary>
        public static bool IsCareerDefMet(in AchievementDef def, PlayerProfile profile)
        {
            if (profile == null) return false;

            switch (def.Metric)
            {
                case AchievementMetric.CareerMatches:
                    return profile.Matches >= def.Target;
                case AchievementMetric.CareerKills:
                    return profile.TotalKills >= def.Target;
                case AchievementMetric.CareerWins:
                    return profile.Wins >= def.Target;
                case AchievementMetric.CareerTurf:
                    return profile.TotalTurf >= def.Target;
                case AchievementMetric.CareerSpecials:
                    return profile.TotalSpecials >= def.Target;
                case AchievementMetric.CareerMvp:
                    return profile.MvpCount >= def.Target;
                case AchievementMetric.CareerDeaths:
                    return profile.TotalDeaths >= def.Target;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 成就页进度（0~1）：生涯指标读档案累计；单局指标用「生涯最佳」近似
        /// （最高击杀 / 最高覆盖率 / 最高连杀 / 单命最高击杀），布尔型成就未解锁恒 0。
        /// </summary>
        public static float GetProgress01(in AchievementDef def, PlayerProfile profile)
        {
            GetProgressValues(in def, profile, out float current, out float target);
            return target > 0f ? Mathf.Clamp01(current / target) : 0f;
        }

        /// <summary>成就页进度短句（空串 = 无量化进度，仅展示条件）。</summary>
        public static string GetProgressText(in AchievementDef def, PlayerProfile profile)
        {
            GetProgressValues(in def, profile, out float current, out float target);
            if (target <= 0f) return string.Empty;

            switch (def.Metric)
            {
                case AchievementMetric.TeamCoverage:
                    return $"最高覆盖率 {current:0}% / {target:0}%";
                case AchievementMetric.CareerTurf:
                    return $"{current:0} / {target:0}㎡";
                default:
                    return $"{current:0} / {target:0}";
            }
        }

        private static void GetProgressValues(in AchievementDef def, PlayerProfile profile,
            out float current, out float target)
        {
            target = Mathf.Max(0.0001f, def.Target);
            current = 0f;
            if (profile == null) return;

            switch (def.Metric)
            {
                case AchievementMetric.MatchKills:
                    current = profile.BestMatchKills;
                    break;
                case AchievementMetric.TeamCoverage:
                    current = profile.BestTeamCoverage;
                    break;
                case AchievementMetric.MatchMaxStreak:
                    current = profile.BestMatchStreak;
                    break;
                case AchievementMetric.SingleLifeKills:
                    current = profile.BestMatchLifeKills;
                    break;
                case AchievementMetric.CareerMatches:
                    current = profile.Matches;
                    break;
                case AchievementMetric.CareerKills:
                    current = profile.TotalKills;
                    break;
                case AchievementMetric.CareerWins:
                    current = profile.Wins;
                    break;
                case AchievementMetric.CareerTurf:
                    current = profile.TotalTurf;
                    break;
                case AchievementMetric.CareerSpecials:
                    current = profile.TotalSpecials;
                    break;
                case AchievementMetric.CareerMvp:
                    current = profile.MvpCount;
                    break;
                case AchievementMetric.CareerDeaths:
                    current = profile.TotalDeaths;
                    break;
                // 布尔型（零阵亡 / 首杀 / 险胜 / 完封 / 压哨 / 复仇 / 佛系 / 甩手 / MVP）：无量化进度
                default:
                    current = 0f;
                    target = 0f;
                    break;
            }
        }
    }

    /// <summary>单局成就结算结果：新解锁 id 列表 + 新纪录标记（结算页 UI 直接消费）。</summary>
    public sealed class AchievementSettleOutcome
    {
        public readonly List<string> NewlyUnlocked = new List<string>();
        public bool NewRecordKills;
        public bool NewRecordStreak;
        public bool NewRecordTurf;
        public bool NewRecordCoverage;
    }

    /// <summary>
    /// 成就结算服务：每局结束（s_SettledThisMatch 守卫内）调用一次，
    /// 累计生涯统计 → 评定单局 + 生涯成就 → 合并解锁 → 落盘一次。
    /// </summary>
    public static class AchievementService
    {
        public static AchievementSettleOutcome SettleMatch(in MatchSettleContext ctx, int mvpAwards)
        {
            var outcome = new AchievementSettleOutcome();
            PlayerProfile profile = PlayerProfile.Current;
            if (profile == null) return outcome;

            // MVP 勋章有两条入口（ctx.MvpAwards 新 / mvpAwards 参数旧），取大者统一，
            // 避免调用方只填其一时「生涯累计了、成就没解锁」的口径分裂。
            MatchSettleContext settleCtx = ctx;
            settleCtx.MvpAwards = Mathf.Max(settleCtx.MvpAwards, Mathf.Max(0, mvpAwards));

            // 1. 记录旧最佳，用于「新纪录」角签
            int oldBestKills = profile.BestMatchKills;
            int oldBestStreak = profile.BestMatchStreak;
            float oldBestTurf = profile.BestMatchTurf;
            float oldBestCoverage = profile.BestTeamCoverage;

            // 2. 生涯累计
            profile.AccumulateCareerStats(settleCtx.Local, settleCtx.TeamCoveragePercent, settleCtx.MvpAwards);

            outcome.NewRecordKills = profile.BestMatchKills > oldBestKills;
            outcome.NewRecordStreak = profile.BestMatchStreak > oldBestStreak;
            outcome.NewRecordTurf = profile.BestMatchTurf > oldBestTurf + 0.01f;
            outcome.NewRecordCoverage = profile.BestTeamCoverage > oldBestCoverage + 0.01f;

            // 3. 成就评定（单局 + 生涯），已解锁的跳过
            AchievementDef[] all = AchievementCatalog.All;
            for (int i = 0; i < all.Length; i++)
            {
                string id = all[i].Id;
                if (profile.IsAchievementUnlocked(id)) continue;

                bool met = AchievementEvaluator.IsCareerMetric(all[i].Metric)
                    ? AchievementEvaluator.IsCareerDefMet(all[i], profile)
                    : AchievementEvaluator.IsMatchDefMet(all[i], settleCtx);
                if (met)
                {
                    outcome.NewlyUnlocked.Add(id);
                    profile.MarkAchievementUnlocked(id);
                }
            }

            // 4. 一次性落盘
            profile.Save();
            return outcome;
        }
    }
}
