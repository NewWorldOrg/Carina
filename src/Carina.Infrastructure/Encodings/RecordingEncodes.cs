using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Answers for one recording out of the encode ledger. A job still waiting or running is work under
/// way. Taking what the jobs left off the disk sweeps the scratch every ended job still owes a
/// removal for, then removes each artefact a completed job made, once per name. A name held by a
/// job that did not complete is left alone.
/// </summary>
public sealed class RecordingEncodes(
    IEncodeJobRepository jobs,
    EncodeScratchCleaner cleaner,
    ILogger<RecordingEncodes> logger) : IRecordingEncodes
{
    public async Task<bool> AnyUnderWayAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);

        IReadOnlyList<EncodeJob> held = await jobs.ListForRecordingAsync(recordingId, cancellationToken);

        return held.Any(job => !job.HasEnded);
    }

    public async Task<EncodesErased> EraseAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);

        IReadOnlyList<EncodeJob> held = await jobs.ListForRecordingAsync(recordingId, cancellationToken);
        int removed = 0;
        List<EncodeFileName> left = [];

        foreach (EncodeJob job in held.Where(job => job.HasEnded))
        {
            foreach (EncodeScratchFile scratch in await cleaner.ClearAsync(job, cancellationToken))
            {
                removed += scratch.Fate is EncodeScratchFate.Removed ? 1 : 0;

                if (scratch.Fate is EncodeScratchFate.CouldNotBeRemoved)
                {
                    left.Add(scratch.FileName);
                }
            }
        }

        IEnumerable<EncodeJob> made = held
            .Where(job => job.Status is EncodeJobStatus.Completed && job.ArtefactName is not null)
            .DistinctBy(job => (job.OutputRoot, job.ArtefactName));

        foreach (EncodeJob job in made)
        {
            EncodeScratchFate fate = cleaner.RemoveArtefact(job);
            removed += fate is EncodeScratchFate.Removed ? 1 : 0;

            if (fate is EncodeScratchFate.CouldNotBeRemoved)
            {
                left.Add(job.ArtefactName!);
            }
        }

        if (left.Count > 0)
        {
            logger.LogWarning(
                "Recording {Recording} is being thrown away and {Left} file(s) its encodes left could not be removed.",
                recordingId.Wire,
                left.Count);
        }

        return new EncodesErased(removed, left);
    }
}
