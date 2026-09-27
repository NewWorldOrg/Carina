using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// What settling a recording's artefacts came to: how many earlier artefacts were marked replaced,
/// how many of their files were removed, and the name of each one that could not be.
/// </summary>
public sealed record EncodeSuccessionReport(int Replaced, int FilesRemoved, IReadOnlyList<EncodeFileName> Left)
{
    public static readonly EncodeSuccessionReport Nothing = new(0, 0, []);
}

/// <summary>
/// Leaves a recording with the artefact of the job that completed last as its only one.
/// </summary>
/// <remarks>
/// Every earlier completed job is marked replaced, and the file each one made under another name is
/// written down as owed a removal in the same write. The removals every replaced job still owes are
/// then made from the ledger. A removal owed at the name the standing artefact now holds is settled
/// without touching the disk. The recording itself is never touched.
/// </remarks>
public sealed class EncodeArtefactSuccession(
    IEncodeJobRepository jobs,
    IEncodeScratchLedger ledger,
    IAtomicWrite writes,
    EncodeScratchCleaner cleaner,
    TimeProvider clock,
    ILogger<EncodeArtefactSuccession> logger)
{
    public async Task<EncodeSuccessionReport> SettleAsync(RecordingId recordingId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordingId);

        IReadOnlyList<EncodeJob> held = await jobs.ListForRecordingAsync(recordingId, cancellationToken);
        EncodeSuccession succession = EncodeSuccession.Of(held);

        if (succession.Standing is not { } standing)
        {
            return EncodeSuccessionReport.Nothing;
        }

        if (succession.ToReplace.Count > 0)
        {
            await ReplaceAsync(standing, succession.ToReplace, cancellationToken);

            logger.LogInformation(
                "Job {Job} made the artefact of recording {Recording}, and replaced what {Earlier} earlier job(s) made.",
                standing.Id.Wire,
                recordingId.Wire,
                succession.ToReplace.Count);
        }

        int removed = 0;
        List<EncodeFileName> left = [];

        foreach (EncodeJob replaced in held.Where(job => job.ReplacedAt is not null))
        {
            await KeepWhatStandsAsync(replaced, standing, cancellationToken);

            foreach (EncodeScratchFile scratch in await cleaner.ClearAsync(replaced, cancellationToken))
            {
                removed += scratch.Fate is EncodeScratchFate.Removed ? 1 : 0;

                if (scratch.Fate is EncodeScratchFate.CouldNotBeRemoved)
                {
                    left.Add(scratch.FileName);
                }
            }
        }

        if (left.Count > 0)
        {
            logger.LogWarning(
                "Recording {Recording} keeps {Left} replaced artefact file(s) that could not be removed: {Names}. The ledger still owes their removal.",
                recordingId.Wire,
                left.Count,
                string.Join(", ", left.Select(name => name.Value)));
        }

        return new EncodeSuccessionReport(succession.ToReplace.Count, removed, left);
    }

    private async Task ReplaceAsync(EncodeJob standing, IReadOnlyList<EncodeJob> earlier, CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;

        await writes.AllOrNothingAsync(
            async token =>
            {
                foreach (EncodeJob job in earlier)
                {
                    if (!job.SharesTheArtefactWith(standing))
                    {
                        await ledger.RecordAsync(
                            EncodeScratchFile.Record(
                                EncodeScratchFileId.New(),
                                job.Id,
                                EncodeScratchKind.ReplacedArtefact,
                                job.OutputRoot,
                                job.ArtefactName!,
                                now),
                            token);
                    }

                    job.Replaced(standing, now);
                    await jobs.SaveAsync(job, token);
                }

                return earlier.Count;
            },
            cancellationToken);
    }

    private async Task KeepWhatStandsAsync(EncodeJob replaced, EncodeJob standing, CancellationToken cancellationToken)
    {
        IEnumerable<EncodeScratchFile> atTheStandingName = (await ledger.ListOwedAsync(replaced.Id, cancellationToken))
            .Where(scratch => scratch.Kind is EncodeScratchKind.ReplacedArtefact
                && scratch.OutputRoot.Equals(standing.OutputRoot)
                && scratch.FileName.Equals(standing.ArtefactName));

        foreach (EncodeScratchFile scratch in atTheStandingName)
        {
            scratch.Settle(EncodeScratchFate.BecameTheArtefact, clock.GetUtcNow().UtcDateTime);
            await ledger.SaveAsync(scratch, cancellationToken);

            logger.LogInformation(
                "The removal job {Job} owed for {File} is settled without touching the disk, as the artefact of job {Standing} now stands at that name.",
                replaced.Id.Wire,
                scratch.FileName.Value,
                standing.Id.Wire);
        }
    }
}
