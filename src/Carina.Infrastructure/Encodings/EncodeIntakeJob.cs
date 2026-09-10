using Carina.Domain.Encodings;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The loop that reads the recording ledger for what has ended and queues it. It sweeps the pages
/// in the order recordings were made and then stays on the last one, which is where a recording
/// that ends next will land; a start reads every page again, so a machine that could not settle a
/// destination earlier picks up everything it passed over once it can. It is a loop of its own and
/// not a step of the dispatch's, because the dispatch's loop is inside a run and a run lasts as
/// long as the encode does.
/// </summary>
public sealed class EncodeIntakeJob(
    IServiceScopeFactory scopes,
    EncodeSettings settings,
    TimeProvider clock,
    ILogger<EncodeIntakeJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!settings.Automatically)
        {
            logger.LogInformation(
                "A recording that ends is not queued for encoding on this machine, because Encodings:Automatically is off.");

            return;
        }

        int page = 1;
        TimeSpan waiting = settings.BeforeFirstLook;

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

            waiting = settings.BetweenLooks;

            try
            {
                EncodeIntake took = await TakeAsync(page, stoppingToken);

                page = took.MorePages ? took.Page + 1 : took.LastPage;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A look for recordings to encode failed; the next one is unaffected.");
                page = 1;
            }
        }
    }

    private async Task<EncodeIntake> TakeAsync(int page, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<EncodeIntakeRound>().TakeAsync(page, cancellationToken);
    }
}
