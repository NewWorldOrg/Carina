using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

/// <summary>
/// What erasing a recording takes off the disk of its encodes: every job that ended, for what it still owes a
/// removal for, and the jobs whose artefact stands, one for each file.
/// </summary>
public sealed record RecordingEncodeErasure(IReadOnlyList<EncodeJob> Ended, IReadOnlyList<EncodeJob> Standing);

/// <summary>
/// Answers for one recording out of the jobs the ledger holds for it: whether an encode is under way, what erasing
/// it removes, and whether a file a job placed is still another completed job's artefact.
/// </summary>
public sealed class RecordingEncodeDomainService(IEncodeJobRepository jobs)
{
    /// <summary>
    /// Whether a job of the recording is waiting or running.
    /// </summary>
    public async Task<bool> AnyUnderWayAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        IReadOnlyList<EncodeJob> held = await jobs.ListForRecordingAsync(recording, cancellationToken);

        return held.Any(job => !job.HasEnded);
    }

    /// <summary>
    /// What erasing the recording takes off the disk. A name held by a job that did not complete is left alone, and
    /// a file two standing jobs name under the same output root is removed once.
    /// </summary>
    public async Task<RecordingEncodeErasure> WhatToEraseAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        IReadOnlyList<EncodeJob> held = await jobs.ListForRecordingAsync(recording, cancellationToken);

        return new RecordingEncodeErasure(
            [.. held.Where(job => job.HasEnded)],
            [.. held.Where(job => job.StandsAsTheArtefact).DistinctBy(job => (job.OutputRoot, job.ArtefactName))]);
    }

    /// <summary>
    /// Whether another completed job of the same recording, not replaced, names the same file as
    /// <paramref name="ended"/>, which keeps the file from being removed with it.
    /// </summary>
    public async Task<bool> AnotherStandsAtTheNameOfAsync(EncodeJob ended, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ended);

        IReadOnlyList<EncodeJob> held = await jobs.ListForRecordingAsync(ended.RecordingId, cancellationToken);

        return held.Any(job => !job.Id.Equals(ended.Id) && job.StandsAsTheArtefact && job.SharesTheArtefactWith(ended));
    }
}
