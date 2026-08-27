using Carina.Domain.Reservations;

namespace Carina.Api.Common;

public static class ReservationInput
{
    public static readonly int LongestMarginSeconds = (int)Margin.Longest.TotalSeconds;

    public static string Description
        => $"A priority is {Priority.MinValue} to {Priority.MaxValue}, and a margin is a whole number of seconds "
            + $"from 0 to {LongestMarginSeconds}.";

    public static bool Holds(int? priority, int? marginBefore, int? marginAfter)
        => WithinPriority(priority) && WithinMargin(marginBefore) && WithinMargin(marginAfter);

    public static Priority? PriorityOf(int? asked) => asked is { } value ? new Priority(value) : null;

    public static Margin? MarginOf(int? asked) => asked is { } value ? Margin.OfSeconds(value) : null;

    private static bool WithinPriority(int? asked)
        => asked is null || asked is >= Priority.MinValue and <= Priority.MaxValue;

    private static bool WithinMargin(int? asked) => asked is null || (asked >= 0 && asked <= LongestMarginSeconds);
}
