using Carina.Domain.Encodings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Removes what a job that has ended still owes a removal for. What to remove is read off the
/// ledger and nothing else: a walk of the directory would take another job's work file with it
/// (BR-ED2-010). A file that is not there any more is written down as such, not as an error.
/// </summary>
public sealed class EncodeScratchCleaner(
    IEncodeScratchLedger ledger,
    EncodePlaces places,
    TimeProvider clock,
    ILogger<EncodeScratchCleaner> logger)
{
    public async Task<IReadOnlyList<EncodeScratchFile>> ClearAsync(EncodeJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (!job.HasEnded)
        {
            throw new InvalidOperationException(
                $"Scratch is cleared once a job has ended, and this one still stands at {job.Status}.");
        }

        IReadOnlyList<EncodeScratchFile> owed = await ledger.ListOwedAsync(job.Id, cancellationToken);

        foreach (EncodeScratchFile scratch in owed)
        {
            EncodeScratchFate fate = places.WhereTheWorkGoes(scratch.OutputRoot) is { } room
                ? Remove(Path.Combine(room, scratch.FileName.Value), scratch)
                : Unplaceable(scratch);

            scratch.Settle(fate, clock.GetUtcNow().UtcDateTime);

            await ledger.SaveAsync(scratch, cancellationToken);
        }

        return owed;
    }

    private EncodeScratchFate Unplaceable(EncodeScratchFile scratch)
    {
        logger.LogWarning(
            "Scratch file {File} of job {Job} is under output root {Root}, and nothing tells this process where that is mounted.",
            scratch.FileName.Value,
            scratch.JobId.Wire,
            scratch.OutputRoot.Value);

        return EncodeScratchFate.CouldNotBeRemoved;
    }

    private EncodeScratchFate Remove(string path, EncodeScratchFile scratch)
    {
        if (!File.Exists(path))
        {
            return EncodeScratchFate.AlreadyGone;
        }

        try
        {
            File.Delete(path);

            return EncodeScratchFate.Removed;
        }
        catch (Exception refusal) when (refusal is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                refusal,
                "Scratch file {File} of job {Job} under output root {Root} could not be removed.",
                scratch.FileName.Value,
                scratch.JobId.Wire,
                scratch.OutputRoot.Value);

            return EncodeScratchFate.CouldNotBeRemoved;
        }
    }
}
