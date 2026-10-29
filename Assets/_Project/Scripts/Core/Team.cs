using UnityEngine;

namespace P1.Core
{
    public enum Team : byte
    {
        Neutral = 0,
        TeamA = 1,
        TeamB = 2
    }

    public static class TeamExtensions
    {
        public static Team GetOpponent(this Team team)
        {
            return team switch
            {
                Team.TeamA => Team.TeamB,
                Team.TeamB => Team.TeamA,
                _ => Team.Neutral
            };
        }

        public static bool IsOpponentOf(this Team self, Team other)
        {
            return self != Team.Neutral && other != Team.Neutral && self != other;
        }
    }
}
