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

public readonly record struct LockWindow(FrontendStatus Before, FrontendStatus After)
{
    public static LockWindow Throughout(FrontendStatus status) => new(status, status);

    public bool HeldThroughout => Held(Before) && Held(After);

    public bool HeldAtNeitherEnd => !Held(Before) && !Held(After);

    public bool Wavered => !HeldThroughout && !HeldAtNeitherEnd;

    private static bool Held(FrontendStatus status) => status.HasFlag(FrontendStatus.Lock);
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

    public static CarrierToNoise Measured(double decibels) => new(SignalReading.Measured, decibels);

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
        var countable = TotalBits is not 0 && ErrorBits <= TotalBits;
        rate = countable ? (double)ErrorBits / TotalBits : double.NaN;

        return countable;
    }
}

public sealed record PostViterbiErrors(SignalReading Reading, IReadOnlyList<LayerBitErrors> Layers)
{
    public static readonly PostViterbiErrors None = new(SignalReading.Unspecified, []);
}

public sealed record SignalQuality(
    LockWindow Locked,
    CarrierToNoise CarrierToNoise,
    PostViterbiErrors PostViterbiErrors
)
{
    public bool HasLock => Locked.HeldThroughout;

    public FrontendStatus Status => Locked.After;
}

public static class SignalQualityReading
{
    private const int MillidecibelsPerDecibel = 1_000;

    public static CarrierToNoise CarrierToNoiseFrom(
        LockWindow locked,
        IReadOnlyList<DvbStatisticLayer> layers
    )
    {
        if (layers.Count is 0)
        {
            return CarrierToNoise.NotImplemented;
        }

        if (locked.HeldAtNeitherEnd)
        {
            return CarrierToNoise.WithoutLock;
        }

        if (locked.Wavered)
        {
            return CarrierToNoise.Unavailable;
        }

        if (layers[0].Scale is not StatisticScale.Decibel)
        {
            return CarrierToNoise.Unavailable;
        }

        return CarrierToNoise.Measured(layers[0].Value / (double)MillidecibelsPerDecibel);
    }

    public static PostViterbiErrors PostViterbiFrom(
        LockWindow locked,
        IReadOnlyList<DvbStatisticLayer> errorBits,
        IReadOnlyList<DvbStatisticLayer> totalBits
    )
    {
        if (errorBits.Count is 0 && totalBits.Count is 0)
        {
            return new PostViterbiErrors(SignalReading.NotImplementedByThisTuner, []);
        }

        if (locked.HeldAtNeitherEnd)
        {
            return new PostViterbiErrors(SignalReading.FrontendNotLocked, []);
        }

        if (locked.Wavered || errorBits.Count != totalBits.Count)
        {
            return new PostViterbiErrors(SignalReading.UnavailableRightNow, []);
        }

        var layers = new LayerBitErrors[errorBits.Count];

        for (var layer = 0; layer < errorBits.Count; layer++)
        {
            if (!Countable(errorBits[layer]) || !Countable(totalBits[layer]))
            {
                return new PostViterbiErrors(SignalReading.UnavailableRightNow, []);
            }

            if (errorBits[layer].Value > totalBits[layer].Value)
            {
                return new PostViterbiErrors(SignalReading.UnavailableRightNow, []);
            }

            layers[layer] = new LayerBitErrors(
                layer,
                (ulong)errorBits[layer].Value,
                (ulong)totalBits[layer].Value
            );
        }

        return new PostViterbiErrors(SignalReading.Measured, layers);
    }

    private static bool Countable(DvbStatisticLayer layer) =>
        layer.Scale is StatisticScale.Counter && layer.Value >= 0;
}
