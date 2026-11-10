using System.Globalization;

namespace Carina.Domain.Notifications;

internal enum NoticeUnit
{
    Share = 1,

    Count = 2,

    Decibels = 3,

    ErrorRate = 4,

    Minutes = 5,
}

/// <summary>
/// How a measured value is said in each unit a notice uses.
/// </summary>
internal static class NoticeUnits
{
    private const string Plain = "0.###";

    private static readonly IReadOnlyDictionary<NoticeUnit, Measure> Measures = new Dictionary<NoticeUnit, Measure>
    {
        [NoticeUnit.Share] = new Measure(0.01, Plain, "%"),
        [NoticeUnit.Count] = new Measure(1, Plain, " 回"),
        [NoticeUnit.Decibels] = new Measure(1000, Plain, " dB"),
        [NoticeUnit.ErrorRate] = new Measure(1, "0.0E-0", string.Empty),
        [NoticeUnit.Minutes] = new Measure(60, Plain, " 分"),
    };

    public static string Said(NoticeUnit unit, double value) => Measures[unit].Said(value);

    private sealed record Measure(double Unit, string Format, string Suffix)
    {
        public string Said(double value) => (value / Unit).ToString(Format, CultureInfo.InvariantCulture) + Suffix;
    }
}
