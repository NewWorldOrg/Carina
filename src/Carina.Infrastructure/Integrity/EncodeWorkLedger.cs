using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Integrity;

public sealed class EncodeWorkLedger(CarinaDbContext context) : IEncodeWorkLedger
{
    private static readonly IReadOnlyList<EncodeJobStatus> StillInHand =
    [
        .. Enum.GetValues<EncodeJobStatus>().Where(status => !EncodeStandings.IsTerminal(status)),
    ];

    public async Task<IReadOnlyList<DeclaredFile>> ListAsync(CancellationToken cancellationToken)
    {
        List<Row> rows = await (
                from scratch in context.Set<EncodeScratchFile>().AsNoTracking()
                join job in context.Set<EncodeJob>().AsNoTracking() on scratch.JobId equals job.Id
                where scratch.RemovedAt == null && StillInHand.Contains(job.Status)
                orderby scratch.OutputRoot, scratch.FileName
                select new Row(scratch.OutputRoot, scratch.FileName))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(row => new DeclaredFile(row.OutputRoot, row.FileName.Value))];
    }

    private sealed record Row(OutputRoot OutputRoot, EncodeFileName FileName);
}
