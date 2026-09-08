using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Quality;

public sealed record SignalReadingAsk(
    string DriverInstanceId,
    SessionId Session,
    SessionPurpose Purpose,
    TunerDeviceId Tuner,
    NetworkId Network,
    ServiceId Service,
    SignalQualityDto? Quality);

public static class SignalSampleIntake
{
    public static QualitySignalSample Take(SignalReadingAsk ask, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(ask);

        return QualitySignalSample.Rehydrate(
            ask.DriverInstanceId,
            ask.Session,
            at,
            ask.Purpose,
            ask.Tuner,
            ask.Network,
            ask.Service,
            Read(ask.Quality, at));
    }

    public static SignalSample Read(SignalQualityDto? quality, DateTime askedAt)
    {
        if (quality is null || quality.Lock is SignalLock.Unspecified)
        {
            return SignalSample.NotTaken(askedAt, SignalNotTaken.NothingReported);
        }

        if (quality.LockReadAt?.UtcDateTime is not { } lockReadAt)
        {
            return SignalSample.NotTaken(askedAt, SignalNotTaken.NoTimeGiven);
        }

        IReadOnlyList<string> notRead = NotRead(quality);

        if (quality.Lock is SignalLock.NotLocked)
        {
            return SignalSample.WithoutLock(lockReadAt, notRead);
        }

        int? carrierToNoise = quality.CnrMilliDecibels;
        IReadOnlyList<LayerBitErrorCounts> bitErrors = quality.PostViterbiBitErrors;
        DateTime? measuredAt = quality.MeasuredAt?.UtcDateTime;

        if ((carrierToNoise is not null || bitErrors.Count > 0) && measuredAt is null)
        {
            return SignalSample.NotTaken(askedAt, SignalNotTaken.NoTimeGiven);
        }

        if (!Countable(bitErrors))
        {
            return SignalSample.NotTaken(askedAt, SignalNotTaken.FiguresRefused);
        }

        return SignalSample.WithLock(
            lockReadAt,
            carrierToNoise,
            carrierToNoise is null ? null : measuredAt,
            bitErrors,
            bitErrors.Count is 0 ? null : measuredAt,
            notRead);
    }

    private static IReadOnlyList<string> NotRead(SignalQualityDto quality)
        => [.. quality.NotImplementedMetrics.Concat(quality.MetricsOnAnotherScale)];

    private static bool Countable(IReadOnlyList<LayerBitErrorCounts> bitErrors)
    {
        foreach (LayerBitErrorCounts counts in bitErrors)
        {
            if (counts is null || counts.Layer < 0 || counts.ErrorBits < 0 || counts.TotalBits < 0)
            {
                return false;
            }
        }

        return bitErrors.Select(counts => counts.Layer).Distinct().Count() == bitErrors.Count;
    }
}
