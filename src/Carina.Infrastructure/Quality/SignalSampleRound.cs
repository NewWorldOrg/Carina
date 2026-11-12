using Carina.Domain.Driver;
using Carina.Domain.Quality;

using Microsoft.Extensions.Logging;

using Puppis.Contracts;

namespace Carina.Infrastructure.Quality;

public sealed record SignalSampleTaking(int Taken, int NotTaken, int Unnamed, int Measured, int Closed);

public sealed class SignalSampleRound(
    IDriverClient driver,
    QualitySessionMeasuringDomainService measuring,
    IQualitySignalSampleRepository samples,
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
        SignalSampleSources sources = await measuring.LocateAsync(tuners, cancellationToken);
        List<QualitySignalSample> taking =
        [
            .. sources.Sources.Select(source => SignalSampleIntake.Take(source.AskOf(hello.InstanceId), at)),
        ];

        await samples.AddAsync(taking, cancellationToken);

        if (sources.Unnamed > 0)
        {
            logger.LogWarning(
                "{Unnamed} session(s) were held on a multiplex no candidate channel names, so what they measured has no channel to be filed under.",
                sources.Unnamed);
        }

        QualitySessionTally tally = await MeasureSessionsAsync(hello, sources, at, cancellationToken);

        return new SignalSampleTaking(
            taking.Count(sample => sample.Signal.WasTaken),
            taking.Count(sample => !sample.Signal.WasTaken),
            sources.Unnamed,
            tally.Measured,
            tally.Closed);
    }

    private async Task<QualitySessionTally> MeasureSessionsAsync(
        DriverHello hello,
        SignalSampleSources sources,
        DateTime at,
        CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<SessionSnapshot>> listed = await driver.GetActiveSessionsAsync(cancellationToken);

        return listed.TryGetValue(out IReadOnlyList<SessionSnapshot>? sessions)
            ? await measuring.MeasureAsync(hello, sessions, sources, at, cancellationToken)
            : new QualitySessionTally(0, 0);
    }
}
