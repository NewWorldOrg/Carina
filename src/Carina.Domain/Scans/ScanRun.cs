namespace Carina.Domain.Scans;

public sealed class ScanRun
{
    public const int ReasonMaxLength = 512;

    private ScanRun()
    {
    }

    public ScanRunId Id { get; private set; } = null!;

    public ScanRunState State { get; private set; }

    public string? DriverInstanceId { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime? FinishedAt { get; private set; }

    public string? Reason { get; private set; }

    public bool IsRunning => State is ScanRunState.Running;

    public static ScanRun Start(ScanRunId id, string? driverInstanceId, DateTime at)
        => Rehydrate(id, ScanRunState.Running, driverInstanceId, at, null, null);

    public static ScanRun Rehydrate(
        ScanRunId id,
        ScanRunState state,
        string? driverInstanceId,
        DateTime startedAt,
        DateTime? finishedAt,
        string? reason)
    {
        ArgumentNullException.ThrowIfNull(id);

        if ((state is ScanRunState.Running) != (finishedAt is null))
        {
            throw new ArgumentException(
                "A running scan has not finished, and a finished one names when it did.",
                nameof(finishedAt));
        }

        if (state is ScanRunState.Failed or ScanRunState.Cancelled && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException(
                $"A scan that ends as {state} says why.",
                nameof(reason));
        }

        return new ScanRun
        {
            Id = id,
            State = state,
            DriverInstanceId = driverInstanceId,
            StartedAt = UtcTimes.Required(startedAt, nameof(startedAt)),
            FinishedAt = UtcTimes.Optional(finishedAt, nameof(finishedAt)),
            Reason = ValidatedReason(reason),
        };
    }

    public void Complete(DateTime at) => Conclude(ScanRunState.Completed, null, at);

    public void Fail(string reason, DateTime at) => Conclude(ScanRunState.Failed, Stated(reason), at);

    public void Cancel(string reason, DateTime at) => Conclude(ScanRunState.Cancelled, Stated(reason), at);

    public void Interrupt(DateTime at) => Conclude(ScanRunState.Interrupted, null, at);

    private void Conclude(ScanRunState state, string? reason, DateTime at)
    {
        if (State is not ScanRunState.Running)
        {
            throw new InvalidOperationException(
                $"A scan leaves Running once; this one is already {State}.");
        }

        State = state;
        Reason = ValidatedReason(reason);
        FinishedAt = UtcTimes.Required(at, nameof(at));
    }

    private static string Stated(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return reason;
    }

    private static string? ValidatedReason(string? reason)
    {
        if (reason is not null && reason.Length > ReasonMaxLength)
        {
            throw new ArgumentException(
                $"A reason is at most {ReasonMaxLength} characters, but this one has {reason.Length}.",
                nameof(reason));
        }

        return reason;
    }
}
