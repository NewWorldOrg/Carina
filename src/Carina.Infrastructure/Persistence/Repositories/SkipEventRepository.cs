using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class SkipEventRepository(CarinaDbContext context) : ISkipEventRepository
{
    /// <summary>
    /// Inserts the event only while the events of its recording that happened within the hour before it number
    /// fewer than the most taken, counted and written in a single statement.
    /// </summary>
    public async Task<bool> TakeAsync(SkipEvent skip, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(skip);

        Guid recording = skip.RecordingId.Value;
        DateTime happenedAt = skip.HappenedAt;
        DateTime since = skip.HappenedAt - SkipEvent.Within;
        string action = skip.Event.ToString();
        string kind = skip.Kind.ToString();
        TimeSpan from = skip.From;
        TimeSpan to = skip.To;
        int? version = skip.ModelVersion;
        int most = SkipEvent.MostAnHour;

        return await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO segment_skip_event (recording_id, happened_at, event, kind, from_at, to_at, model_version)
            SELECT {recording}::uuid, {happenedAt}::timestamptz, {action}::varchar, {kind}::varchar, {from}::interval, {to}::interval, {version}::integer
            WHERE (SELECT count(*) FROM segment_skip_event WHERE recording_id = {recording}::uuid AND happened_at > {since}::timestamptz) < {most}
            """,
            cancellationToken) is 1;
    }
}
