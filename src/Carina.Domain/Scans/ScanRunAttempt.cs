using Carina.Domain.Channels;

namespace Carina.Domain.Scans;

public sealed class ScanRunAttempt
{
    public const int DetailMaxLength = 512;

    private ScanRunAttempt()
    {
    }

    public ScanRunAttemptId Id { get; private set; } = null!;

    public ScanRunId ScanRunId { get; private set; } = null!;

    public TuningParameters Tuning { get; private set; } = null!;

    public ScanAttemptOutcome Outcome { get; private set; }

    public SignalMeasurement? Measurement { get; private set; }

    public TransportStreamId? ObservedTransportStreamId { get; private set; }

    public string? Detail { get; private set; }

    public DateTime StartedAt { get; private set; }

    public DateTime FinishedAt { get; private set; }

    public bool Failed => Outcome is not ScanAttemptOutcome.Succeeded;

    public static ScanRunAttempt Rehydrate(
        ScanRunAttemptId id,
        ScanRunId scanRunId,
        TuningParameters tuning,
        ScanAttemptOutcome outcome,
        SignalMeasurement? measurement,
        TransportStreamId? observedTransportStreamId,
        string? detail,
        DateTime startedAt,
        DateTime finishedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(scanRunId);
        ArgumentNullException.ThrowIfNull(tuning);

        if (detail is not null && detail.Length > DetailMaxLength)
        {
            throw new ArgumentException(
                $"A detail is at most {DetailMaxLength} characters, but this one has {detail.Length}.",
                nameof(detail));
        }

        var started = UtcTimes.Required(startedAt, nameof(startedAt));
        var finished = UtcTimes.Required(finishedAt, nameof(finishedAt));

        if (finished < started)
        {
            throw new ArgumentException(
                "An attempt cannot finish before it started.",
                nameof(finishedAt));
        }

        return new ScanRunAttempt
        {
            Id = id,
            ScanRunId = scanRunId,
            Tuning = tuning,
            Outcome = outcome,
            Measurement = measurement,
            ObservedTransportStreamId = observedTransportStreamId,
            Detail = detail,
            StartedAt = started,
            FinishedAt = finished,
        };
    }
}
