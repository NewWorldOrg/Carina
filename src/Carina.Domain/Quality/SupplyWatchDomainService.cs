using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Domain.Quality;

public sealed class SupplyWatchDomainService(IQualitySignalSampleRepository samples)
{
    public async Task<IReadOnlyList<SupplyReading>> SignalSamplesAsync(
        IReadOnlyList<TunerSnapshot> tuners,
        DateTime now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tuners);

        IReadOnlyDictionary<TunerDeviceId, DateTime> latest = await samples.ListLastTakenAsync(cancellationToken);
        List<SupplyReading> read = [];

        foreach (TunerSnapshot tuner in tuners)
        {
            if (tuner.State is TunerState.Disabled
                || string.IsNullOrWhiteSpace(tuner.DeviceId)
                || tuner.CurrentSession is not { } session
                || session.SessionId.IsUnset
                || session.StartedAt is not { } opened)
            {
                continue;
            }

            var device = new TunerDeviceId(tuner.DeviceId);

            read.Add(SupplyReading.Of(
                SupplySilence.SignalSamples,
                QualitySubject.Of(QualitySubjectKind.Tuner, device.Value),
                LastHeard(opened.UtcDateTime, latest.TryGetValue(device, out DateTime taken) ? taken : null, now)));
        }

        return read;
    }

    private static DateTime LastHeard(DateTime opened, DateTime? taken, DateTime now)
    {
        DateTime since = opened > now ? now : opened;

        return taken is { } last && last > since ? last : since;
    }
}
