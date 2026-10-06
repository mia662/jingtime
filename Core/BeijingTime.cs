using System.Globalization;

namespace BeijingClock.Core;

public sealed record ClockSnapshot(
    DateTimeOffset BeijingTime,
    string DateText,
    string TimeText,
    string RemainingText,
    double RemainingFraction,
    int RemainingMinutes);

public static class BeijingTime
{
    private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
    private const double SecondsPerDay = 24 * 60 * 60;

    public static ClockSnapshot At(DateTimeOffset instant)
    {
        DateTimeOffset beijingTime = instant.ToOffset(BeijingOffset);
        DateTimeOffset nextMidnight = new(
            beijingTime.Year,
            beijingTime.Month,
            beijingTime.Day,
            0,
            0,
            0,
            BeijingOffset);
        nextMidnight = nextMidnight.AddDays(1);

        double remainingSeconds = (nextMidnight - beijingTime).TotalSeconds;
        int remainingMinutes = checked((int)Math.Ceiling(remainingSeconds / 60d));
        int hours = remainingMinutes / 60;
        int minutes = remainingMinutes % 60;

        return new ClockSnapshot(
            beijingTime,
            beijingTime.ToString("yyyy年MM月dd日", CultureInfo.InvariantCulture),
            beijingTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            FormattableString.Invariant($"{hours:00}时{minutes:00}分"),
            remainingSeconds / SecondsPerDay,
            remainingMinutes);
    }
}
