using System;
using System.Collections.Generic;
using P1.Core;
using P1.Player;
using UnityEngine;

namespace P1.UI
{
    /// <summary>
    /// 结算勋章位掩码（对齐 INKWAVE 7 种核心勋章）。
    /// </summary>
    [Flags]
    public enum AwardType
    {
        None = 0,
        TurfKing = 1 << 0,       // 1: 全场涂地最多
        TopInker = 1 << 1,       // 2: 本队涂地最多 (可与 Turf King 叠加)
        TopSplatter = 1 << 2,    // 4: 全场击杀最多
        Untouchable = 1 << 3,    // 8: 全场 0 死亡 (总人数 >= 4)
        Survivor = 1 << 4,       // 16: 死亡数全场最少且 > 0 (与 Untouchable 互斥)
        PurePainter = 1 << 5,    // 32: 涂地前三且 0 击杀
        Mvp = 1 << 6             // 64: 全场最高 MVP 综合分
    }

    /// <summary>
    /// 勋章中文标题与幽默描述常量（结算页勋章条直接上屏）。
    /// </summary>
    public static class AwardConstants
    {
        public const string TurfKing = "涂地之王";
        public const string TopInker = "本队粉刷匠";
        public const string TopSplatter = "击杀王";
        public const string Untouchable = "无人能碰";
        public const string Survivor = "幸存者";
        public const string PurePainter = "纯纯艺术家";
        public const string Mvp = "本场MVP";

        public const string TurfKingDesc = "全场涂地最多，大地为你改名";
        public const string TopInkerDesc = "本队涂地最多，队友颜料成了你的陪衬";
        public const string TopSplatterDesc = "全场击杀最多，对面复活键都被按冒烟了";
        public const string UntouchableDesc = "整局零阵亡，对面连你的尾气都吃不到";
        public const string SurvivorDesc = "阵亡最少，走位风骚得让子弹怀疑人生";
        public const string PurePainterDesc = "一枪没开还进了涂地前三，和平主义者的胜利";
        public const string MvpDesc = "涂地、击杀、生存全都要，全场没有比你还全面的";
    }

    /// <summary>
    /// 参战者统计纯数据输入（供 EditMode 测试与零 GC 批处理使用）。
    /// </summary>
    public struct ContestantAwardInput
    {
        public Team Team;
        public CombatantMatchStats Stats;

        public ContestantAwardInput(Team team, CombatantMatchStats stats)
        {
            Team = team;
            Stats = stats;
        }

        public ContestantAwardInput(Team team, float turf, int kills, int deaths)
        {
            Team = team;
            Stats = new CombatantMatchStats
            {
                TurfSquareMeters = turf,
                Kills = kills,
                Deaths = deaths
            };
        }
    }

    /// <summary>
    /// 结算勋章判定纯静态计算类（纯函数，零持续 GC，EditMode 友好）：
    /// 对齐 INKWAVE 7 种勋章，由 turf/splats/deaths 直接推导。
    /// </summary>
    public static class MatchAwards
    {
        private static readonly ContestantAwardInput[] s_SharedBuffer = new ContestantAwardInput[32];

        public static readonly AwardType[] DisplayOrder = new[]
        {
            AwardType.Mvp,
            AwardType.TurfKing,
            AwardType.TopSplatter,
            AwardType.TopInker,
            AwardType.Untouchable,
            AwardType.Survivor,
            AwardType.PurePainter
        };

        /// <summary>
        /// 计算 MVP 综合得分：
        /// turf/maxTurf + 0.55*splats/maxSplats - 0.3*deaths/maxDeaths；分母为 0 时按 0 处理。
        /// </summary>
        public static float CalculateMvpScore(CombatantMatchStats stats, float maxTurf, int maxSplats, int maxDeaths)
        {
            float turf = Mathf.Max(0f, stats.TurfSquareMeters);
            int splats = Mathf.Max(0, stats.Kills);
            int deaths = Mathf.Max(0, stats.Deaths);

            float turfTerm = maxTurf > 0f ? (turf / maxTurf) : 0f;
            float splatTerm = maxSplats > 0 ? (0.55f * splats / maxSplats) : 0f;
            float deathTerm = maxDeaths > 0 ? (0.3f * deaths / maxDeaths) : 0f;

            return turfTerm + splatTerm - deathTerm;
        }

        /// <summary>
        /// 纯数据输入勋章评定（零堆内存分配）。
        /// </summary>
        public static void Evaluate(IReadOnlyList<ContestantAwardInput> contestants, AwardType[] outAwards)
        {
            if (contestants == null || outAwards == null) return;
            int count = contestants.Count;
            if (count == 0) return;

            // 1. 第一遍扫描：收集最大与最小值
            float maxTurf = 0f;
            int maxSplats = 0;
            int maxDeaths = 0;
            int minDeaths = int.MaxValue;
            float maxTeamATurf = 0f;
            float maxTeamBTurf = 0f;

            for (int i = 0; i < count; i++)
            {
                CombatantMatchStats stats = contestants[i].Stats;
                float turf = Mathf.Max(0f, stats.TurfSquareMeters);
                int kills = Mathf.Max(0, stats.Kills);
                int deaths = Mathf.Max(0, stats.Deaths);

                if (turf > maxTurf) maxTurf = turf;
                if (kills > maxSplats) maxSplats = kills;
                if (deaths > maxDeaths) maxDeaths = deaths;
                if (deaths < minDeaths) minDeaths = deaths;

                if (contestants[i].Team == Team.TeamA)
                {
                    if (turf > maxTeamATurf) maxTeamATurf = turf;
                }
                else if (contestants[i].Team == Team.TeamB)
                {
                    if (turf > maxTeamBTurf) maxTeamBTurf = turf;
                }
            }

            // 2. 第二遍扫描：计算最高 MVP 得分（全场至少有人有涂地或击杀贡献）
            float maxMvpScore = -999999f;
            bool hasContribution = (maxTurf > 0f || maxSplats > 0);
            if (hasContribution)
            {
                for (int i = 0; i < count; i++)
                {
                    float score = CalculateMvpScore(contestants[i].Stats, maxTurf, maxSplats, maxDeaths);
                    if (score > maxMvpScore) maxMvpScore = score;
                }
            }

            // 3. 第三遍扫描：为每位参战者赋予勋章掩码
            for (int i = 0; i < count; i++)
            {
                if (i >= outAwards.Length) break;

                ContestantAwardInput c = contestants[i];
                CombatantMatchStats stats = c.Stats;
                float turf = Mathf.Max(0f, stats.TurfSquareMeters);
                int kills = Mathf.Max(0, stats.Kills);
                int deaths = Mathf.Max(0, stats.Deaths);

                AwardType awards = AwardType.None;

                // Turf King：全场涂地最多 (turf > 0)
                if (maxTurf > 0f && turf >= maxTurf - 1e-4f)
                {
                    awards |= AwardType.TurfKing;
                }

                // Top Inker：本队涂地最多（可与 Turf King 叠加, turf > 0）
                float teamMax = (c.Team == Team.TeamA) ? maxTeamATurf : maxTeamBTurf;
                if (teamMax > 0f && turf >= teamMax - 1e-4f)
                {
                    awards |= AwardType.TopInker;
                }

                // Top Splatter：全场击杀最多 (kills > 0)
                if (maxSplats > 0 && kills == maxSplats)
                {
                    awards |= AwardType.TopSplatter;
                }

                // Untouchable：全场 0 死亡（总人数 >= 4 才发，避免 1v1）
                if (count >= 4 && deaths == 0)
                {
                    awards |= AwardType.Untouchable;
                }

                // Survivor：死亡数全场最少且 > 0（与 Untouchable 互斥）
                if (minDeaths > 0 && deaths == minDeaths)
                {
                    awards |= AwardType.Survivor;
                }

                // Pure Painter：涂地前三且 0 击杀 (turf > 0)
                if (turf > 0f && kills == 0)
                {
                    int strictlyGreater = 0;
                    for (int j = 0; j < count; j++)
                    {
                        if (j != i && Mathf.Max(0f, contestants[j].Stats.TurfSquareMeters) > turf + 1e-4f)
                        {
                            strictlyGreater++;
                        }
                    }
                    if (strictlyGreater < 3)
                    {
                        awards |= AwardType.PurePainter;
                    }
                }

                // MVP：全场最高分；分母为 0 时按 0 处理
                if (hasContribution)
                {
                    float score = CalculateMvpScore(stats, maxTurf, maxSplats, maxDeaths);
                    if (score >= maxMvpScore - 1e-5f)
                    {
                        awards |= AwardType.Mvp;
                    }
                }

                outAwards[i] = awards;
            }
        }

        /// <summary>
        /// 便捷重载：返回新分配的数组。
        /// </summary>
        public static AwardType[] Evaluate(IReadOnlyList<ContestantAwardInput> contestants)
        {
            if (contestants == null) return Array.Empty<AwardType>();
            var results = new AwardType[contestants.Count];
            Evaluate(contestants, results);
            return results;
        }

        /// <summary>
        /// 基于 CombatantHealth 列表与委托查询的勋章评定（零 GC 模式）。
        /// </summary>
        public static void Evaluate(
            IReadOnlyList<CombatantHealth> contestants,
            Func<CombatantHealth, CombatantMatchStats> statsLookup,
            AwardType[] outAwards)
        {
            if (contestants == null || outAwards == null || statsLookup == null) return;
            int count = contestants.Count;
            if (count == 0) return;

            ContestantAwardInput[] buffer = (count <= s_SharedBuffer.Length)
                ? s_SharedBuffer
                : new ContestantAwardInput[count];

            for (int i = 0; i < count; i++)
            {
                CombatantHealth ch = contestants[i];
                Team team = (ch != null) ? ch.Team : Team.TeamA;
                CombatantMatchStats stats = (ch != null) ? statsLookup(ch) : default;
                buffer[i] = new ContestantAwardInput(team, stats);
            }

            var segment = new ArraySegment<ContestantAwardInput>(buffer, 0, count);
            Evaluate(segment, outAwards);
        }

        /// <summary>
        /// 便捷重载：基于 CombatantHealth 列表评定并返回数组。
        /// </summary>
        public static AwardType[] Evaluate(
            IReadOnlyList<CombatantHealth> contestants,
            Func<CombatantHealth, CombatantMatchStats> statsLookup)
        {
            if (contestants == null) return Array.Empty<AwardType>();
            var results = new AwardType[contestants.Count];
            Evaluate(contestants, statsLookup, results);
            return results;
        }

        /// <summary>
        /// 计算单个 AwardType 位掩码中包含的勋章数量。
        /// </summary>
        public static int GetAwardCount(AwardType awards)
        {
            int count = 0;
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                if ((awards & DisplayOrder[i]) != 0) count++;
            }
            return count;
        }

        /// <summary>
        /// 将位掩码拆解为排序后的勋章列表写入目标数组，返回写入的数量。
        /// </summary>
        public static int ExtractAwards(AwardType awards, AwardType[] outList)
        {
            if (outList == null) return 0;
            int written = 0;
            for (int i = 0; i < DisplayOrder.Length; i++)
            {
                if ((awards & DisplayOrder[i]) != 0)
                {
                    if (written < outList.Length)
                    {
                        outList[written] = DisplayOrder[i];
                        written++;
                    }
                }
            }
            return written;
        }

        /// <summary>
        /// 获取勋章的中文标题。
        /// </summary>
        public static string GetTitle(AwardType award)
        {
            switch (award)
            {
                case AwardType.TurfKing: return AwardConstants.TurfKing;
                case AwardType.TopInker: return AwardConstants.TopInker;
                case AwardType.TopSplatter: return AwardConstants.TopSplatter;
                case AwardType.Untouchable: return AwardConstants.Untouchable;
                case AwardType.Survivor: return AwardConstants.Survivor;
                case AwardType.PurePainter: return AwardConstants.PurePainter;
                case AwardType.Mvp: return AwardConstants.Mvp;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 获取勋章的中文幽默描述。
        /// </summary>
        public static string GetDescription(AwardType award)
        {
            switch (award)
            {
                case AwardType.TurfKing: return AwardConstants.TurfKingDesc;
                case AwardType.TopInker: return AwardConstants.TopInkerDesc;
                case AwardType.TopSplatter: return AwardConstants.TopSplatterDesc;
                case AwardType.Untouchable: return AwardConstants.UntouchableDesc;
                case AwardType.Survivor: return AwardConstants.SurvivorDesc;
                case AwardType.PurePainter: return AwardConstants.PurePainterDesc;
                case AwardType.Mvp: return AwardConstants.MvpDesc;
                default: return string.Empty;
            }
        }

        /// <summary>
        /// 勋章对应几何大胆风强调色（金、银、铜）。
        /// </summary>
        public static Color GetBadgeColor(AwardType award)
        {
            switch (award)
            {
                case AwardType.Mvp:
                case AwardType.TurfKing:
                    return ComicStyle.Yellow; // 金 (#FFD400)
                case AwardType.TopSplatter:
                case AwardType.TopInker:
                    return new Color(0.88f, 0.90f, 0.95f, 1f); // 银白
                case AwardType.Untouchable:
                case AwardType.Survivor:
                case AwardType.PurePainter:
                    return new Color(0.92f, 0.58f, 0.32f, 1f); // 铜橙
                default:
                    return ComicStyle.Paper;
            }
        }
    }
}
