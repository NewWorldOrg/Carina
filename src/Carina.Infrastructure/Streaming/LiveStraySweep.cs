using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Streaming;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Streaming;

/// <summary>
/// Lets go of the live sessions the driver holds that no viewer in this app is behind.
/// </summary>
/// <remarks>
/// The driver deliberately outlives the app so a recording in progress survives a deployment. A
/// live session has no file to finish, so the same independence leaves it holding a tuner for a
/// viewer that went away with the app that seated them, until its own window closes hours later.
/// Nothing else clears it: the ledger this app answers from is what it raised itself, so it does
/// not even list one it did not raise.
///
/// This assumes one app to a driver, which is what the unix socket between them gives. A second
/// app against the same driver would read the first one's sessions as strays.
/// </remarks>
public sealed class LiveStraySweep(
    IDriverClient driver,
    ILiveLeases leases,
    LiveStraySettings settings,
    TimeProvider clock,
    ILogger<LiveStraySweep> logger) : BackgroundService
{
    public const string LetGoBecause = "no viewer in this app is behind this session";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstSweep;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(waiting, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            waiting = settings.BetweenSweeps;

            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A sweep for stray live sessions failed; the next one is unaffected.");
            }
        }
    }

    public async Task SweepAsync(CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<SessionSnapshot>> asked = await driver.GetActiveSessionsAsync(cancellationToken);

        if (!asked.TryGetValue(out IReadOnlyList<SessionSnapshot>? held))
        {
            return;
        }

        IReadOnlyCollection<SessionId> ours = leases.Held;

        foreach (SessionSnapshot stray in held.Where(session => Stray(session, ours)))
        {
            logger.LogWarning(
                "The driver holds the live session {SessionId} on {DeviceId}, started at {StartedAt} and open "
                + "until {EndsAt}, that no viewer in this app is behind; letting it go so the tuner is free.",
                stray.SessionId.Value,
                stray.DeviceId,
                stray.StartedAt,
                stray.EndsAt);

            await driver.StopSessionAsync(stray.SessionId, LetGoBecause, cancellationToken);
        }
    }

    private static bool Stray(SessionSnapshot session, IReadOnlyCollection<SessionId> ours)
        => session.Purpose is SessionPurpose.Live
           && !session.Concluded
           && !ours.Contains(session.SessionId);
}
