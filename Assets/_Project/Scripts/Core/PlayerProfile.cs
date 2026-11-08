using System;
using System.Collections.Generic;
using UnityEngine;

namespace P1.Core
{
    /// <summary>
    /// 本地玩家单局结算结果与 XP 跨级信息。
    /// </summary>
    public struct ProfileMatchResult
    {
        public int XpGained;
        public int LevelsGained;
        public int OldLevel;
        public int NewLevel;
        public int OldXp;
        public int NewXp;
        public int XpRequiredForNextLevel;
        public bool DidLevelUp => LevelsGained > 0;
    }

    /// <summary>
    /// 本地玩家档案与段位成长系统（对齐 INKWAVE 等级曲线与 5 大段位）：
    /// - PlayerPrefs 持久化，键前缀 "P1.Profile."；
    /// - 损坏/非法存档安全回退默认值，绝不抛错；
    /// - 静态单例缓存，避免每帧/每次读取访问 PlayerPrefs；
    /// - 升到下一级所需 XP：800 + Level * 350；支持跨级计算；
    /// - 单局 XP 计算公式：胜 1200 / 负 500 + TurfSquareMeters * 1 + Kills * 40。
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
    public const string KeyPrefix = "P1.Profile.";
    public const string KeyName = KeyPrefix + "Name";
    public const string KeyLevel = KeyPrefix + "Level";
    public const string KeyXp = KeyPrefix + "Xp";
    public const string KeyWins = KeyPrefix + "Wins";
    public const string KeyMatches = KeyPrefix + "Matches";
    public const string KeyTotalTurf = KeyPrefix + "TotalTurf";
    public const string KeyTotalKills = KeyPrefix + "TotalKills";
    public const string KeyTotalDeaths = KeyPrefix + "TotalDeaths";
    public const string KeyTotalSpecials = KeyPrefix + "TotalSpecials";
    public const string KeyBestMatchKills = KeyPrefix + "BestMatchKills";
    public const string KeyBestMatchStreak = KeyPrefix + "BestMatchStreak";
    public const string KeyBestMatchLifeKills = KeyPrefix + "BestMatchLifeKills";
    public const string KeyBestTeamCoverage = KeyPrefix + "BestTeamCoverage";
    public const string KeyBestMatchTurf = KeyPrefix + "BestMatchTurf";
    public const string KeyMvpCount = KeyPrefix + "MvpCount";
    public const string KeyAchievements = KeyPrefix + "Achievements";

    public const string DefaultName = "INKLING";
    public const int DefaultLevel = 1;
    public const int DefaultXp = 0;
    public const int DefaultWins = 0;
    public const int DefaultMatches = 0;
    public const float DefaultTotalTurf = 0f;
    public const int DefaultTotalKills = 0;
    public const int DefaultTotalDeaths = 0;
    public const int DefaultTotalSpecials = 0;
    public const int DefaultBestMatchKills = 0;
    public const int DefaultBestMatchStreak = 0;
    public const int DefaultBestMatchLifeKills = 0;
    public const float DefaultBestTeamCoverage = 0f;
    public const float DefaultBestMatchTurf = 0f;
    public const int DefaultMvpCount = 0;

        public const int XpWin = 1200;
        public const int XpLose = 500;
        public const float XpPerTurf = 1.0f;
        public const float XpPerKill = 40.0f;

        public static readonly string[] RankNames = new[]
        {
            "Fresh Recruit", // Tier 0: Lv 1..4
            "Turf Scrapper", // Tier 1: Lv 5..9
            "Ink Slinger",   // Tier 2: Lv 10..19
            "Splat Veteran", // Tier 3: Lv 20..29
            "Tide Legend"    // Tier 4: Lv 30+
        };

        [SerializeField] private string name = DefaultName;
        [SerializeField] private int level = DefaultLevel;
        [SerializeField] private int xp = DefaultXp;
        [SerializeField] private int wins = DefaultWins;
        [SerializeField] private int matches = DefaultMatches;
        [SerializeField] private float totalTurf = DefaultTotalTurf;
        [SerializeField] private int totalKills = DefaultTotalKills;
        [SerializeField] private int totalDeaths = DefaultTotalDeaths;
        [SerializeField] private int totalSpecials = DefaultTotalSpecials;
        [SerializeField] private int bestMatchKills = DefaultBestMatchKills;
        [SerializeField] private int bestMatchStreak = DefaultBestMatchStreak;
        [SerializeField] private int bestMatchLifeKills = DefaultBestMatchLifeKills;
        [SerializeField] private float bestTeamCoverage = DefaultBestTeamCoverage;
        [SerializeField] private float bestMatchTurf = DefaultBestMatchTurf;
        [SerializeField] private int mvpCount = DefaultMvpCount;

        private HashSet<string> _unlockedAchievements;

        private static PlayerProfile _cachedProfile;

        public string Name
        {
            get => name;
            set => name = string.IsNullOrWhiteSpace(value) ? DefaultName : value.Trim();
        }

        public int Level
        {
            get => level;
            set => level = Mathf.Max(1, value);
        }

        public int Xp
        {
            get => xp;
            set => xp = Mathf.Max(0, value);
        }

        public int Wins
        {
            get => wins;
            set => wins = Mathf.Max(0, value);
        }

        public int Matches
        {
            get => matches;
            set => matches = Mathf.Max(0, value);
        }

        public float TotalTurf
        {
            get => totalTurf;
            set => totalTurf = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        }

        public int TotalKills
        {
            get => totalKills;
            set => totalKills = Mathf.Max(0, value);
        }

        public int TotalDeaths
        {
            get => totalDeaths;
            set => totalDeaths = Mathf.Max(0, value);
        }

        public int TotalSpecials
        {
            get => totalSpecials;
            set => totalSpecials = Mathf.Max(0, value);
        }

        public int BestMatchKills
        {
            get => bestMatchKills;
            set => bestMatchKills = Mathf.Max(0, value);
        }

        public int BestMatchStreak
        {
            get => bestMatchStreak;
            set => bestMatchStreak = Mathf.Max(0, value);
        }

        /// <summary>单条命最高击杀数（两次阵亡之间），成就进度用。</summary>
        public int BestMatchLifeKills
        {
            get => bestMatchLifeKills;
            set => bestMatchLifeKills = Mathf.Max(0, value);
        }

        public float BestTeamCoverage
        {
            get => bestTeamCoverage;
            set => bestTeamCoverage = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, 0f, 100f);
        }

        public float BestMatchTurf
        {
            get => bestMatchTurf;
            set => bestMatchTurf = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);
        }

        public int MvpCount
        {
            get => mvpCount;
            set => mvpCount = Mathf.Max(0, value);
        }

        public int RankTier => GetRankTierIndex(level);
        public string RankName => GetRankName(level);
        public int XpRequiredForNextLevel => GetXpRequiredForLevel(level);
        public float LevelProgress01 => XpRequiredForNextLevel > 0 ? Mathf.Clamp01((float)xp / XpRequiredForNextLevel) : 0f;

        /// <summary>
        /// 全局静态活跃档案（带内存缓存，避免频繁读盘）。
        /// </summary>
        public static PlayerProfile Current
        {
            get
            {
                if (_cachedProfile == null)
                {
                    _cachedProfile = Load();
                }
                return _cachedProfile;
            }
        }

        /// <summary>
        /// 计算指定等级升下一级所需的总经验值：
        /// 800 + Level * 350
        /// </summary>
        public static int GetXpRequiredForLevel(int lvl)
        {
            int l = Mathf.Max(1, lvl);
            return 800 + l * 350;
        }

        /// <summary>
        /// 计算等级对应的段位索引 (0..4)。
        /// </summary>
        public static int GetRankTierIndex(int lvl)
        {
            int l = Mathf.Max(1, lvl);
            if (l >= 30) return 4;
            if (l >= 20) return 3;
            if (l >= 10) return 2;
            if (l >= 5) return 1;
            return 0;
        }

        /// <summary>
        /// 获取段位英文名称。
        /// </summary>
        public static string GetRankName(int lvl)
        {
            int tier = GetRankTierIndex(lvl);
            if (tier >= 0 && tier < RankNames.Length)
            {
                return RankNames[tier];
            }
            return RankNames[0];
        }

        /// <summary>
        /// 单局经验值计算：
        /// 胜 1200 / 负 500 + TurfSquareMeters * 1 + Kills * 40（取整，负数钳 0）。
        /// </summary>
        public static int CalculateMatchXp(bool win, CombatantMatchStats stats)
        {
            int baseScore = win ? XpWin : XpLose;
            float turf = Mathf.Max(0f, stats.TurfSquareMeters);
            int kills = Mathf.Max(0, stats.Kills);
            float raw = baseScore + turf * XpPerTurf + kills * XpPerKill;
            return Mathf.Max(0, Mathf.RoundToInt(raw));
        }

        /// <summary>
        /// 对当前玩家档案应用单局结算结果，支持跨级与自动保存。
        /// </summary>
        public ProfileMatchResult ApplyResult(bool win, CombatantMatchStats stats)
        {
            int gained = CalculateMatchXp(win, stats);
            int oldLevel = level;
            int oldXp = xp;

            matches++;
            if (win) wins++;
            totalTurf += Mathf.Max(0f, stats.TurfSquareMeters);

            xp += gained;
            while (xp >= GetXpRequiredForLevel(level))
            {
                xp -= GetXpRequiredForLevel(level);
                level++;
            }

            int levelsGained = level - oldLevel;
            Save();

            return new ProfileMatchResult
            {
                XpGained = gained,
                LevelsGained = levelsGained,
                OldLevel = oldLevel,
                NewLevel = level,
                OldXp = oldXp,
                NewXp = xp,
                XpRequiredForNextLevel = GetXpRequiredForLevel(level)
            };
        }

        /// <summary>
        /// 对本地玩家静态档案结算（便捷入口）。
        /// </summary>
        public static ProfileMatchResult ApplyMatchResult(bool win, CombatantMatchStats localStats)
        {
            return Current.ApplyResult(win, localStats);
        }

        /// <summary>
        /// 生涯统计累计（击杀 / 阵亡 / 大招 / 单局最佳），由成就结算服务在每局结束时调用。
        /// 不写盘：调用方在成就解锁合并后统一 Save() 一次。
        /// </summary>
        public void AccumulateCareerStats(CombatantMatchStats stats, float teamCoveragePercent, int mvpAwards)
        {
            totalKills += Mathf.Max(0, stats.Kills);
            totalDeaths += Mathf.Max(0, stats.Deaths);
            totalSpecials += Mathf.Max(0, stats.SpecialCasts);
            bestMatchKills = Mathf.Max(bestMatchKills, Mathf.Max(0, stats.Kills));
            bestMatchStreak = Mathf.Max(bestMatchStreak, Mathf.Max(0, stats.MaxStreak));
            bestMatchLifeKills = Mathf.Max(bestMatchLifeKills, Mathf.Max(0, stats.MaxLifeKills));
            if (!float.IsNaN(stats.TurfSquareMeters) && !float.IsInfinity(stats.TurfSquareMeters))
            {
                bestMatchTurf = Mathf.Max(bestMatchTurf, stats.TurfSquareMeters);
            }
            if (!float.IsNaN(teamCoveragePercent) && !float.IsInfinity(teamCoveragePercent))
            {
                bestTeamCoverage = Mathf.Clamp(Mathf.Max(bestTeamCoverage, teamCoveragePercent), 0f, 100f);
            }
            mvpCount += Mathf.Max(0, mvpAwards);
        }

        /// <summary>已解锁成就 id 集合（只读视图；懒初始化，容忍空 id）。</summary>
        public IReadOnlyCollection<string> UnlockedAchievements
        {
            get
            {
                EnsureAchievementSet();
                return _unlockedAchievements;
            }
        }

        public int AchievementUnlockedCount
        {
            get
            {
                EnsureAchievementSet();
                return _unlockedAchievements.Count;
            }
        }

        public bool IsAchievementUnlocked(string achievementId)
        {
            if (string.IsNullOrEmpty(achievementId)) return false;
            EnsureAchievementSet();
            return _unlockedAchievements.Contains(achievementId);
        }

        /// <summary>标记成就解锁；返回 true 表示此前未解锁（新解锁）。</summary>
        public bool MarkAchievementUnlocked(string achievementId)
        {
            if (string.IsNullOrEmpty(achievementId)) return false;
            return EnsureAchievementSet().Add(achievementId);
        }

        private HashSet<string> EnsureAchievementSet()
        {
            if (_unlockedAchievements != null) return _unlockedAchievements;

            _unlockedAchievements = new HashSet<string>();
            string serialized;
            try
            {
                serialized = PlayerPrefs.GetString(KeyAchievements, string.Empty);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerProfile] 读取成就解锁记录失败: " + ex.Message);
                serialized = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(serialized))
            {
                // 成就 id 只含 [a-z0-9_]，逗号分隔安全；坏段落直接跳过，绝不让存档解析抛错。
                string[] parts = serialized.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string id = parts[i].Trim();
                    if (!string.IsNullOrEmpty(id)) _unlockedAchievements.Add(id);
                }
            }
            return _unlockedAchievements;
        }

        private string SerializeAchievements()
        {
            EnsureAchievementSet();
            if (_unlockedAchievements.Count == 0) return string.Empty;
            return string.Join(",", _unlockedAchievements);
        }

        /// <summary>
        /// 从 PlayerPrefs 加载数据。若数据损坏或缺失则安全回退默认值。
        /// </summary>
        public static PlayerProfile Load()
        {
            var profile = new PlayerProfile();
            try
            {
                string savedName = PlayerPrefs.GetString(KeyName, DefaultName);
                profile.name = string.IsNullOrWhiteSpace(savedName) ? DefaultName : savedName.Trim();

                int savedLevel = PlayerPrefs.GetInt(KeyLevel, DefaultLevel);
                profile.level = Mathf.Max(1, savedLevel);

                int savedXp = PlayerPrefs.GetInt(KeyXp, DefaultXp);
                profile.xp = Mathf.Max(0, savedXp);

                int savedWins = PlayerPrefs.GetInt(KeyWins, DefaultWins);
                profile.wins = Mathf.Max(0, savedWins);

                int savedMatches = PlayerPrefs.GetInt(KeyMatches, DefaultMatches);
                profile.matches = Mathf.Max(0, savedMatches);

                float savedTurf = PlayerPrefs.GetFloat(KeyTotalTurf, DefaultTotalTurf);
                profile.totalTurf = float.IsNaN(savedTurf) || float.IsInfinity(savedTurf)
                    ? DefaultTotalTurf
                    : Mathf.Max(0f, savedTurf);

                profile.totalKills = Mathf.Max(0, PlayerPrefs.GetInt(KeyTotalKills, DefaultTotalKills));
                profile.totalDeaths = Mathf.Max(0, PlayerPrefs.GetInt(KeyTotalDeaths, DefaultTotalDeaths));
                profile.totalSpecials = Mathf.Max(0, PlayerPrefs.GetInt(KeyTotalSpecials, DefaultTotalSpecials));
                profile.bestMatchKills = Mathf.Max(0, PlayerPrefs.GetInt(KeyBestMatchKills, DefaultBestMatchKills));
                profile.bestMatchStreak = Mathf.Max(0, PlayerPrefs.GetInt(KeyBestMatchStreak, DefaultBestMatchStreak));
                profile.bestMatchLifeKills = Mathf.Max(0, PlayerPrefs.GetInt(KeyBestMatchLifeKills, DefaultBestMatchLifeKills));
                profile.mvpCount = Mathf.Max(0, PlayerPrefs.GetInt(KeyMvpCount, DefaultMvpCount));

                float savedCoverage = PlayerPrefs.GetFloat(KeyBestTeamCoverage, DefaultBestTeamCoverage);
                profile.bestTeamCoverage = float.IsNaN(savedCoverage) || float.IsInfinity(savedCoverage)
                    ? DefaultBestTeamCoverage
                    : Mathf.Clamp(savedCoverage, 0f, 100f);

                float savedBestTurf = PlayerPrefs.GetFloat(KeyBestMatchTurf, DefaultBestMatchTurf);
                profile.bestMatchTurf = float.IsNaN(savedBestTurf) || float.IsInfinity(savedBestTurf)
                    ? DefaultBestMatchTurf
                    : Mathf.Max(0f, savedBestTurf);

                // 成就解锁集合懒加载（EnsureAchievementSet 直读 PlayerPrefs），Load 无需解析。
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerProfile] 读取存档失败，重置为默认值: " + ex.Message);
                profile.ResetToDefaults();
            }

            _cachedProfile = profile;
            return profile;
        }

        /// <summary>
        /// 保存当前档案到 PlayerPrefs。
        /// </summary>
        public void Save()
        {
            try
            {
                PlayerPrefs.SetString(KeyName, name);
                PlayerPrefs.SetInt(KeyLevel, Mathf.Max(1, level));
                PlayerPrefs.SetInt(KeyXp, Mathf.Max(0, xp));
                PlayerPrefs.SetInt(KeyWins, Mathf.Max(0, wins));
                PlayerPrefs.SetInt(KeyMatches, Mathf.Max(0, matches));
                PlayerPrefs.SetFloat(KeyTotalTurf, Mathf.Max(0f, totalTurf));
                PlayerPrefs.SetInt(KeyTotalKills, Mathf.Max(0, totalKills));
                PlayerPrefs.SetInt(KeyTotalDeaths, Mathf.Max(0, totalDeaths));
                PlayerPrefs.SetInt(KeyTotalSpecials, Mathf.Max(0, totalSpecials));
                PlayerPrefs.SetInt(KeyBestMatchKills, Mathf.Max(0, bestMatchKills));
                PlayerPrefs.SetInt(KeyBestMatchStreak, Mathf.Max(0, bestMatchStreak));
                PlayerPrefs.SetInt(KeyBestMatchLifeKills, Mathf.Max(0, bestMatchLifeKills));
                PlayerPrefs.SetFloat(KeyBestTeamCoverage, Mathf.Clamp(bestTeamCoverage, 0f, 100f));
                PlayerPrefs.SetFloat(KeyBestMatchTurf, Mathf.Max(0f, bestMatchTurf));
                PlayerPrefs.SetInt(KeyMvpCount, Mathf.Max(0, mvpCount));
                PlayerPrefs.SetString(KeyAchievements, SerializeAchievements());
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerProfile] 保存存档失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 重置存档为初始默认值并清除 PlayerPrefs 对应键。
        /// </summary>
        public static void Reset()
        {
            try
            {
                PlayerPrefs.DeleteKey(KeyName);
                PlayerPrefs.DeleteKey(KeyLevel);
                PlayerPrefs.DeleteKey(KeyXp);
                PlayerPrefs.DeleteKey(KeyWins);
                PlayerPrefs.DeleteKey(KeyMatches);
                PlayerPrefs.DeleteKey(KeyTotalTurf);
                PlayerPrefs.DeleteKey(KeyTotalKills);
                PlayerPrefs.DeleteKey(KeyTotalDeaths);
                PlayerPrefs.DeleteKey(KeyTotalSpecials);
                PlayerPrefs.DeleteKey(KeyBestMatchKills);
                PlayerPrefs.DeleteKey(KeyBestMatchStreak);
                PlayerPrefs.DeleteKey(KeyBestMatchLifeKills);
                PlayerPrefs.DeleteKey(KeyBestTeamCoverage);
                PlayerPrefs.DeleteKey(KeyBestMatchTurf);
                PlayerPrefs.DeleteKey(KeyMvpCount);
                PlayerPrefs.DeleteKey(KeyAchievements);
                PlayerPrefs.Save();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PlayerProfile] 重置存档失败: " + ex.Message);
            }

            if (_cachedProfile == null)
            {
                _cachedProfile = new PlayerProfile();
            }
            else
            {
                _cachedProfile.ResetToDefaults();
            }
        }

        /// <summary>
        /// 重置实例字段为默认值（不写盘）。
        /// </summary>
        public void ResetToDefaults()
        {
            name = DefaultName;
            level = DefaultLevel;
            xp = DefaultXp;
            wins = DefaultWins;
            matches = DefaultMatches;
            totalTurf = DefaultTotalTurf;
            totalKills = DefaultTotalKills;
            totalDeaths = DefaultTotalDeaths;
            totalSpecials = DefaultTotalSpecials;
            bestMatchKills = DefaultBestMatchKills;
            bestMatchStreak = DefaultBestMatchStreak;
            bestMatchLifeKills = DefaultBestMatchLifeKills;
            bestTeamCoverage = DefaultBestTeamCoverage;
            bestMatchTurf = DefaultBestMatchTurf;
            mvpCount = DefaultMvpCount;
            if (_unlockedAchievements != null) _unlockedAchievements.Clear();
        }

        /// <summary>
        /// 清除静态内存缓存，使下次读取强制重新访问 PlayerPrefs。
        /// </summary>
        public static void InvalidateCache()
        {
            _cachedProfile = null;
        }
    }
}
