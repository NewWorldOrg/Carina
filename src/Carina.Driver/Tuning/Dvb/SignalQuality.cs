namespace Carina.Driver.Tuning.Dvb;

[Flags]
public enum FrontendStatus
{
    None = 0,

    Signal = 0x01,

    Carrier = 0x02,

    Viterbi = 0x04,

    Sync = 0x08,

    Lock = 0x10,

    TimedOut = 0x20,

    Reinitialised = 0x40,
}

public enum SignalReading
{
    Unspecified = 0,

    Measured = 1,

    FrontendNotLocked = 2,

    NotImplementedByThisTuner = 3,

    UnavailableRightNow = 4,
}

public readonly struct CarrierToNoise : IEquatable<CarrierToNoise>
{
    private readonly double decibels;

    private CarrierToNoise(SignalReading reading, double decibels)
    {
        Reading = reading;
        this.decibels = decibels;
    }

    public SignalReading Reading { get; }

    public static readonly CarrierToNoise WithoutLock = Nothing(SignalReading.FrontendNotLocked);

    public static readonly CarrierToNoise NotImplemented = Nothing(
        SignalReading.NotImplementedByThisTuner
    );

    public static readonly CarrierToNoise Unavailable = Nothing(SignalReading.UnavailableRightNow);

    public static CarrierToNoise Measured(double decibels) =>
        new(SignalReading.Measured, decibels);

    public bool TryGetDecibels(out double value)
    {
        value = Reading is SignalReading.Measured ? decibels : double.NaN;

        return Reading is SignalReading.Measured;
    }

    public bool Equals(CarrierToNoise other) =>
        Reading == other.Reading && decibels.Equals(other.decibels);

    public override bool Equals(object? other) => other is CarrierToNoise reading && Equals(reading);

    public override int GetHashCode() => HashCode.Combine(Reading, decibels);

    public static bool operator ==(CarrierToNoise left, CarrierToNoise right) => left.Equals(right);

    public static bool operator !=(CarrierToNoise left, CarrierToNoise right) => !left.Equals(right);

    private static CarrierToNoise Nothing(SignalReading reading) => new(reading, double.NaN);
}

public readonly record struct LayerBitErrors(int Layer, ulong ErrorBits, ulong TotalBits)
{
    public bool TryGetErrorRate(out double rate)
    {
        rate = TotalBits is 0 ? double.NaN : (double)ErrorBits / TotalBits;

        return TotalBits is not 0;
    }
}

public sealed record PostViterbiErrors(SignalReading Reading, IReadOnlyList<LayerBitErrors> Layers)
{
    public static readonly PostViterbiErrors None = new(SignalReading.Unspecified, []);
}

public sealed record SignalQuality(
    FrontendStatus Status,
    CarrierToNoise CarrierToNoise,
    PostViterbiErrors PostViterbiErrors
)
{
    public bool HasLock => Status.HasFlag(FrontendStatus.Lock);
}

public static class SignalQualityReading
{
    public static CarrierToNoise CarrierToNoiseFrom(
        FrontendStatus status,
        IReadOnlyList<DvbStatisticLayer> layers
    )
    {
        if (layers.Count is 0)
        {
            return CarrierToNoise.NotImplemented;
        }

        if (!status.HasFlag(FrontendStatus.Lock))
        {
            return CarrierToNoise.WithoutLock;
        }

        if (layers[0].Scale is not StatisticScale.Decibel)
        {
            return CarrierToNoise.Unavailable;
        }

        return CarrierToNoise.Measured(layers[0].Value / (double)MillidecibelsPerDecibel);
    }

    public static PostViterbiErrors PostViterbiFrom(
        FrontendStatus status,
        IReadOnlyList<DvbStatisticLayer> errorBits,
        IReadOnlyList<DvbStatisticLayer> totalBits
    )
    {
        if (errorBits.Count is 0 && totalBits.Count is 0)
        {
            return new PostViterbiErrors(SignalReading.NotImplementedByThisTuner, []);
        }

        if (!status.HasFlag(FrontendStatus.Lock))
        {
            return new PostViterbiErrors(SignalReading.FrontendNotLocked, []);
        }

        if (errorBits.Count != totalBits.Count)
        {
            return new PostViterbiErrors(SignalReading.UnavailableRightNow, []);
        }

        var layers = new LayerBitErrors[errorBits.Count];

        for (var layer = 0; layer < errorBits.Count; layer++)
        {
            if (
                errorBits[layer].Scale is not StatisticScale.Counter
                || totalBits[layer].Scale is not StatisticScale.Counter
            )
            {
                return new PostViterbiErrors(SignalReading.UnavailableRightNow, []);
            }

            layers[layer] = new LayerBitErrors(
                layer,
                unchecked((ulong)errorBits[layer].Value),
                unchecked((ulong)totalBits[layer].Value)
            );
        }

        return new PostViterbiErrors(SignalReading.Measured, layers);
    }

    private const int MillidecibelsPerDecibel = 1_000;
}
