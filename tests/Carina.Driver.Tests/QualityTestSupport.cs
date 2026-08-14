using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Tests;

public static class Readings
{
    public const FrontendStatus Locked =
        FrontendStatus.Signal
        | FrontendStatus.Carrier
        | FrontendStatus.Viterbi
        | FrontendStatus.Sync
        | FrontendStatus.Lock;

    public static SignalQuality Measured(
        double decibels = 20.5,
        params LayerBitErrors[] layers
    ) =>
        new(
            LockWindow.Throughout(Locked),
            CarrierToNoise.Measured(decibels),
            new PostViterbiErrors(
                SignalReading.Measured,
                layers.Length is 0
                    ?
                    [
                        new LayerBitErrors(0, 12, 1_000_000),
                        new LayerBitErrors(1, 0, 500_000),
                    ]
                    : layers
            )
        );

    public static SignalQuality WithoutLock() =>
        new(
            LockWindow.Throughout(FrontendStatus.Signal),
            CarrierToNoise.WithoutLock,
            new PostViterbiErrors(SignalReading.FrontendNotLocked, [])
        );

    public static SignalQuality Wavering() =>
        new(
            new LockWindow(Locked, FrontendStatus.Signal),
            CarrierToNoise.Unavailable,
            new PostViterbiErrors(SignalReading.UnavailableRightNow, [])
        );

    public static SignalQuality WithoutCarrierToNoise() =>
        new(
            LockWindow.Throughout(Locked),
            CarrierToNoise.NotImplemented,
            new PostViterbiErrors(
                SignalReading.Measured,
                [new LayerBitErrors(0, 12, 1_000_000)]
            )
        );
}

public sealed class ScriptedQualitySource(ManualTimeProvider? clock = null) : ISignalQualitySource
{
    private readonly Queue<SignalQuality> readings = new();

    public int Reads { get; private set; }

    public SignalQuality Standing { get; set; } = Readings.Measured();

    public TimeSpan ReadingTakes { get; set; } = TimeSpan.Zero;

    public int? RefuseFromReadNumber { get; set; }

    public ScriptedQualitySource Answer(params SignalQuality[] qualities)
    {
        foreach (var quality in qualities)
        {
            readings.Enqueue(quality);
        }

        return this;
    }

    public SignalQuality Measure()
    {
        Reads++;
        clock?.Advance(ReadingTakes);

        if (RefuseFromReadNumber is { } refuseFrom && Reads >= refuseFrom)
        {
            throw DvbFailure.AtDevice(
                "/dev/dvb/adapter0/frontend0",
                "reading the signal statistics",
                Errno.NoSuchDevice,
                "The driver reports no quality rather than a number it cannot stand behind."
            );
        }

        return readings.Count > 0 ? readings.Dequeue() : Standing;
    }
}

public sealed class PacedTunerDeviceFactory(Func<ScriptedQualitySource?> signal)
    : ITunerDeviceFactory
{
    private readonly List<PacedTunerDevice> made = [];

    public PacedTunerDeviceFactory(ScriptedQualitySource? signal = null)
        : this(() => signal) { }

    public IReadOnlyList<PacedTunerDevice> Made => made;

    public PacedTunerDevice Last => made[^1];

    public ITunerDevice Create(DeviceSettings device, TuningRequest tuning, TuneParams? tune)
    {
        var paced = new PacedTunerDevice { Signal = signal() };
        made.Add(paced);

        return paced;
    }
}
