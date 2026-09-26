using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class EncodeIntakeReader(CarinaDbContext context) : IEncodeIntakeReader
{
    public async Task<IReadOnlyList<RecordingId>> NeverQueuedAsync(int most, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(most, 1);

        RecordingOutcome[] subject = [.. EncodeAutoRun.Subject];

        return await context.Set<Recording>()
            .AsNoTracking()
            .Where(recording => recording.Outcome != null && subject.Contains(recording.Outcome.Value))
            .Where(recording => recording.EncodeWhenRecorded)
            .Where(recording => recording.LeftBehindAt == null)
            .Where(recording => !context.Set<EncodeJob>().Any(job => job.RecordingId == recording.Id))
            .OrderBy(recording => recording.StartedAtActual)
            .ThenBy(recording => recording.Id)
            .Take(most)
            .Select(recording => recording.Id)
            .ToListAsync(cancellationToken);
    }
}
