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
        Draining,
        FeedEnded,
    }

    private readonly DriverReconnectCadence cadence = new(settings);

    private DriverHello? adopted;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var serve = Serve.Of(ServeOutcome.NeverReached);

            try
            {
                serve = await ServeOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "The driver connection loop failed; it will retry.");
            }

            if (serve.Outcome is not (ServeOutcome.Alive or ServeOutcome.Draining))
            {
                monitor.Record(DriverObservation.NotConnected);
            }

            var pause = serve.Outcome switch
            {
                ServeOutcome.Draining => cadence.WhileDraining(),
                ServeOutcome.FeedEnded => cadence.AfterFeed(serve.Held),
                _ => cadence.AfterSetback(),
            };

            try
            {
                await Task.Delay(pause, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<Serve> ServeOnceAsync(CancellationToken stoppingToken)
    {
        var health = await client.GetHealthAsync(stoppingToken);

        if (!health.TryGetValue(out var hello))
        {
            return Serve.Of(ServeOutcome.NeverReached);
        }

        var missing = settings.ExpectedCapabilities
            .Where(capability => !hello.Supports(capability))
            .ToArray();

        var observation = DriverObservation.Of(hello, missing);
        monitor.Record(observation);

        if (hello.IsDifferentInstanceFrom(adopted))
        {
            var sessions = await client.GetActiveSessionsAsync(stoppingToken);

            if (sessions.Outcome is DriverCallOutcome.Unreachable)
            {
                logger.LogWarning(
                    "The driver answered its hello but its session list did not arrive: {Failure}",
                    sessions.Failure);

                return Serve.Of(ServeOutcome.Lost);
            }

            if (!sessions.TryGetValue(out var held))
            {
                logger.LogWarning(
                    "The driver refused its session list ({Problem}); it stays connected and readoption retries.",
                    sessions.Problem?.Title);

                return Serve.Of(ServeOutcome.Alive);
            }

            if (!await ReadoptAsync(held, stoppingToken))
            {
                return Serve.Of(ServeOutcome.Alive);
            }

            var previous = adopted;
            adopted = hello;

            if (previous is not null)
            {
                signals.Publish(DriverClientSignals.InstanceChanged);
            }
        }

        if (observation.Connection is DriverConnection.Draining)
        {
            return Serve.Of(ServeOutcome.Draining);
        }

        var feed = await client.OpenEventsAsync(stoppingToken);

        if (feed.Outcome is DriverCallOutcome.Refused)
        {
            return Serve.Of(ServeOutcome.Alive);
        }

        if (!feed.TryGetValue(out var stream))
        {
            return Serve.Of(ServeOutcome.Lost);
        }

        var opened = timeProvider.GetTimestamp();

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
            return new Serve(ServeOutcome.FeedEnded, timeProvider.GetElapsedTime(opened));
        }

        return new Serve(ServeOutcome.FeedEnded, timeProvider.GetElapsedTime(opened));
    }

    private async Task<bool> ReadoptAsync(
        IReadOnlyList<SessionSnapshot> held,
        CancellationToken stoppingToken)
    {
        try
        {
            await resyncHook.ReadoptAsync(held, stoppingToken);

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            logger.LogError(
                error,
                "Readopting the sessions held by the driver failed; it stays connected and readoption retries.");

            return false;
        }
    }

    private readonly record struct Serve(ServeOutcome Outcome, TimeSpan Held)
    {
        public static Serve Of(ServeOutcome outcome) => new(outcome, TimeSpan.Zero);
    }
}
