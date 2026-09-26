using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Removes what a job that has ended still owes a removal for, and the artefact a completed job
/// made. What to remove is read off the ledger and nothing else: a walk of the directory would take
/// another job's work file with it. A file that is not there any more is written down
/// as such, not as an error.
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
                ? Remove(room, scratch.FileName, scratch.JobId, scratch.OutputRoot)
                : Unplaceable(scratch.FileName, scratch.JobId, scratch.OutputRoot);

            scratch.Settle(fate, clock.GetUtcNow().UtcDateTime);

            await ledger.SaveAsync(scratch, cancellationToken);
        }

        return owed;
    }

    /// <summary>
    /// Removes the artefact the ledger says a completed job made, from under the root that job
    /// placed it in.
    /// </summary>
    public EncodeScratchFate RemoveArtefact(EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.Status is not EncodeJobStatus.Completed || job.ArtefactName is not { } artefact)
        {
            throw new InvalidOperationException(
                $"An artefact is removed for a job that completed and named it, and this one stands at {job.Status}.");
        }

        return places.WhereTheArtefactGoes(job.OutputRoot) is { } room
            ? Remove(room, artefact, job.Id, job.OutputRoot)
            : Unplaceable(artefact, job.Id, job.OutputRoot);
    }

    private EncodeScratchFate Unplaceable(EncodeFileName file, EncodeJobId job, OutputRoot root)
    {
        logger.LogWarning(
            "File {File} of job {Job} is under output root {Root}, and nothing tells this process where that is mounted.",
            file.Value,
            job.Wire,
            root.Value);

        return EncodeScratchFate.CouldNotBeRemoved;
    }

    private EncodeScratchFate Remove(string room, EncodeFileName file, EncodeJobId job, OutputRoot root)
    {
        string path = Path.Combine(room, file.Value);

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
                "File {File} of job {Job} under output root {Root} could not be removed.",
                file.Value,
                job.Wire,
                root.Value);

            return EncodeScratchFate.CouldNotBeRemoved;
        }
    }
}
