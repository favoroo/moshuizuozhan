using System.Collections.Generic;
using P1.Core;
using UnityEngine;

namespace P1.UI
{
    /// <summary>首页信息浮层的两种内容。</summary>
    public enum HomeInfoTab
    {
        Record = 0,
        Controls = 1,
    }

    /// <summary>键位行的语义分组：左列移动、右列战斗。</summary>
    public enum ControlGroup
    {
        Movement = 0,
        Combat = 1,
    }

    public readonly struct ControlRow
    {
        public readonly string Key;
        public readonly string Action;
        public readonly ControlGroup Group;

        public ControlRow(string key, string action, ControlGroup group)
        {
            Key = key;
            Action = action;
            Group = group;
        }
    }

    /// <summary>战绩浮层的只读快照（不持有 PlayerProfile，避免浮层生命周期内读到半更新状态）。</summary>
    public readonly struct RecordSummary
    {
        public readonly string PlayerName;
        public readonly string RankName;
        public readonly int Level;
        public readonly int Xp;
        public readonly int XpForNextLevel;
        public readonly int XpToNextLevel;
        public readonly int Matches;
        public readonly int Wins;
        public readonly float TotalTurf;
        public readonly float TurfPerMatch;
        public readonly float WinRate01;
        public readonly float LevelProgress01;

        public bool HasMatches => Matches > 0;

        public RecordSummary(
            string playerName, string rankName,
            int level, int xp, int xpForNextLevel, int xpToNextLevel,
            int matches, int wins, float totalTurf, float turfPerMatch,
            float winRate01, float levelProgress01)
        {
            PlayerName = playerName;
            RankName = rankName;
            Level = level;
            Xp = xp;
            XpForNextLevel = xpForNextLevel;
            XpToNextLevel = xpToNextLevel;
            Matches = matches;
            Wins = wins;
            TotalTurf = totalTurf;
            TurfPerMatch = turfPerMatch;
            WinRate01 = winRate01;
            LevelProgress01 = levelProgress01;
        }
    }

    /// <summary>
    /// 首页「战绩 / 键位说明」浮层的纯内容层：
    /// - 键位表是项目唯一数据源，暂停菜单「操作说明」卡与首页浮层共用，避免文案漂移；
    /// - 战绩统计只读现有 PlayerProfile 聚合字段，不扩展存档结构；
    /// - 全部是纯函数，EditMode 可直接断言。
    /// </summary>
    public static class HomeInfoContent
    {
        private static readonly ControlRow[] DesktopRows =
        {
            new ControlRow("WASD", "移动", ControlGroup.Movement),
            new ControlRow("鼠标", "转视角", ControlGroup.Movement),
            new ControlRow("空格", "跳跃", ControlGroup.Movement),
            new ControlRow("右键 · Shift", "潜墨 · 爬墙", ControlGroup.Movement),
            new ControlRow("左键", "开火涂地", ControlGroup.Combat),
            new ControlRow("E · 中键", "投掷手雷", ControlGroup.Combat),
            new ControlRow("Q", "释放大招", ControlGroup.Combat),
            new ControlRow("Esc", "暂停 · 设置", ControlGroup.Combat),
        };

        private static readonly ControlRow[] MobileRows =
        {
            new ControlRow("摇杆", "移动", ControlGroup.Movement),
            new ControlRow("滑屏", "转视角", ControlGroup.Movement),
            new ControlRow("跳跃键", "跳跃", ControlGroup.Movement),
            new ControlRow("射击键", "开火涂地", ControlGroup.Combat),
            new ControlRow("潜墨键", "潜墨 · 爬墙", ControlGroup.Combat),
            new ControlRow("大招键", "释放大招", ControlGroup.Combat),
        };

        private static readonly string[] DesktopRules =
        {
            "涂地面积决定胜负：结算时覆盖率高的一方获胜",
            "踩到敌墨会持续掉血，先涂出一条路再推进",
            "在己方墨中潜行：快速回墨、回血，还能爬墙",
            "涂墨积攒大招能量，满槽后按 Q 释放",
        };

        private static readonly string[] MobileRules =
        {
            "涂地面积决定胜负：结算时覆盖率高的一方获胜",
            "踩到敌墨会持续掉血，先涂出一条路再推进",
            "在己方墨中潜行：快速回墨、回血，还能爬墙",
            "涂墨积攒大招能量，满槽后按大招键释放",
        };

        /// <summary>桌面 / 移动端完整键位表（顺序：移动在前、战斗在后）。</summary>
        public static IReadOnlyList<ControlRow> Controls(bool mobile)
        {
            return mobile ? MobileRows : DesktopRows;
        }

        /// <summary>按语义分组取键位行（浮层左右两栏用）。</summary>
        public static ControlRow[] RowsInGroup(bool mobile, ControlGroup group)
        {
            IReadOnlyList<ControlRow> rows = Controls(mobile);
            var result = new List<ControlRow>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Group == group) result.Add(rows[i]);
            }
            return result.ToArray();
        }

        public static string[] Rules(bool mobile)
        {
            return mobile ? MobileRules : DesktopRules;
        }

        public static string GamepadHint =>
            "手柄：左摇杆 移动 · RT 开火 · LT 潜墨 · A 跳跃 · RB 手雷 · LB 大招 · Start 暂停";

        public static string MobileLayoutHint => "右上角「自定义按键」可调整按键位置与大小";

        /// <summary>从现有档案计算战绩快照；空档案 / 脏数据全部安全回退。</summary>
        public static RecordSummary Summarize(PlayerProfile profile)
        {
            if (profile == null)
            {
                int defaultLevel = PlayerProfile.DefaultLevel;
                int defaultRequired = PlayerProfile.GetXpRequiredForLevel(defaultLevel);
                return new RecordSummary(
                    PlayerProfile.DefaultName,
                    PlayerProfile.GetRankName(defaultLevel),
                    defaultLevel,
                    PlayerProfile.DefaultXp,
                    defaultRequired,
                    defaultRequired,
                    0,
                    0,
                    0f,
                    0f,
                    0f,
                    0f);
            }

            int level = Mathf.Max(1, profile.Level);
            int xp = Mathf.Max(0, profile.Xp);
            int required = Mathf.Max(1, PlayerProfile.GetXpRequiredForLevel(level));
            int matches = Mathf.Max(0, profile.Matches);
            int wins = Mathf.Clamp(profile.Wins, 0, matches);
            float totalTurf = Mathf.Max(0f, profile.TotalTurf);
            float winRate = matches > 0 ? (float)wins / matches : 0f;
            float turfPerMatch = matches > 0 ? totalTurf / matches : 0f;
            float progress = Mathf.Clamp01((float)xp / required);
            int xpToNext = Mathf.Max(0, required - xp);

            return new RecordSummary(
                string.IsNullOrWhiteSpace(profile.Name) ? PlayerProfile.DefaultName : profile.Name,
                PlayerProfile.GetRankName(level),
                level,
                xp,
                required,
                xpToNext,
                matches,
                wins,
                totalTurf,
                turfPerMatch,
                winRate,
                progress);
        }

        /// <summary>0~1 的比率 → 整数百分比文本。</summary>
        public static string Percent(float value01)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(value01) * 100f) + "%";
        }
    }
}
