using Carina.Contracts;
using Carina.Domain.Base;

namespace Carina.Domain.Quality;

public sealed record SignalSample
{
    private SignalSample(
        bool locked,
        DateTime lockReadAt,
        int? carrierToNoiseMilliDecibels,
        DateTime? carrierToNoiseReadAt,
        IReadOnlyList<LayerBitErrorCounts> bitErrors,
        DateTime? bitErrorsReadAt,
        IReadOnlyList<string> metricsNotRead,
        SignalNotTaken? notTakenBecause)
    {
        Locked = locked;
        LockReadAt = lockReadAt;
        CarrierToNoiseMilliDecibels = carrierToNoiseMilliDecibels;
        CarrierToNoiseReadAt = carrierToNoiseReadAt;
        BitErrors = bitErrors;
        BitErrorsReadAt = bitErrorsReadAt;
        MetricsNotRead = metricsNotRead;
        NotTakenBecause = notTakenBecause;
    }

    public bool Locked { get; }

    public DateTime LockReadAt { get; }

    public int? CarrierToNoiseMilliDecibels { get; }

    public DateTime? CarrierToNoiseReadAt { get; }

    public IReadOnlyList<LayerBitErrorCounts> BitErrors { get; }

    public DateTime? BitErrorsReadAt { get; }

    public IReadOnlyList<string> MetricsNotRead { get; }

    public SignalNotTaken? NotTakenBecause { get; }

    public bool WasTaken => NotTakenBecause is null;

    public bool CarriesAnyValue => CarrierToNoiseMilliDecibels is not null || BitErrors.Count > 0;

    public LayerBitErrorCounts? Layer(int layer)
        => BitErrors.FirstOrDefault(counts => counts.Layer == layer);

    public static SignalSample WithoutLock(DateTime lockReadAt, IReadOnlyList<string>? metricsNotRead = null)
        => new(
            false,
            UtcTimes.Required(lockReadAt, nameof(lockReadAt)),
            null,
            null,
            [],
            null,
            Named(metricsNotRead),
            null);

    public static SignalSample NotTaken(DateTime askedAt, SignalNotTaken because)
    {
        if (!Enum.IsDefined(because))
        {
            throw new ArgumentOutOfRangeException(
                nameof(because),
                because,
                "A reading that could not be taken says which way it could not be, and one this domain does not name says nothing.");
        }

        return new SignalSample(false, UtcTimes.Required(askedAt, nameof(askedAt)), null, null, [], null, [], because);
    }

    public static SignalSample WithLock(
        DateTime lockReadAt,
        int? carrierToNoiseMilliDecibels = null,
        DateTime? carrierToNoiseReadAt = null,
        IReadOnlyList<LayerBitErrorCounts>? bitErrors = null,
        DateTime? bitErrorsReadAt = null,
        IReadOnlyList<string>? metricsNotRead = null)
    {
        UtcTimes.Required(lockReadAt, nameof(lockReadAt));
        UtcTimes.Optional(carrierToNoiseReadAt, nameof(carrierToNoiseReadAt));
        UtcTimes.Optional(bitErrorsReadAt, nameof(bitErrorsReadAt));

        if ((carrierToNoiseMilliDecibels is null) != (carrierToNoiseReadAt is null))
        {
            throw new ArgumentException(
                "A carrier to noise figure and the time it was read arrive together or not at all.",
                nameof(carrierToNoiseReadAt));
        }

        IReadOnlyList<LayerBitErrorCounts> layers = Layers(bitErrors);

        if ((layers.Count > 0) != (bitErrorsReadAt is not null))
        {
            throw new ArgumentException(
                "Bit error counts and the time they were read arrive together or not at all.",
                nameof(bitErrorsReadAt));
        }

        return new SignalSample(
            true,
            lockReadAt,
            carrierToNoiseMilliDecibels,
            carrierToNoiseReadAt,
            layers,
            bitErrorsReadAt,
            Named(metricsNotRead),
            null);
    }

    private static IReadOnlyList<LayerBitErrorCounts> Layers(IReadOnlyList<LayerBitErrorCounts>? bitErrors)
    {
        if (bitErrors is null or { Count: 0 })
        {
            return [];
        }

        foreach (LayerBitErrorCounts counts in bitErrors)
        {
            ArgumentNullException.ThrowIfNull(counts);
            ArgumentOutOfRangeException.ThrowIfNegative(counts.Layer, nameof(bitErrors));
            ArgumentOutOfRangeException.ThrowIfNegative(counts.ErrorBits, nameof(bitErrors));
            ArgumentOutOfRangeException.ThrowIfNegative(counts.TotalBits, nameof(bitErrors));
        }

        if (bitErrors.Select(counts => counts.Layer).Distinct().Count() != bitErrors.Count)
        {
            throw new ArgumentException(
                "Each broadcast layer is counted once, and folding two of them together loses which one failed.",
                nameof(bitErrors));
        }

        return [.. bitErrors.OrderBy(counts => counts.Layer)];
    }

    private static IReadOnlyList<string> Named(IReadOnlyList<string>? metrics)
    {
        if (metrics is null or { Count: 0 })
        {
            return [];
        }

        foreach (string metric in metrics)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(metric, nameof(metrics));
        }

        return [.. metrics.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
    }
}
