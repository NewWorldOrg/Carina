using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class EncodeStandingReader(CarinaDbContext context) : IEncodeStandingReader
{
    public async Task<EncodeStandingBoard> ReadAsync(
        IReadOnlyCollection<RecordingId> recordings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordings);

        if (recordings.Count is 0)
        {
            return EncodeStandingBoard.Empty;
        }

        RecordingId[] asked = [.. recordings.Distinct()];

        List<HeldStanding> held = await context.Set<EncodeJob>()
            .AsNoTracking()
            .Where(row => asked.Contains(row.RecordingId))
            .Select(row => new HeldStanding(row.RecordingId, row.Status))
            .ToListAsync(cancellationToken);

        return EncodeStandingBoard.Of(held.Select(row => (row.Recording, row.Status)));
    }

    private sealed record HeldStanding(RecordingId Recording, EncodeJobStatus Status);
}
