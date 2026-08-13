using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Driver;

public sealed class DriverConnectionSupervisor(
    IDriverClient client,
    DriverConnectionMonitor monitor,
    DriverSignalRelay signals,
    IDriverSessionResyncHook resyncHook,
    DriverSupervisionSettings settings,
    TimeProvider timeProvider,
    ILogger<DriverConnectionSupervisor> logger) : BackgroundService
{
    private enum ServeOutcome
    {
        NeverReached,
        Lost,
        Alive,
    }

    private DriverHello? adopted;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var backoff = new ReconnectBackoff(settings.FirstDelay, settings.DelayCap, settings.Chance);

        while (!stoppingToken.IsCancellationRequested)
        {
            var outcome = ServeOutcome.NeverReached;

            try
            {
                outcome = await ServeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "The driver connection loop failed; it will retry.");
            }

            if (outcome is not ServeOutcome.Alive)
            {
                monitor.Record(DriverObservation.NotConnected);
            }

            if (outcome is not ServeOutcome.NeverReached)
            {
                backoff.Reset();
            }

            try
            {
                await Task.Delay(backoff.Next(), timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<ServeOutcome> ServeOnceAsync(CancellationToken stoppingToken)
    {
        var health = await client.GetHealthAsync(stoppingToken);

        if (!health.TryGetValue(out var hello))
        {
            return ServeOutcome.NeverReached;
        }

        var missing = settings.ExpectedCapabilities
            .Where(capability => !hello.Supports(capability))
            .ToArray();

        if (hello.IsDifferentInstanceFrom(adopted))
        {
            var sessions = await client.GetActiveSessionsAsync(stoppingToken);

            if (!sessions.TryGetValue(out var held))
            {
                return ServeOutcome.NeverReached;
            }

            await resyncHook.ReadoptAsync(held, stoppingToken);
            adopted = hello;
        }

        var observation = DriverObservation.Of(hello, missing);
        monitor.Record(observation);

        var feed = await client.OpenEventsAsync(stoppingToken);

        if (feed.Outcome is DriverCallOutcome.Refused)
        {
            return ServeOutcome.Alive;
        }

        if (!feed.TryGetValue(out var stream))
        {
            return ServeOutcome.Lost;
        }

        try
        {
            await using (stream)
            {
                await foreach (var name in SseFrames.ReadNamesAsync(stream, stoppingToken))
                {
                    if (!DriverEvents.IsKnown(name))
                    {
                        continue;
                    }

                    if (name == DriverEvents.Draining
                        && observation.Connection is not DriverConnection.Draining)
                    {
                        observation = observation.WhileDraining();
                        monitor.Record(observation);
                    }

                    signals.Publish(name);
                }
            }
        }
        catch (Exception error) when (error is IOException or HttpRequestException)
        {
            return ServeOutcome.Lost;
        }

        return ServeOutcome.Lost;
    }
}
