namespace Carina.Domain.Notifications;

public enum ProgrammeWatchState
{
    Waiting = 1,

    Notified = 2,

    Skipped = 3,

    Cancelled = 4,
}

/// <summary>
/// Where the clock stands against a programme asked to be told of.
/// </summary>
public enum ProgrammeWatchMoment
{
    NotYet = 1,

    Soon = 2,

    OnAir = 3,

    Over = 4,
}
