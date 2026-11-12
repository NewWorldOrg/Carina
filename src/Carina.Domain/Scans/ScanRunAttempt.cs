using Carina.Domain.Base;
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

    /// <summary>
    /// Tables that never completed, or a stream other than the one expected when no particular one was asked for or
    /// the one asked for arrived, say nothing of whether the channel can be received.
    /// </summary>
    public bool SaysNothingOfReception
        => Outcome switch
        {
            ScanAttemptOutcome.IncompleteTables => true,
            ScanAttemptOutcome.UnexpectedStream => Tuning.TransportStreamId is not { } wanted
                || wanted.Equals(ObservedTransportStreamId),
            _ => false,
        };

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

        DateTime started = UtcTimes.Required(startedAt, nameof(startedAt));
        DateTime finished = UtcTimes.Required(finishedAt, nameof(finishedAt));

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
