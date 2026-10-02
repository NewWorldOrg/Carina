using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Quality;

public sealed record QualityLedgerRow
{
    private QualityLedgerRow(
        RecordingId recording,
        NetworkId network,
        ServiceId service,
        TuneSystem? kind,
        TunerDeviceId? tuner,
        DateTime startedAt,
        DropCounters counters,
        long? scrambledPackets,
        long overflows,
        DateTime? measuredUpdatedAt,
        int gaps,
        long missedMs)
    {
        Recording = recording;
        Network = network;
        Service = service;
        Kind = kind;
        Tuner = tuner;
        StartedAt = startedAt;
        Counters = counters;
        ScrambledPackets = scrambledPackets;
        Overflows = overflows;
        MeasuredUpdatedAt = measuredUpdatedAt;
        Gaps = gaps;
        MissedMs = missedMs;
    }

    public RecordingId Recording { get; }

    public NetworkId Network { get; }

    public ServiceId Service { get; }

    public TuneSystem? Kind { get; }

    public TunerDeviceId? Tuner { get; }

    public DateTime StartedAt { get; }

    public DropCounters Counters { get; }

    public long? ScrambledPackets { get; }

    public long Overflows { get; }

    public DateTime? MeasuredUpdatedAt { get; }

    public int Gaps { get; }

    public long MissedMs { get; }

    public QualityFacet Facet => QualityFacet.OfWhatIsKnown(Kind, Network, Service, Tuner, StartedAt.Hour);

    public static QualityLedgerRow Of(
        RecordingId recording,
        NetworkId network,
        ServiceId service,
        TuneSystem? kind,
        TunerDeviceId? tuner,
        DateTime startedAt,
        DropCounters counters,
        long? scrambledPackets,
        long overflows,
        DateTime? measuredUpdatedAt,
        int gaps,
        long missedMs)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(counters);
        ArgumentOutOfRangeException.ThrowIfNegative(overflows);

        if (scrambledPackets is { } left)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(left, nameof(scrambledPackets));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(gaps);
        ArgumentOutOfRangeException.ThrowIfNegative(missedMs);

        if (gaps is 0 != missedMs is 0)
        {
            throw new ArgumentException("A recording missed something exactly when it holds a gap.", nameof(missedMs));
        }

        UtcTimes.Required(startedAt, nameof(startedAt));
        UtcTimes.Optional(measuredUpdatedAt, nameof(measuredUpdatedAt));

        return new QualityLedgerRow(
            recording,
            network,
            service,
            kind,
            tuner,
            startedAt,
            counters,
            scrambledPackets,
            overflows,
            measuredUpdatedAt,
            gaps,
            missedMs);
    }
}

public interface IQualityLedgerReader
{
    Task<IReadOnlyList<QualityLedgerRow>> ReadAsync(QualityPeriod period, CancellationToken cancellationToken);
}
