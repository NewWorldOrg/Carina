using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Quality;

public sealed record SignalSampleTaking(int Taken, int NotTaken, int Unnamed, int Measured, int Closed);

public sealed class SignalSampleRound(
    IDriverClient driver,
    IBroadcastStreamDirectory streams,
    IQualitySignalSampleRepository samples,
    IQualitySessionMeasurementRepository measurements,
    TimeProvider clock,
    ILogger<SignalSampleRound> logger)
{
    private static readonly SignalSampleTaking NothingTaken = new(0, 0, 0, 0, 0);

    public async Task<SignalSampleTaking> TakeAsync(CancellationToken cancellationToken)
    {
        DriverCall<DriverHello> greeting = await driver.GetHealthAsync(cancellationToken);

        if (!greeting.TryGetValue(out DriverHello? hello) || string.IsNullOrEmpty(hello.InstanceId))
        {
            return NothingTaken;
        }

        DriverCall<IReadOnlyList<TunerSnapshot>> asked = await driver.GetTunersAsync(cancellationToken);

        if (!asked.TryGetValue(out IReadOnlyList<TunerSnapshot>? tuners))
        {
            return NothingTaken;
        }

        DateTime at = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<IntendedStream> intended = await streams.ListIntendedAsync(cancellationToken);
        List<QualitySignalSample> taking = [];
        var held = new Dictionary<string, Whereabouts>(StringComparer.Ordinal);
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

            var whereabouts = new Whereabouts(new TunerDeviceId(tuner.DeviceId), stream.NetworkId, stream.Services[0]);
            held[tuner.DeviceId] = whereabouts;

            taking.Add(SignalSampleIntake.Take(
                new SignalReadingAsk(
                    hello.InstanceId,
                    session.SessionId,
                    session.Purpose,
                    whereabouts.Tuner,
                    whereabouts.Network,
                    whereabouts.Service,
                    tuner.SignalQuality),
                at));
        }

        await samples.AddAsync(taking, cancellationToken);

        if (unnamed > 0)
        {
            logger.LogWarning(
                "{Unnamed} session(s) were held on a multiplex no candidate channel names, so what they measured has no channel to be filed under.",
                unnamed);
        }

        SessionTally tally = await MeasureSessionsAsync(hello, hello.InstanceId, held, at, cancellationToken);

        return new SignalSampleTaking(
            taking.Count(sample => sample.Signal.WasTaken),
            taking.Count(sample => !sample.Signal.WasTaken),
            unnamed,
            tally.Measured,
            tally.Closed);
    }

    private async Task<SessionTally> MeasureSessionsAsync(
        DriverHello hello,
        string instance,
        IReadOnlyDictionary<string, Whereabouts> held,
        DateTime at,
        CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<SessionSnapshot>> listed = await driver.GetActiveSessionsAsync(cancellationToken);

        if (!listed.TryGetValue(out IReadOnlyList<SessionSnapshot>? sessions))
        {
            return new SessionTally(0, 0);
        }

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

            QualitySessionMeasurement? measurement =
                open.FirstOrDefault(row => row.DriverInstanceId == instance && row.Session.Equals(session.SessionId));

            if (measurement is null)
            {
                if (session.Concluded || !held.TryGetValue(session.DeviceId, out Whereabouts? whereabouts))
                {
                    continue;
                }

                measurement = await measurements.FindAsync(instance, session.SessionId, cancellationToken)
                    ?? QualitySessionMeasurement.Open(
                        instance,
                        session.SessionId,
                        session.Purpose,
                        whereabouts.Tuner,
                        whereabouts.Network,
                        whereabouts.Service,
                        session.StartedAt.UtcDateTime);
            }

            if (measurement.HasEnded)
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
                measurement.Close(EndOf(measurement, at));
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

            gone.Close(EndOf(gone, at));
            await measurements.SaveAsync(gone, cancellationToken);
            closed++;
        }

        return new SessionTally(measured, closed);
    }

    private static DateTime EndOf(QualitySessionMeasurement measurement, DateTime at)
        => at < measurement.StartedAt ? measurement.StartedAt : at;

    private sealed record Whereabouts(TunerDeviceId Tuner, NetworkId Network, ServiceId Service);

    private sealed record SessionTally(int Measured, int Closed);
}
