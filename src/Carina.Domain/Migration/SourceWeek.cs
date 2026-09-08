namespace Carina.Domain.Migration;

public static class SourceWeek
{
    public const int EveryDay = 0b111_1111;

    public static int Of(int days, string parameterName)
        => days is >= 0 and <= EveryDay
            ? days
            : throw new ArgumentOutOfRangeException(
                parameterName,
                days,
                $"A week of the source system is a mask of seven days, so it is 0 to {EveryDay}.");

    public static bool NarrowsTheWeek(int days) => Of(days, nameof(days)) != EveryDay;
}
