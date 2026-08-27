using Carina.Contracts;
using Carina.Domain.Driver;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed class RecordingStreamJob(
    RecordingStreamSupervisor supervisor,
    IDriverSignals signals,
    RecordingWatchSettings settings,
    TimeProvider clock,
    ILogger<RecordingStreamJob> logger) : BackgroundService
{
    private CancellationTokenSource? waking;

    public static bool WakesOn(string name) => string.Equals(name, DriverEvents.RecordingProgress, StringComparison.Ordinal);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IDisposable subscription = signals.Subscribe(Woken);
        TimeSpan waiting = settings.BeforeFirstWatch;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await WaitAsync(waiting, stoppingToken))
            {
                break;
            }

            waiting = settings.BetweenWatches;

            try
            {
                Report(await supervisor.WatchAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A recording watch failed; the next one is unaffected.");
            }
        }
    }

    private void Woken(string name)
    {
        if (!WakesOn(name))
        {
            return;
        }

        try
        {
            Volatile.Read(ref waking)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task<bool> WaitAsync(TimeSpan waiting, CancellationToken stoppingToken)
    {
        using var woken = new CancellationTokenSource();

        Volatile.Write(ref waking, woken);

        try
        {
            using CancellationTokenSource linked =
                CancellationTokenSource.CreateLinkedTokenSource(woken.Token, stoppingToken);

            await Task.Delay(waiting, clock, linked.Token);
        }
        catch (OperationCanceledException)
        {
            return !stoppingToken.IsCancellationRequested;
        }
        finally
        {
            Volatile.Write(ref waking, null);
        }

        return true;
    }

    private void Report(RecordingWatch watch)
    {
        if (!watch.SaysAnything)
        {
            return;
        }

        logger.LogInformation(
            "A recording watch read {Watched} recording(s) in flight: {Broken} lost their stream, {Resumed} were "
            + "written to again, {Settled} ended, {LeftOpen} are still without a stream, {StoodDown} were still "
            + "being written after they had ended, {OutOfTouch} could not be asked about, and {Collisions} write(s) "
            + "landed on a row something else had moved.",
            watch.Watched,
            watch.Broken,
            watch.Resumed,
            watch.Settled,
            watch.LeftOpen,
            watch.StoodDown,
            watch.OutOfTouch,
            watch.Collisions);
    }
}
