using Carina.Domain.Recordings;

namespace Carina.Domain.Encodings;

public sealed record RecordingEncodeErasure(IReadOnlyList<EncodeJob> Ended, IReadOnlyList<EncodeJob> Standing);

public sealed class RecordingEncodeDomainService(IEncodeJobRepository jobs)
{
    public async Task<bool> AnyUnderWayAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        IReadOnlyList<EncodeJob> held = await jobs.ListForRecordingAsync(recording, cancellationToken);

        return held.Any(job => !job.HasEnded);
    }

    public async Task<RecordingEncodeErasure> WhatToEraseAsync(RecordingId recording, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recording);

        IReadOnlyList<EncodeJob> held = await jobs.ListForRecordingAsync(recording, cancellationToken);

        return new RecordingEncodeErasure(
            [.. held.Where(job => job.HasEnded)],
            [.. held.Where(job => job.StandsAsTheArtefact).DistinctBy(job => (job.OutputRoot, job.ArtefactName))]);
    }

    public async Task<bool> AnotherStandsAtTheNameOfAsync(EncodeJob ended, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ended);

        IReadOnlyList<EncodeJob> held = await jobs.ListForRecordingAsync(ended.RecordingId, cancellationToken);

        return held.Any(job => !job.Id.Equals(ended.Id) && job.StandsAsTheArtefact && job.SharesTheArtefactWith(ended));
    }
}
