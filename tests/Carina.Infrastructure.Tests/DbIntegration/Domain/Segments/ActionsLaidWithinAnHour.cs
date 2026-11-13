using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using NpgsqlTypes;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Segments;

/// <summary>
/// The events a recording kept before a new skip event or seek, laid so that the statements that keep the new one
/// only while its recording has taken fewer than the most an hour can be held to the event's own answer for the same
/// rows: which kept events count with the new one, on the instant an hour before it and a microsecond after, with one
/// short of the most and with the most.
/// </summary>
internal static class ActionsLaidWithinAnHour
{
    public static readonly DateTime Noon = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Microsecond = TimeSpan.FromTicks(10);

    public static TheoryData<string, int, double[]> Arms
        => new()
        {
            { "one short of the most, and one an hour before", SkipEvent.MostAnHour - 1, [-3600] },
            { "one short of the most, and one a microsecond after an hour before", SkipEvent.MostAnHour - 1, [-3600 + 0.000001] },
            { "one short of the most, and one later than the new one", SkipEvent.MostAnHour - 1, [300] },
            { "two short of the most, and one at the same instant", SkipEvent.MostAnHour - 2, [0] },
            { "the most, all an hour before or earlier", 0, [.. Enumerable.Repeat(-3600.0, SkipEvent.MostAnHour)] },
            { "none", 0, [] },
        };

    public static DateTime[] Laid(int withinTheHalfHour, double[] secondsFromTheNew)
        =>
        [
            .. Enumerable.Range(1, withinTheHalfHour).Select(index => Noon.AddMinutes(-30).AddMilliseconds(index)),
            .. secondsFromTheNew.Select(seconds => Noon + TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond / Microsecond.Ticks) * Microsecond.Ticks)),
        ];

    public static async Task LayAsync(ServiceProvider container, string statement, RecordingId recording, DateTime[] laid)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        CarinaDbContext context = scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
        await context.Database.OpenConnectionAsync();
        await using var command = new NpgsqlCommand(statement, (NpgsqlConnection)context.Database.GetDbConnection());
        command.Parameters.AddWithValue("recording", recording.Value);
        command.Parameters.Add(new NpgsqlParameter("laid", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = laid });

        await command.ExecuteNonQueryAsync();
    }
}
