namespace Carina.Domain.Quality;

public enum SignalNotTaken
{
    DriverUnreachable = 1,

    NothingReported = 2,

    NoTimeGiven = 3,

    FiguresRefused = 4,
}

public static class SignalNotTakens
{
    public static readonly IReadOnlyList<SignalNotTaken> All =
    [
        SignalNotTaken.DriverUnreachable,
        SignalNotTaken.NothingReported,
        SignalNotTaken.NoTimeGiven,
        SignalNotTaken.FiguresRefused,
    ];
}
