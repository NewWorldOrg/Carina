using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Quality;

public sealed record SignalSampleTaking(int Taken, int NotTaken, int Unnamed);

public sealed class SignalSampleRound(
    IDriverClient driver,
    IBroadcastStreamDirectory streams,
    IQualitySignalSampleRepository samples,
    TimeProvider clock,
    ILogger<SignalSampleRound> logger)
{
    public async Task<SignalSampleTaking> TakeAsync(CancellationToken cancellationToken)
    {
        DriverCall<DriverHello> greeting = await driver.GetHealthAsync(cancellationToken);

        if (!greeting.TryGetValue(out DriverHello? hello) || string.IsNullOrEmpty(hello.InstanceId))
        {
            return new SignalSampleTaking(0, 0, 0);
        }

        DriverCall<IReadOnlyList<TunerSnapshot>> asked = await driver.GetTunersAsync(cancellationToken);

        if (!asked.TryGetValue(out IReadOnlyList<TunerSnapshot>? tuners))
        {
            return new SignalSampleTaking(0, 0, 0);
        }

        DateTime at = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<IntendedStream> intended = await streams.ListIntendedAsync(cancellationToken);
        List<QualitySignalSample> taking = [];
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

            if (Named(intended, tune) is not { } channel)
            {
                unnamed++;

                continue;
            }

            taking.Add(SignalSampleIntake.Take(
                new SignalReadingAsk(
                    hello.InstanceId,
                    session.SessionId,
                    session.Purpose,
                    new TunerDeviceId(tuner.DeviceId),
                    channel.Network,
                    channel.Service,
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

        return new SignalSampleTaking(
            taking.Count(sample => sample.Signal.WasTaken),
            taking.Count(sample => !sample.Signal.WasTaken),
            unnamed);
    }

    private static Channel? Named(IReadOnlyList<IntendedStream> intended, TuneParams tune)
    {
        foreach (IntendedStream stream in intended)
        {
            if (stream.Services.Count > 0 && Same(stream.Tuning, tune))
            {
                return new Channel(stream.NetworkId, stream.Services[0]);
            }
        }

        return null;
    }

    private static bool Same(TuningParameters tuning, TuneParams tune)
        => tuning.System == tune.System
           && tuning.PhysicalChannel == Slot(tune)
           && (tune.System is not TuneSystem.IsdbSBs || tuning.TransportStreamId?.Value == tune.IsdbSBs?.Tsid);

    private static int? Slot(TuneParams tune) => tune.System switch
    {
        TuneSystem.IsdbT => tune.IsdbT?.PhysicalChannel,
        TuneSystem.IsdbSBs => tune.IsdbSBs?.BsChannel,
        TuneSystem.IsdbSCs110 => tune.IsdbSCs110?.CsChannel,
        _ => null,
    };

    private readonly record struct Channel(NetworkId Network, ServiceId Service);
}
