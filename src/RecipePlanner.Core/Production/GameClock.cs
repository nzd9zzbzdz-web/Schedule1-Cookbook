using System;

namespace RecipePlanner.Core.Production
{
    /// <summary>
    /// Converts the game's split (ElapsedDays, TimeOfDay) clock into a single ordinal so two
    /// moments can be compared.
    ///
    /// TimeOfDay is packed decimal — 1507 means 15:07, not 1507 minutes — so comparing it directly
    /// is wrong across an hour boundary. <c>TimeManager</c> does the same unpacking in
    /// <c>GetMinSumFrom24HourTime</c>, and increments ElapsedDays as the clock rolls 23:59 → 00:00,
    /// which is what makes <c>days * 1440 + minutes</c> strictly increasing over a play session.
    ///
    /// Verified against ScheduleOne.GameTime.TimeManager in 0.4.6f13:
    ///   GetMinSumFrom24HourTime(t) => (t / 100) * 60 + (t % 100)
    ///   GetTotalMinSum()           => ElapsedDays * 1440 + DailyMinSum
    /// </summary>
    public static class GameClock
    {
        public const int MinutesPerDay = 1440;

        /// <summary>Minutes past midnight for a packed HHMM time.</summary>
        public static int MinutesOfDay(int timeOfDay)
        {
            var hours = timeOfDay / 100;
            var minutes = timeOfDay - hours * 100;
            return hours * 60 + minutes;
        }

        /// <summary>Minutes since the save began. Monotonic for as long as time only moves forward.</summary>
        public static long Ordinal(int elapsedDays, int timeOfDay) =>
            (long)elapsedDays * MinutesPerDay + MinutesOfDay(timeOfDay);

        public static long Ordinal(ProductionEvent evt) =>
            evt == null ? 0 : Ordinal(evt.ElapsedDays, evt.TimeOfDay);

        /// <summary>"day 40, 15:07" — for logs a human has to read.</summary>
        public static string Describe(int elapsedDays, int timeOfDay) =>
            string.Format("day {0}, {1:00}:{2:00}", elapsedDays, timeOfDay / 100, timeOfDay % 100);
    }
}
