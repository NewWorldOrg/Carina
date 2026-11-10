using System.Globalization;

using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

/// <summary>
/// Words times as a notice says them: in Japan's time, with the day of the week.
/// </summary>
internal static class NoticeTimes
{
    private const string DaysOfTheWeek = "日月火水木金土";

    private static readonly TimeSpan ADay = TimeSpan.FromDays(1);

    public static string Day(DateTime utc)
    {
        DateTime local = JapanTimeZone.FromUtc(utc);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{local:MM}/{local:dd}({DaysOfTheWeek[(int)local.DayOfWeek]}) {local:HH}:{local:mm}");
    }

    public static string Clock(DateTime utc)
    {
        DateTime local = JapanTimeZone.FromUtc(utc);

        return string.Create(CultureInfo.InvariantCulture, $"{local:HH}:{local:mm}");
    }

    public static string Span(DateTime startsUtc, DateTime endsUtc)
    {
        if (endsUtc <= startsUtc)
        {
            return Day(startsUtc);
        }

        string ends = endsUtc - startsUtc >= ADay ? Day(endsUtc) : Clock(endsUtc);

        return $"{Day(startsUtc)}〜{ends}";
    }

    public static string Since(DateTime utc) => $"{Day(utc)} から";
}
