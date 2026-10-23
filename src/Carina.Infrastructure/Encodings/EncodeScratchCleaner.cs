using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Removes what a job that has ended still owes a removal for, a file a running job wrote and will
/// not use, and the artefact a completed job made. What is removed is read off the ledger: scratch
/// from where the work goes, a replaced artefact from where the artefact went. A file that is not
/// there any more is settled as already gone.
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
            await SettleAsync(scratch, cancellationToken);
        }

        return owed;
    }

    /// <summary>
    /// Removes the one file named <paramref name="unused"/> that a running job wrote and will not use, read off
    /// the ledger among what the job owes a removal for, so it does not wait on the disk for the job to end.
    /// Nothing, when the ledger holds no such file as owed.
    /// </summary>
    public async Task<EncodeScratchFate?> ClearOneAsync(EncodeJob job, EncodeFileName unused, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(unused);

        IReadOnlyList<EncodeScratchFile> owed = await ledger.ListOwedAsync(job.Id, cancellationToken);

        return owed.FirstOrDefault(scratch => scratch.FileName.Equals(unused)) is { } named
            ? await SettleAsync(named, cancellationToken)
            : null;
    }

    private async Task<EncodeScratchFate> SettleAsync(EncodeScratchFile scratch, CancellationToken cancellationToken)
    {
        EncodeScratchFate fate = RoomOf(scratch) is { } room
            ? Remove(room, scratch.FileName, scratch.JobId, scratch.OutputRoot)
            : Unplaceable(scratch.FileName, scratch.JobId, scratch.OutputRoot);

        scratch.Settle(fate, clock.GetUtcNow().UtcDateTime);

        await ledger.SaveAsync(scratch, cancellationToken);

        return fate;
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

    private string? RoomOf(EncodeScratchFile scratch)
        => scratch.Kind is EncodeScratchKind.ReplacedArtefact
            ? places.WhereTheArtefactGoes(scratch.OutputRoot)
            : places.WhereTheWorkGoes(scratch.OutputRoot);

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
