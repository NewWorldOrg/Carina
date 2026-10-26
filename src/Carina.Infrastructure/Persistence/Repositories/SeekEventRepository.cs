using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SeekEventRepository(CarinaDbContext context) : ISeekEventRepository
{
    /// <summary>
    /// Inserts the seek only while the seeks of its recording that happened within the hour before it number fewer
    /// than the most taken, counted and written in a single statement.
    /// </summary>
    public async Task<bool> TakeAsync(SeekEvent seek, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(seek);

        Guid recording = seek.RecordingId.Value;
        DateTime happenedAt = seek.HappenedAt;
        DateTime since = seek.HappenedAt - SeekEvent.Within;
        string action = seek.Event.ToString();
        string means = seek.Means.ToString();
        TimeSpan from = seek.From;
        TimeSpan to = seek.To;
        int most = SeekEvent.MostAnHour;

        return await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO segment_seek_event (recording_id, happened_at, event, means, from_at, to_at)
            SELECT {recording}::uuid, {happenedAt}::timestamptz, {action}::varchar, {means}::varchar, {from}::interval, {to}::interval
            WHERE (SELECT count(*) FROM segment_seek_event WHERE recording_id = {recording}::uuid AND happened_at > {since}::timestamptz) < {most}
            """,
            cancellationToken) is 1;
    }

    public async Task<IReadOnlyList<SeekEvent>> ListAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordings);

        RecordingId[] named = [.. recordings];

        return await context.Set<SeekEvent>()
            .AsNoTracking()
            .Where(seek => named.Contains(seek.RecordingId))
            .OrderBy(seek => seek.HappenedAt)
            .ThenBy(seek => seek.Id)
            .ToListAsync(cancellationToken);
    }
}
