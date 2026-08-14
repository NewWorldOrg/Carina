using System.Text.Json.Serialization;

namespace Carina.Contracts;

[JsonConverter(typeof(SessionPurposeConverter))]
public enum SessionPurpose
{
    Unspecified = 0,

    Recording = 1,

    Live = 2,

    Survey = 3,

    Scan = 4,
}

[JsonConverter(typeof(TunerKindConverter))]
public enum TunerKind
{
    Unspecified = 0,

    Terrestrial = 1,

    Satellite = 2,
}

[JsonConverter(typeof(TunerStateConverter))]
public enum TunerState
{
    Unspecified = 0,

    Idle = 1,

    Busy = 2,

    Disabled = 3,

    Faulted = 4,

    Draining = 5,
}

[JsonConverter(typeof(SessionStateConverter))]
public enum SessionState
{
    Unspecified = 0,

    Requested = 1,

    Active = 2,

    Stopping = 3,

    Stopped = 4,

    Failed = 5,
}

[JsonConverter(typeof(DiagnosticReasonConverter))]
public enum DiagnosticReason
{
    Unspecified = 0,

    RecordingWriteFailed = 1,

    DiskSpaceLow = 2,

    DeviceFaulted = 3,

    TuningLost = 4,

    RecordingCutShort = 5,

    MeasurementFaulted = 6,
}

public sealed record TuningRequest(TunerKind Kind, int PhysicalChannel, int? ServiceId = null);

public sealed record StartSessionRequest
{
    private const int MinPhysicalChannel = 1;
    private const int MaxPhysicalChannel = 255;

    private const int MaxServiceId = 65535;

    public required SessionId SessionId { get; init; }

    public required SessionPurpose Purpose { get; init; }

    public required TuningRequest Tuning { get; init; }

    public string? DeviceId { get; init; }

    public string? OutputRoot { get; init; }

    public DateTimeOffset? EndsAt { get; init; }

    public TuneParams? Tune { get; init; }

    public IReadOnlyList<string> Validate(DateTimeOffset now)
    {
        var problems = new List<string>();

        if (SessionId.IsUnset)
        {
            problems.Add(
                $"sessionId: expected 1 to {SessionId.MaxLength} characters of A-Z, a-z, 0-9 or '-'."
            );
        }

        if (Purpose is SessionPurpose.Unspecified)
        {
            problems.Add("purpose: missing, or a value this driver does not know.");
        }

        if (DeviceId is not null && !WireName.IsUsable(DeviceId))
        {
            problems.Add($"deviceId: expected {WireName.Description}; got '{DeviceId}'.");
        }

        problems.AddRange(OutputRootProblems());

        if (Tuning is null)
        {
            problems.Add("tuning: missing.");
            return problems;
        }

        var tuneProblems = Tune?.Validate() ?? [];
        problems.AddRange(tuneProblems.Select(problem => $"tune.{problem}"));

        if (Tune is null && Tuning.Kind is TunerKind.Unspecified)
        {
            problems.Add("tuning.kind: missing, or a value this driver does not know.");
        }

        if (Tuning.PhysicalChannel is < MinPhysicalChannel or > MaxPhysicalChannel)
        {
            problems.Add(
                $"tuning.physicalChannel: expected {MinPhysicalChannel} to {MaxPhysicalChannel}, got {Tuning.PhysicalChannel}."
            );
        }

        if (Tune is null && Tuning.ServiceId is < 0 or > MaxServiceId)
        {
            problems.Add(
                $"tuning.serviceId: expected 0 to {MaxServiceId}, got {Tuning.ServiceId}."
            );
        }

        if (Tune is not null && Tuning.ServiceId is not null)
        {
            problems.Add(
                $"tuning.serviceId: a typed tune names no service and the driver filters no PIDs by service, so a service id here would mean something to one driver and nothing to another; got {Tuning.ServiceId}."
            );
        }

        if (Tune is not null && tuneProblems.Count is 0)
        {
            problems.AddRange(AgreementProblems(Tune.ToLegacyRequest()));
        }

        if (Purpose is SessionPurpose.Recording && EndsAt is null)
        {
            problems.Add("endsAt: a recording session has to carry its own end time.");
        }

        if (EndsAt is { } endsAt && endsAt <= now)
        {
            problems.Add($"endsAt: expected a time after {now:O}, got {endsAt:O}.");
        }

        return problems;
    }

    private IReadOnlyList<string> AgreementProblems(TuningRequest expected)
    {
        if (
            Tuning.Kind == expected.Kind
            && Tuning.PhysicalChannel == expected.PhysicalChannel
        )
        {
            return [];
        }

        return expected.Kind is TunerKind.Unspecified
            ?
            [
                $"tuning: the older field cannot name a tune on {TuneSystemConverter.WireName(Tune!.System)}, so it has to be left without a kind on physical channel {expected.PhysicalChannel}, which a driver that reads only that field refuses instead of tuning; got {Describe(Tuning)}.",
            ]
            :
            [
                $"tuning: expected {Describe(expected)} to match tune, so that a driver reading either field tunes the same way; got {Describe(Tuning)}.",
            ];
    }

    private static string Describe(TuningRequest tuning) =>
        $"kind {TunerKindConverter.WireName(tuning.Kind)} on physical channel {tuning.PhysicalChannel}";

    private IReadOnlyList<string> OutputRootProblems()
    {
        if (Purpose is not SessionPurpose.Recording)
        {
            return OutputRoot is null
                ? []
                :
                [
                    $"outputRoot: only a recording writes a file, and this request is a {Purpose.ToString().ToLowerInvariant()} one; got '{OutputRoot}'.",
                ];
        }

        if (OutputRoot is null)
        {
            return
            [
                "outputRoot: a recording names one of the output roots this driver declares.",
            ];
        }

        return WireName.IsUsable(OutputRoot)
            ? []
            : [$"outputRoot: expected {WireName.Description}; got '{OutputRoot}'."];
    }
}

public sealed record SessionCounters(
    long Packets = 0,
    long Drops = 0,
    long Duplicates = 0,
    long Discontinuities = 0,
    long TransportErrors = 0,
    long ScrambledPackets = 0,
    long ProvisionalPackets = 0,
    long DiscardedBytes = 0,
    long Resyncs = 0
)
{
    public static readonly SessionCounters Nothing = new();
}

public sealed record SessionSnapshot(
    SessionId SessionId,
    SessionPurpose Purpose,
    string DeviceId,
    SessionState State,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndsAt = null
)
{
    private readonly SessionCounters counters = SessionCounters.Nothing;

    public string DeviceId { get; init; } = DeviceId ?? string.Empty;

    public SessionStopReason StopReason { get; init; }

    public bool Concluded { get; init; }

    public string? InstanceId { get; init; }

    public string? OutputRoot { get; init; }

    public long BytesRecorded { get; init; }

    public long FaultCount { get; init; }

    public long DroppedChunks { get; init; }

    public string? FirstFault { get; init; }

    public string? FailureCause { get; init; }

    public SessionCounters Counters
    {
        get => counters;
        init => counters = value ?? SessionCounters.Nothing;
    }
}

public sealed record DriverProblem(string Title, IReadOnlyList<string> Problems)
{
    public string Title { get; init; } = Title ?? string.Empty;

    public IReadOnlyList<string> Problems { get; init; } = Problems ?? [];
}

public sealed record TunerSnapshot(
    string DeviceId,
    TunerKind Kind,
    TunerState State,
    SessionId SessionId = default,
    string? Detail = null
)
{
    public string DeviceId { get; init; } = DeviceId ?? string.Empty;

    public TunerHealthDto? Health { get; init; }

    public SignalQualityDto? SignalQuality { get; init; }

    public CurrentSessionDto? CurrentSession { get; init; }

    public bool Toggled { get; init; }
}

public sealed record DiagnosticSnapshot(
    DiagnosticReason Reason,
    DateTimeOffset OccurredAt,
    string? DeviceId = null,
    SessionId SessionId = default,
    string? Detail = null
);
