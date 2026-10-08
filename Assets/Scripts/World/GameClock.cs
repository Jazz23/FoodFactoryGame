// The one game clock that pacing rules share (decision 0039): road traffic's rush hours and districts' customer rates both
// count game hours of HourSeconds clock seconds, so "per hour" means the same thing everywhere. All numbers are PROTOTYPE.
namespace FoodFactoryGame.World
{
    public static class GameClock
    {
        // PROTOTYPE: one game hour lasts a real minute, so a day of rush hours passes in 24 minutes.
        public const int HourSeconds = 60;
        public const int DaySeconds = 24 * HourSeconds;

        // Decision 0039: district rates stored before the game hour (layout format 4 and older, goods snapshot v20 and older)
        // counted customers per 3600 clock s. Re-basing them on the 60 s game hour would multiply demand by 60, so they are
        // divided by this PROTOTYPE factor instead, giving 60 / RetuneDivisor times the old demand per clock second.
        public const int RetuneDivisor = 10;

        // An old per-3600 s rate in customers per game hour, rounded half up.
        public static int FromClockHourRate(int customersPerClockHour) =>
            customersPerClockHour <= 0 ? 0 : (customersPerClockHour + RetuneDivisor / 2) / RetuneDivisor;
    }
}
