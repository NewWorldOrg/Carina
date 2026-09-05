using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Quality;

public sealed class QualitySignalRollup
{
    private QualitySignalRollup()
    {
    }

    public QualityWindow Granularity { get; private set; }

    public DateTime WindowStart { get; private set; }

    public TunerDeviceId Tuner { get; private set; } = null!;

    public NetworkId Network { get; private set; } = null!;

    public ServiceId Service { get; private set; } = null!;

    public long Samples { get; private set; }

    public long Locked { get; private set; }

    public long Unmeasured { get; private set; }

    public long Unreachable { get; private set; }

    public double? CarrierToNoiseAverage { get; private set; }

    public int? CarrierToNoiseLowest { get; private set; }

    public int? CarrierToNoiseHighest { get; private set; }

    public IReadOnlyList<LayerErrorRate> BitErrors { get; private set; } = [];

    public double? LockRate => Samples is 0 ? null : (double)Locked / Samples;

    public static QualitySignalRollup Rehydrate(
        QualityWindow granularity,
        DateTime windowStart,
        TunerDeviceId tuner,
        NetworkId network,
        ServiceId service,
        long samples,
        long locked,
        long unmeasured,
        long unreachable,
        double? carrierToNoiseAverage,
        int? carrierToNoiseLowest,
        int? carrierToNoiseHighest,
        IReadOnlyList<LayerErrorRate>? bitErrors)
    {
        if (!Enum.IsDefined(granularity))
        {
            throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "A rollup covers one of the windows this domain keeps.");
        }

        ArgumentNullException.ThrowIfNull(tuner);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentOutOfRangeException.ThrowIfNegative(samples);
        ArgumentOutOfRangeException.ThrowIfNegative(locked);
        ArgumentOutOfRangeException.ThrowIfNegative(unmeasured);
        ArgumentOutOfRangeException.ThrowIfNegative(unreachable);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(locked, samples);

        int given = new[] { carrierToNoiseAverage is not null, carrierToNoiseLowest is not null, carrierToNoiseHighest is not null }
            .Count(present => present);

        if (given is not 0 and not 3)
        {
            throw new ArgumentException(
                "A window either read a carrier to noise figure or it did not, and half of one says neither.",
                nameof(carrierToNoiseAverage));
        }

        if (carrierToNoiseLowest > carrierToNoiseHighest
            || carrierToNoiseAverage < carrierToNoiseLowest
            || carrierToNoiseAverage > carrierToNoiseHighest)
        {
            throw new ArgumentException(
                "The average of a window sits between the lowest reading in it and the highest.",
                nameof(carrierToNoiseAverage));
        }

        return new QualitySignalRollup
        {
            Granularity = granularity,
            WindowStart = UtcTimes.Required(windowStart, nameof(windowStart)),
            Tuner = tuner,
            Network = network,
            Service = service,
            Samples = samples,
            Locked = locked,
            Unmeasured = unmeasured,
            Unreachable = unreachable,
            CarrierToNoiseAverage = carrierToNoiseAverage,
            CarrierToNoiseLowest = carrierToNoiseLowest,
            CarrierToNoiseHighest = carrierToNoiseHighest,
            BitErrors = Layers(bitErrors),
        };
    }

    private static IReadOnlyList<LayerErrorRate> Layers(IReadOnlyList<LayerErrorRate>? bitErrors)
    {
        if (bitErrors is null or { Count: 0 })
        {
            return [];
        }

        foreach (LayerErrorRate rate in bitErrors)
        {
            ArgumentNullException.ThrowIfNull(rate);
            ArgumentOutOfRangeException.ThrowIfNegative(rate.Layer, nameof(bitErrors));
            ArgumentOutOfRangeException.ThrowIfNegative(rate.Average, nameof(bitErrors));
            ArgumentOutOfRangeException.ThrowIfLessThan(rate.Highest, rate.Average, nameof(bitErrors));
        }

        if (bitErrors.Select(rate => rate.Layer).Distinct().Count() != bitErrors.Count)
        {
            throw new ArgumentException(
                "Each broadcast layer is rolled up on its own, and folding two of them together loses which one failed.",
                nameof(bitErrors));
        }

        return [.. bitErrors.OrderBy(rate => rate.Layer)];
    }
}
