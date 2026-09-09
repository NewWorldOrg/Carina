using Carina.Domain.Programmes;

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

    public static IReadOnlyList<DayOfWeek>? Named(int days)
    {
        int week = Of(days, nameof(days));

        if (week is 0)
        {
            return null;
        }

        if (week == EveryDay)
        {
            return [];
        }

        List<DayOfWeek> found = new(ProgrammeSearch.DaysInTheWeek);

        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            if ((week & (1 << (int)day)) is not 0)
            {
                found.Add(day);
            }
        }

        return found;
    }
}
