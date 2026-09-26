using Carina.Domain.Encodings;
using Carina.Domain.Machines;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

public sealed record EncodeRestartReport(int PutBack, int GivenUp, int Stopped, int Spared)
{
    public int Found => PutBack + GivenUp;
}

/// <summary>
/// Handles the jobs the ledger still holds as running when the process comes up. A programme
/// written down against a job is stopped first when what runs under its id began at the time
/// written down, and left alone otherwise. Each job then goes back to the queue with its work files
/// left in place, or is given up when its attempts are spent, with what it still owes a removal for
/// swept.
/// </summary>
public sealed class EncodeRestart(
    IEncodeJobRepository jobs,
    IStrayProgrammes strays,
    EncodeScratchCleaner cleaner,
    EncodeSettings settings,
    TimeProvider clock,
    ILogger<EncodeRestart> logger)
{
    public async Task<EncodeRestartReport> RecoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EncodeJob> found = await jobs.ListRunningAsync(cancellationToken);
        int putBack = 0;
        int givenUp = 0;
        int stopped = 0;
        int spared = 0;

        foreach (EncodeJob job in found)
        {
            if (job.Programme is { } left)
            {
                StrayFate fate = strays.Stop(left);
                Told(job, left, fate);
                stopped += fate is StrayFate.Stopped ? 1 : 0;
                spared += fate is StrayFate.AnotherProgrammeHasThatId ? 1 : 0;
            }

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
                await cleaner.ClearAsync(job, cancellationToken);
                logger.LogWarning(
                    "Job {Job} was running when this process last stopped, on the last of its {Attempts} attempts, and is given up as {Failure}.",
                    job.Id.Wire,
                    settings.MostAttempts,
                    job.Failure!.Failure);
            }
        }

        return new EncodeRestartReport(putBack, givenUp, stopped, spared);
    }

    private void Told(EncodeJob job, RunningProgramme left, StrayFate fate)
    {
        switch (fate)
        {
            case StrayFate.Stopped:
                logger.LogWarning(
                    "Job {Job} left process {Process} (begun {Began:O}) running when the last process died; it has been stopped.",
                    job.Id.Wire,
                    left.ProcessId,
                    left.StartedAt);
                break;
            case StrayFate.AnotherProgrammeHasThatId:
                logger.LogInformation(
                    "Job {Job} wrote down process {Process} (begun {Began:O}); what runs under that id now began at another time and is left alone.",
                    job.Id.Wire,
                    left.ProcessId,
                    left.StartedAt);
                break;
            case StrayFate.CouldNotBeStopped:
                logger.LogError(
                    "Job {Job} left process {Process} (begun {Began:O}) running, and it could not be stopped.",
                    job.Id.Wire,
                    left.ProcessId,
                    left.StartedAt);
                break;
            default:
                logger.LogInformation(
                    "Job {Job} wrote down process {Process} (begun {Began:O}), which is already gone.",
                    job.Id.Wire,
                    left.ProcessId,
                    left.StartedAt);
                break;
        }
    }
}
