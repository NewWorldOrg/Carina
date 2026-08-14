using Carina.Domain.Channels;
using Carina.Domain.Scans;

namespace Carina.Infrastructure.Scanning;

public enum ProbeVerdict
{
    Attempted = 1,

    TunersBusy = 2,

    DriverUnreachable = 3,
}

public sealed record StreamProbe
{
    private StreamProbe(ProbeVerdict verdict, ScanAttemptOutcome outcome, string? detail)
    {
        Verdict = verdict;
        Outcome = outcome;
        Detail = detail;
    }

    public ProbeVerdict Verdict { get; }

    public ScanAttemptOutcome Outcome { get; }

    public string? Detail { get; }

    public SignalMeasurement? Measurement { get; init; }

    public TransportStreamId? ObservedTransportStreamId { get; init; }

    public HarvestedNetwork? Network { get; init; }

    public HarvestedDescription? Description { get; init; }

    public bool Failed => Outcome is not ScanAttemptOutcome.Succeeded;

    public static StreamProbe Attempted(ScanAttemptOutcome outcome, string? detail = null)
        => new(ProbeVerdict.Attempted, outcome, Clipped(detail));

    public static StreamProbe TunersBusy(string detail)
        => new(ProbeVerdict.TunersBusy, ScanAttemptOutcome.NoLock, Clipped(detail));

    public static StreamProbe DriverUnreachable(string detail)
        => new(ProbeVerdict.DriverUnreachable, ScanAttemptOutcome.NoLock, Clipped(detail));

    private static string? Clipped(string? detail)
        => detail is not null && detail.Length > ScanRunAttempt.DetailMaxLength
            ? detail[..ScanRunAttempt.DetailMaxLength]
            : detail;
}
