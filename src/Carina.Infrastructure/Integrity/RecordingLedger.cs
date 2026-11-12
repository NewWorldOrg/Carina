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
                recording.FileSizeObserved,
                recording.ThumbnailState))
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(row => LedgerFile.Of(
                row.Id,
                row.OutputRoot,
                row.FileName,
                row.Outcome,
                row.FileSizeObserved,
                row.ThumbnailState)),
        ];
    }

    private sealed record Row(
        RecordingId Id,
        OutputRoot OutputRoot,
        RecordingFileName FileName,
        RecordingOutcome? Outcome,
        long? FileSizeObserved,
        ThumbnailState ThumbnailState);
}
