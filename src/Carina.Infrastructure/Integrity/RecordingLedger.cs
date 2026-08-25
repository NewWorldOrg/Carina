using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Integrity;

public sealed class RecordingLedger(CarinaDbContext context) : IRecordingLedger
{
    public async Task<IReadOnlyList<LedgerFile>> ListAsync(CancellationToken cancellationToken)
    {
        List<Row> rows = await context.Set<Recording>()
            .AsNoTracking()
            .OrderBy(recording => recording.OutputRoot)
            .ThenBy(recording => recording.FileName)
            .Select(recording => new Row(
                recording.Id,
                recording.OutputRoot,
                recording.FileName,
                recording.Outcome,
                recording.FileSizeObserved))
            .ToListAsync(cancellationToken);

        return [.. rows.Select(Read)];
    }

    private static LedgerFile Read(Row row)
    {
        if (row.Outcome is null)
        {
            return LedgerFile.StillWriting(row.Id, row.OutputRoot, row.FileName);
        }

        if (row.FileSizeObserved is not { } observed)
        {
            throw new InvalidOperationException(
                $"Recording {row.Id.Wire} ended {row.Outcome} without a size read off the disk.");
        }

        return LedgerFile.Ended(row.Id, row.OutputRoot, row.FileName, observed);
    }

    private sealed record Row(
        RecordingId Id,
        OutputRoot OutputRoot,
        RecordingFileName FileName,
        RecordingOutcome? Outcome,
        long? FileSizeObserved);
}
