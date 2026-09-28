using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Integrity;

/// <summary>
/// The files encode work claims: what a job still in hand writes on the way and the artefact it is
/// making, and the artefact a completed job made that stands for a recording the ledger still holds.
/// </summary>
public sealed class EncodeWorkLedger(CarinaDbContext context) : IEncodeWorkLedger
{
    private static readonly IReadOnlyList<EncodeJobStatus> StillInHand =
    [
        .. Enum.GetValues<EncodeJobStatus>().Where(status => !EncodeStandings.IsTerminal(status)),
    ];

    public async Task<IReadOnlyList<DeclaredFile>> ListAsync(CancellationToken cancellationToken)
    {
        List<Row> scratch = await (
                from written in context.Set<EncodeScratchFile>().AsNoTracking()
                join job in context.Set<EncodeJob>().AsNoTracking() on written.JobId equals job.Id
                where written.RemovedAt == null && StillInHand.Contains(job.Status)
                select new Row(written.OutputRoot, written.FileName))
            .ToListAsync(cancellationToken);

        List<EncodeJob> jobs = await context.Set<EncodeJob>()
            .AsNoTracking()
            .Where(job => StillInHand.Contains(job.Status)
                          || (job.Status == EncodeJobStatus.Completed
                              && job.ReplacedAt == null
                              && job.ArtefactName != null
                              && context.Set<Recording>().Any(recording => recording.Id == job.RecordingId)))
            .ToListAsync(cancellationToken);

        return
        [
            .. scratch
                .Concat(jobs.Select(job => new Row(job.OutputRoot, job.ArtefactItMakes)))
                .Select(row => (Root: row.OutputRoot.Value, Path: row.FileName.Value))
                .Distinct()
                .OrderBy(row => row.Root, StringComparer.Ordinal)
                .ThenBy(row => row.Path, StringComparer.Ordinal)
                .Select(row => new DeclaredFile(new OutputRoot(row.Root), row.Path)),
        ];
    }

    private sealed record Row(OutputRoot OutputRoot, EncodeFileName FileName);
}
