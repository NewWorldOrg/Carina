using System.Buffers;
using System.Collections.Concurrent;

using Carina.Broadcast.Tables;
using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Collection;

public sealed class RideAlongHarvester(
    IServiceScopeFactory scopes,
    IDriverClient driver,
    CollectionSettings settings,
    TimeProvider clock,
    ILogger<RideAlongHarvester> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<SessionId, Task> riding = [];

    private readonly ConcurrentDictionary<SessionId, byte> ridden = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.RidesAlong)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await JoinWhatIsOpenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogWarning(failure, "Looking for sessions to ride along with failed; it retries.");
            }

            try
            {
                await Task.Delay(settings.BetweenSessionChecks, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        await Task.WhenAll(riding.Values.ToArray());
    }

    private async Task JoinWhatIsOpenAsync(CancellationToken stoppingToken)
    {
        DriverCall<IReadOnlyList<SessionSnapshot>> open = await driver.GetActiveSessionsAsync(stoppingToken);

        if (!open.TryGetValue(out IReadOnlyList<SessionSnapshot>? sessions))
        {
            return;
        }

        ForgetSessionsThatHaveEnded(sessions);

        foreach (SessionSnapshot session in sessions.Where(CarriesAGuideWeDoNotAlreadyAskFor))
        {
            SessionId sessionId = session.SessionId;

            if (!ridden.TryAdd(sessionId, 0))
            {
                continue;
            }

            Task rider = Task.Run(() => RideAsync(sessionId, stoppingToken), CancellationToken.None);

            riding[sessionId] = rider;
            _ = rider.ContinueWith(
                _ => riding.TryRemove(sessionId, out Task? _),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void ForgetSessionsThatHaveEnded(IReadOnlyList<SessionSnapshot> open)
    {
        var live = open.Select(session => session.SessionId).ToHashSet();

        foreach (SessionId sessionId in ridden.Keys.Where(known => !live.Contains(known)))
        {
            ridden.TryRemove(sessionId, out byte _);
        }
    }

    private static bool CarriesAGuideWeDoNotAlreadyAskFor(SessionSnapshot session)
        => session.State is SessionState.Active
            && session.Purpose is SessionPurpose.Recording or SessionPurpose.Live;

    private async Task RideAsync(SessionId sessionId, CancellationToken stoppingToken)
    {
        DriverCall<Stream> opened = await driver.OpenSessionStreamAsync(
            sessionId,
            DriverEndpoints.PiggybackSubscriber,
            stoppingToken);

        if (!opened.TryGetValue(out Stream? stream))
        {
            logger.LogDebug(
                "Riding along with {SessionId} was refused: {Problem}",
                sessionId.Value,
                opened.Problem?.Title ?? opened.Failure);

            return;
        }

        var harvest = new StreamHarvest();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 188);
        long lastSaved = clock.GetTimestamp();

        try
        {
            await using (stream)
            {
                while (true)
                {
                    int got = await stream.ReadAsync(buffer, stoppingToken);

                    if (got == 0)
                    {
                        break;
                    }

                    harvest.Push(buffer.AsSpan(0, got));

                    if (clock.GetElapsedTime(lastSaved) < settings.BetweenRideAlongSaves)
                    {
                        continue;
                    }

                    await SaveAsync(harvest, sessionId, stoppingToken);
                    lastSaved = clock.GetTimestamp();
                }
            }
        }
        catch (Exception ending) when (ending is OperationCanceledException or IOException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        await SaveAsync(harvest, sessionId, CancellationToken.None);
    }

    private async Task SaveAsync(StreamHarvest harvest, SessionId sessionId, CancellationToken cancellationToken)
    {
        IReadOnlyList<EventInformationTable> gathered = harvest.TakeWhatIsGathered();

        if (gathered.Count == 0)
        {
            return;
        }

        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            ProgrammesWritten written = await scope.ServiceProvider
                .GetRequiredService<ProgrammeWriter>()
                .WriteAsync(gathered, cancellationToken);

            logger.LogInformation(
                "Riding along with {SessionId} added {Added} and updated {Updated} programme(s).",
                sessionId.Value,
                written.Added,
                written.Updated);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(
                failure,
                "What riding along with {SessionId} gathered could not be written down.",
                sessionId.Value);
        }
    }
}
