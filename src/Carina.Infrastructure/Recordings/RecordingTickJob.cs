using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed class RecordingTickJob(
    IServiceScopeFactory scopes,
    RecordingSettings settings,
    TimeProvider clock,
    ILogger<RecordingTickJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstTick;

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

            waiting = settings.BetweenTicks;

            try
            {
                Report(await TickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A recording tick failed; the next one is unaffected.");
            }
        }
    }

    private async Task<RecordingRun> TickAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<RecordingRound>().RunAsync(cancellationToken);
    }

    private void Report(RecordingRun run)
    {
        if (run.Started.Count is 0 && run.Stopped.Count is 0 && run.Refused.Count is 0)
        {
            return;
        }

        logger.LogInformation(
            "A recording tick stopped {Stopped} recording(s) and started {Started}, "
            + "and {Refused} reservation(s) did not start.",
            run.Stopped.Count,
            run.Started.Count,
            run.Refused.Count);

        foreach (RecordingRefusal refusal in run.Refused)
        {
            logger.LogWarning(
                "A reservation that was due did not start: {Kind} ({Refusal}, {Note}).",
                refusal.Kind,
                refusal.Refusal,
                refusal.Note);
        }
    }
}
