namespace Carina.Domain.Encodings;

public enum EncodeJobStatus
{
    Queued = 1,

    Running = 2,

    Completed = 3,

    Failed = 4,

    Cancelled = 5,
}

public enum EncodeStanding
{
    NotEncoded = 1,

    Queued = 2,

    Running = 3,

    Completed = 4,

    Failed = 5,
}

public static class EncodeStandings
{
    public static readonly IReadOnlyList<EncodeJobStatus> Terminal =
    [
        EncodeJobStatus.Completed,
        EncodeJobStatus.Failed,
        EncodeJobStatus.Cancelled,
    ];

    public static EncodeJobStatus Named(EncodeJobStatus status)
        => Enum.IsDefined(status)
            ? status
            : throw new ArgumentOutOfRangeException(nameof(status), status, "A job stands in one of the five places.");

    public static bool IsTerminal(EncodeJobStatus status) => Terminal.Contains(Named(status));

    public static EncodeStanding Of(EncodeJobStatus? latest)
        => latest is null
            ? EncodeStanding.NotEncoded
            : Named(latest.Value) switch
            {
                EncodeJobStatus.Queued => EncodeStanding.Queued,
                EncodeJobStatus.Running => EncodeStanding.Running,
                EncodeJobStatus.Completed => EncodeStanding.Completed,
                EncodeJobStatus.Failed => EncodeStanding.Failed,
                _ => EncodeStanding.NotEncoded,
            };
}
