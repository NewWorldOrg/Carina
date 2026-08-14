using Carina.Contracts;
using Carina.Driver.Tuning;
using Carina.Driver.Tuning.Dvb;

namespace Carina.Driver.Ipc;

public static class SignalQualityViews
{
    private const int MillidecibelsPerDecibel = 1_000;

    public static SignalQualityDto Of(SignalQualitySample sample)
    {
        if (sample.Quality is not { } quality)
        {
            return new SignalQualityDto
            {
                Lock = SignalLock.Unspecified,
                MeasuredAt = sample.MeasuredAt,
                LockReadAt = sample.LockReadAt,
            };
        }

        var reading = new SignalQualityDto
        {
            Lock = LockIn(quality),
            MeasuredAt = sample.MeasuredAt,
            LockReadAt = sample.LockReadAt,
            NotImplementedMetrics = NotImplementedIn(quality),
        };

        if (quality.CarrierToNoise.TryGetDecibels(out var decibels))
        {
            reading = reading with
            {
                CnrMilliDecibels = (int)Math.Round(decibels * MillidecibelsPerDecibel),
            };
        }

        if (quality.PostViterbiErrors.Reading is SignalReading.Measured)
        {
            reading = reading with
            {
                PostViterbiBitErrors =
                [
                    .. quality.PostViterbiErrors.Layers.Select(layer => new LayerBitErrorCounts(
                        layer.Layer,
                        (long)layer.ErrorBits,
                        (long)layer.TotalBits
                    )),
                ],
            };
        }

        return reading;
    }

    private static SignalLock LockIn(SignalQuality quality)
    {
        if (quality.HasLock)
        {
            return SignalLock.Locked;
        }

        return quality.Locked.HeldAtNeitherEnd ? SignalLock.NotLocked : SignalLock.Unspecified;
    }

    private static IReadOnlyList<string> NotImplementedIn(SignalQuality quality)
    {
        var missing = new List<string>();

        if (quality.CarrierToNoise.Reading is SignalReading.NotImplementedByThisTuner)
        {
            missing.Add(SignalQualityMetrics.Cnr);
        }

        if (quality.PostViterbiErrors.Reading is SignalReading.NotImplementedByThisTuner)
        {
            missing.Add(SignalQualityMetrics.PostViterbiBitError);
        }

        return missing;
    }
}
