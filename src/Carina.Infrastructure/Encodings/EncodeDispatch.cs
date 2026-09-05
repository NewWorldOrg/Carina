using Carina.Domain.Encodings;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

public sealed record EncodeLook(EncodeClaimStanding Standing, EncodeJobId? Job, EncodeJobStatus? Ended);

/// <summary>
/// The one loop that runs encode jobs. It first puts back what was running when the last process
/// stopped, then looks at the queue: a claim is a conditional update in the ledger, one job is run
/// to its end, and the queue is looked at again at once, or after a pause when nothing was waiting.
/// Two of these looking at the same ledger cannot both start a job, because the ledger holds one
/// running job and refuses the second claim (BR-ED2-005).
/// </summary>
public sealed class EncodeDispatch(
    IServiceScopeFactory scopes,
    EncodeSettings settings,
    TimeProvider clock,
    ILogger<EncodeDispatch> logger) : BackgroundService
{
    public async Task<EncodeRestartReport> RecoverAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<EncodeRestart>().RecoverAsync(cancellationToken);
    }

    public async Task<EncodeLook> LookAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IEncodeJobRepository jobs = scope.ServiceProvider.GetRequiredService<IEncodeJobRepository>();
        EncodeClaim claim = await jobs.ClaimNextAsync(clock.GetUtcNow().UtcDateTime, cancellationToken);

        if (claim.Job is not { } job)
        {
            return new EncodeLook(claim.Standing, null, null);
        }

        try
        {
            EncodeJobStatus ended = await scope.ServiceProvider.GetRequiredService<EncodeJobRunner>().RunAsync(job, cancellationToken);

            return new EncodeLook(claim.Standing, job.Id, ended);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "Job {Job} was stopped with this process on attempt {Attempt}; the ledger still holds it as running, and the next start puts it back.",
                job.Id.Wire,
                job.Attempt);

            throw;
        }
        catch (EncodeJobMovedMeanwhileException)
        {
            logger.LogInformation(
                "Job {Job} was moved in the ledger by another hand while it ran on attempt {Attempt}; this run is dropped and the ledger's word stands.",
                job.Id.Wire,
                job.Attempt);

            return new EncodeLook(claim.Standing, job.Id, await SweptAsync(job.Id, cancellationToken));
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "Job {Job} threw on attempt {Attempt}; the attempt is discarded.", job.Id.Wire, job.Attempt);

            if (job.Status is not EncodeJobStatus.Running)
            {
                return new EncodeLook(claim.Standing, job.Id, job.Status);
            }

            EncodeRecovery recovery = job.Recover(settings.MostAttempts, clock.GetUtcNow().UtcDateTime);
            await jobs.SaveAsync(job, cancellationToken);

            return new EncodeLook(claim.Standing, job.Id, recovery is EncodeRecovery.GivenUp ? job.Status : null);
        }
    }

    /// <summary>
    /// Reads the job again, as the other hand left it, and sweeps what it still owes a removal for if
    /// it has ended. The scope that ran it holds a copy that no longer describes the job, so a fresh
    /// one is opened.
    /// </summary>
    private async Task<EncodeJobStatus?> SweptAsync(EncodeJobId id, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        EncodeJob? found = await scope.ServiceProvider.GetRequiredService<IEncodeJobRepository>().FindAsync(id, cancellationToken);

        if (found is null)
        {
            return null;
        }

        if (found.HasEnded)
        {
            await scope.ServiceProvider.GetRequiredService<EncodeScratchCleaner>().ClearAsync(found, cancellationToken);
        }

        return found.Status;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool recovered = false;
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
                if (!recovered)
                {
                    Told(await RecoverAsync(stoppingToken));
                    recovered = true;
                }

                EncodeLook look = await LookAsync(stoppingToken);

                if (look.Standing is EncodeClaimStanding.Claimed or EncodeClaimStanding.TakenMeanwhile)
                {
                    waiting = TimeSpan.Zero;
                }

                if (look.Standing is EncodeClaimStanding.AnotherIsRunning)
                {
                    logger.LogInformation("The ledger holds a running job this process is not running, so the queue waits for it.");
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A look at the encode queue failed; the next one is unaffected.");
            }
        }
    }

    private void Told(EncodeRestartReport report)
    {
        if (report.Found is 0)
        {
            logger.LogInformation("No encode job was left running by the last process.");

            return;
        }

        logger.LogWarning(
            "{Found} encode job(s) were left running by the last process: {PutBack} put back in the queue, {GivenUp} given up; {Stopped} programme(s) still running were stopped, {Spared} under a written id belonged to someone else.",
            report.Found,
            report.PutBack,
            report.GivenUp,
            report.Stopped,
            report.Spared);
    }
}
