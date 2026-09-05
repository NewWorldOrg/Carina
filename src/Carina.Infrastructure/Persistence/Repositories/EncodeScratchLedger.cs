using Carina.Domain.Encodings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class EncodeScratchLedger(CarinaDbContext context) : IEncodeScratchLedger
{
    public async Task RecordAsync(EncodeScratchFile scratch, CancellationToken cancellationToken)
    {
        context.Add(scratch);

        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EncodeScratchFile>> ListOwedAsync(
        EncodeJobId jobId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobId);

        return await context.Set<EncodeScratchFile>()
            .Where(scratch => scratch.JobId == jobId && scratch.RemovedAt == null)
            .OrderBy(scratch => scratch.WrittenAt)
            .ThenBy(scratch => scratch.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveAsync(EncodeScratchFile scratch, CancellationToken cancellationToken)
    {
        context.Update(scratch);

        await context.SaveChangesAsync(cancellationToken);
    }
}
