using Carina.Domain.Encodings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

public sealed record EncodeRestartReport(int PutBack, int GivenUp)
{
    public int Found => PutBack + GivenUp;
}

/// <summary>
/// What the process does about jobs still held as running when it comes up: none of them is
/// running, because whatever ran them died with the last process, so each goes back to the queue
/// to start over or is given up when its attempts are spent. Work files are left where they are;
/// the next attempt writes under another name (BR-ED2-011).
/// </summary>
public sealed class EncodeRestart(
    IEncodeJobRepository jobs,
    EncodeSettings settings,
    TimeProvider clock,
    ILogger<EncodeRestart> logger)
{
    public async Task<EncodeRestartReport> RecoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeJob> found = await jobs.ListRunningAsync(cancellationToken);
        int putBack = 0;
        int givenUp = 0;

        foreach (EncodeJob job in found)
        {
            int attemptItWasOn = job.Attempt;
            EncodeRecovery recovery = job.Recover(settings.MostAttempts, clock.GetUtcNow().UtcDateTime);

            await jobs.SaveAsync(job, cancellationToken);

            if (recovery is EncodeRecovery.PutBack)
            {
                putBack++;
                logger.LogWarning(
                    "Job {Job} was running when this process last stopped; attempt {Attempt} is discarded and it waits for attempt {Next}. Its work file is left where it is.",
                    job.Id.Wire,
                    attemptItWasOn,
                    job.Attempt);
            }
            else
            {
                givenUp++;
                logger.LogWarning(
                    "Job {Job} was running when this process last stopped, on the last of its {Attempts} attempts, and is given up as {Failure}.",
                    job.Id.Wire,
                    settings.MostAttempts,
                    job.Failure!.Failure);
            }
        }

        return new EncodeRestartReport(putBack, givenUp);
    }
}
