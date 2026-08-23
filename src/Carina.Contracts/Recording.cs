namespace Carina.Contracts;

public sealed record ExtendSessionRequest
{
    public required DateTimeOffset EndsAt { get; init; }

    public IReadOnlyList<string> Validate(DateTimeOffset currentEndsAt) =>
        EndsAt > currentEndsAt
            ? []
            :
            [
                $"endsAt: a recording only ever follows a programme later, so expected a time after {currentEndsAt:O}; got {EndsAt:O}.",
            ];
}

public sealed record RecordingProgressDto
{
    private readonly SessionCounters counters = SessionCounters.Nothing;

    public SessionId SessionId { get; init; }

    public string RecordingId { get; init; } = string.Empty;

    public DateTimeOffset ObservedAt { get; init; }

    public long BytesWritten { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    public SessionCounters Counters
    {
        get => counters;
        init => counters = value ?? SessionCounters.Nothing;
    }
}

public sealed record RecordingSessionDto
{
    public SessionId SessionId { get; init; }

    public string RecordingId { get; init; } = string.Empty;

    public string OutputRoot { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    public long BytesWritten { get; init; }

    public long CcDropped { get; init; }

    public long CcTotal { get; init; }

    public bool CcMeasured { get; init; }

    public long ScrambledPackets { get; init; }

    public bool ScrambleMeasured { get; init; }

    public long EovfCount { get; init; }

    public static RecordingSessionDto Of(DriverHello hello, SessionSnapshot session)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(session);

        bool countedContinuity =
            hello.Supports(DriverCapabilities.CcMeasurement) && session.Counters.CcMeasured;
        bool countedScrambling =
            hello.Supports(DriverCapabilities.ScrambleMeasurement)
            && session.Counters.ScrambleMeasured;

        return new RecordingSessionDto
        {
            SessionId = session.SessionId,
            RecordingId = session.RecordingId ?? string.Empty,
            OutputRoot = session.OutputRoot ?? string.Empty,
            StartedAt = session.StartedAt,
            EndsAt = session.EndsAt,
            BytesWritten = session.BytesRecorded,
            CcDropped = countedContinuity ? session.Counters.Drops : 0,
            CcTotal = countedContinuity ? session.Counters.Packets : 0,
            CcMeasured = countedContinuity,
            ScrambledPackets = countedScrambling ? session.Counters.ScrambledPackets : 0,
            ScrambleMeasured = countedScrambling,
            EovfCount = session.Counters.DeviceOverflows,
        };
    }
}
