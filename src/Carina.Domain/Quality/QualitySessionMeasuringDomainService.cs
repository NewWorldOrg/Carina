using Carina.Domain.Channels;
using Carina.Domain.Recordings;

using Puppis.Contracts;

namespace Carina.Domain.Quality;

public sealed record SignalSite(TunerDeviceId Tuner, NetworkId Network, ServiceId Service);

public sealed record SignalSampleSource(
    SignalSite Site,
    SessionId Session,
    SessionPurpose Purpose,
    SignalQualityDto? Quality,
    int? PhysicalChannel)
{
    public SignalReadingAsk AskOf(string driverInstanceId)
        => new(driverInstanceId, Session, Purpose, Site.Tuner, Site.Network, Site.Service, Quality, PhysicalChannel);
}

public sealed record SignalSampleSources(IReadOnlyList<SignalSampleSource> Sources, int Unnamed)
{
    public SignalSite? SiteOf(string deviceId)
        => Sources.LastOrDefault(source => string.Equals(source.Site.Tuner.Value, deviceId, StringComparison.Ordinal))?.Site;
}

public sealed record QualitySessionTally(int Measured, int Closed);

public sealed class QualitySessionMeasuringDomainService(
    IBroadcastStreamDirectory streams,
    IQualitySessionMeasurementRepository measurements)
{
    public async Task<SignalSampleSources> LocateAsync(
        IReadOnlyList<TunerSnapshot> tuners,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tuners);

        IReadOnlyList<IntendedStream> intended = await streams.ListIntendedAsync(cancellationToken);
        List<SignalSampleSource> sources = [];
        int unnamed = 0;

        foreach (TunerSnapshot tuner in tuners)
        {
            if (tuner.CurrentSession is not { } session
                || session.SessionId.IsUnset
                || session.Tune is not { } tune
                || string.IsNullOrWhiteSpace(tuner.DeviceId))
            {
                continue;
            }

            if (SignalFiling.StreamFor(intended, tune) is not { } stream)
            {
                unnamed++;

                continue;
            }

            sources.Add(new SignalSampleSource(
                new SignalSite(new TunerDeviceId(tuner.DeviceId), stream.NetworkId, stream.Services[0]),
                session.SessionId,
                session.Purpose,
                tuner.SignalQuality,
                SignalFiling.ChannelOf(tune)));
        }

        return new SignalSampleSources(sources, unnamed);
    }

    public async Task<QualitySessionTally> MeasureAsync(
        DriverHello hello,
        IReadOnlyList<SessionSnapshot> sessions,
        SignalSampleSources sources,
        DateTime at,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(sources);

        if (string.IsNullOrEmpty(hello.InstanceId))
        {
            throw new ArgumentException("A session is measured under the driver that named itself.", nameof(hello));
        }

        string instance = hello.InstanceId;
        IReadOnlyList<QualitySessionMeasurement> open = await measurements.ListOpenAsync(cancellationToken);
        HashSet<SessionId> listedHere = [];
        int measured = 0;
        int closed = 0;

        foreach (SessionSnapshot session in sessions)
        {
            if (session.Purpose is SessionPurpose.Recording || session.SessionId.IsUnset)
            {
                continue;
            }

            listedHere.Add(session.SessionId);

            if (await MeasurementOfAsync(instance, session, open, sources, cancellationToken) is not { HasEnded: false } measurement)
            {
                continue;
            }

            RecordingSessionDto counted = RecordingSessionDto.Of(hello, session);

            if (counted is { CcMeasured: true, CcDropped: long dropped, CcTotal: long total })
            {
                measurement.Observe(dropped, total, counted.EovfCount, at);
            }

            if (session.Concluded)
            {
                measurement.Close(at);
                closed++;
            }

            await measurements.SaveAsync(measurement, cancellationToken);
            measured++;
        }

        foreach (QualitySessionMeasurement gone in open)
        {
            if (gone.DriverInstanceId == instance && listedHere.Contains(gone.Session))
            {
                continue;
            }

            gone.Close(at);
            await measurements.SaveAsync(gone, cancellationToken);
            closed++;
        }

        return new QualitySessionTally(measured, closed);
    }

    private async Task<QualitySessionMeasurement?> MeasurementOfAsync(
        string instance,
        SessionSnapshot session,
        IReadOnlyList<QualitySessionMeasurement> open,
        SignalSampleSources sources,
        CancellationToken cancellationToken)
    {
        if (open.FirstOrDefault(row => row.DriverInstanceId == instance && row.Session.Equals(session.SessionId)) is { } held)
        {
            return held;
        }

        if (session.Concluded || sources.SiteOf(session.DeviceId) is not { } site)
        {
            return null;
        }

        return await measurements.FindAsync(instance, session.SessionId, cancellationToken)
               ?? QualitySessionMeasurement.Open(
                   instance,
                   session.SessionId,
                   session.Purpose,
                   site.Tuner,
                   site.Network,
                   site.Service,
                   session.StartedAt.UtcDateTime);
    }
}
