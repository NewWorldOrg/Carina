namespace Carina.Domain.Machines;

/// <summary>
/// Where the card stands on this machine. <see cref="Usable"/> means a frame was actually encoded
/// on it, not that a device node happened to be there.
/// </summary>
public enum CardStanding
{
    Usable = 1,

    NodeMissing = 2,

    NodeUnreadable = 3,

    DriverUnusable = 4,

    ProbeTimedOut = 5,

    ProbeProgrammeMissing = 6,
}

public static class CardStandings
{
    public static CardStanding Named(CardStanding standing)
        => Enum.IsDefined(standing)
            ? standing
            : throw new ArgumentOutOfRangeException(
                nameof(standing),
                standing,
                "A card stands in one of the places named here.");

    public static bool IsUsable(CardStanding standing) => Named(standing) is CardStanding.Usable;
}
