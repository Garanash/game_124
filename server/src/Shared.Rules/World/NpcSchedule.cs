using System.Linq;
using Realmforge.Rules.Definitions;

namespace Realmforge.Rules.World
{
    public static class GameClock
    {
        /// <summary>One in-game day lasts two real hours, so players see every part of the NPC routine.</summary>
        public const double RealSecondsPerGameDay = 7200;
        public const int HoursPerDay = 24;

        public static double HourOfDay(double unixSeconds)
        {
            double dayFraction = unixSeconds % RealSecondsPerGameDay / RealSecondsPerGameDay;
            return dayFraction * HoursPerDay;
        }
    }

    public static class NpcSchedule
    {
        /// <summary>Supports ranges wrapping past midnight, e.g. 22 -> 6 for night guards.</summary>
        public static ScheduleEntry? ActiveEntry(NpcDefinition npc, double hourOfDay)
        {
            return npc.Schedule.FirstOrDefault(entry => Covers(entry, hourOfDay)) ?? npc.Schedule.FirstOrDefault();
        }

        private static bool Covers(ScheduleEntry entry, double hour)
        {
            if (entry.FromHour <= entry.ToHour)
            {
                return hour >= entry.FromHour && hour < entry.ToHour;
            }
            return hour >= entry.FromHour || hour < entry.ToHour;
        }
    }
}
