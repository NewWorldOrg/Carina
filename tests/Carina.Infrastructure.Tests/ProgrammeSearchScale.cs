using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using Npgsql;

namespace Carina.Infrastructure.Tests;

public sealed class ProgrammeSearchScale : IAsyncLifetime
{
    public const int HotRows = 10_000;

    public const int ArchivedRows = 410_000;

    public const int RowsHeldInBothLayers = 200;

    public const int SlotMinutes = 25;

    public static readonly DateTime Anchor = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string ScaleDatabase = "carina_search_scale";

    private const string ConnectionStringVariable = "CARINA_DB_CONNECTION";

    private const int FillTimeoutSeconds = 600;

    private readonly string connectionString = Scaled();

    public async Task InitializeAsync()
    {
        await using (CarinaDbContext dropping = Open())
        {
            await dropping.Database.EnsureDeletedAsync();
        }

        await MakeTheDatabaseAndWhatItsConstraintsCallAsync();

        await using (CarinaDbContext context = Open())
        {
            await context.Database.EnsureCreatedAsync();
        }

        await RunAsync(HotLayer, Anchored(HotRows));
        await RunAsync(ArchiveLayer, Anchored(ArchivedRows));
        await RunAsync(HeldInBothLayers, [new NpgsqlParameter("rows", RowsHeldInBothLayers)]);

        foreach (string statement in SearchIndexesAndStatistics)
        {
            await RunAsync(statement, []);
        }

        await RunAsync("VACUUM (ANALYZE) programme", []);
        await RunAsync("VACUUM (ANALYZE) archived_programme", []);
    }

    private async Task RunAsync(string sql, NpgsqlParameter[] parameters)
    {
        await using var scale = new NpgsqlConnection(connectionString);
        await scale.OpenAsync();

        await using var running = new NpgsqlCommand(sql, scale) { CommandTimeout = FillTimeoutSeconds };
        running.Parameters.AddRange(parameters);

        await running.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public string ConnectionString => connectionString;

    public CarinaDbContext Open(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase(connectionString);

        if (interceptor is not null)
        {
            builder.AddInterceptors(interceptor);
        }

        return new CarinaDbContext(builder.Options);
    }

    public static DateTime ArchiveStartOfSlot(int slot)
        => Anchor.AddDays(-366).AddMinutes(slot * SlotMinutes);

    private static NpgsqlParameter[] Anchored(int rows)
        =>
        [
            new NpgsqlParameter("anchor", Anchor),
            new NpgsqlParameter("rows", rows),
            new NpgsqlParameter("slot", SlotMinutes),
        ];

    private async Task MakeTheDatabaseAndWhatItsConstraintsCallAsync()
    {
        string maintenance = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }
            .ConnectionString;

        await using (var server = new NpgsqlConnection(maintenance))
        {
            await server.OpenAsync();

            await using var asking = new NpgsqlCommand(
                $"SELECT count(*) FROM pg_database WHERE datname = '{ScaleDatabase}'",
                server);

            if ((long)(await asking.ExecuteScalarAsync())! is 0)
            {
                await using var creating = new NpgsqlCommand($"CREATE DATABASE {ScaleDatabase}", server);
                await creating.ExecuteNonQueryAsync();
            }
        }

        await using var scale = new NpgsqlConnection(connectionString);
        await scale.OpenAsync();

        await using var running = new NpgsqlCommand(RecordingGuards.Functions, scale);
        await running.ExecuteNonQueryAsync();
    }

    private static string Scaled()
    {
        string? configured = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"The scale measurement needs {ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScaleDatabase }.ConnectionString;
    }

    private static readonly string[] SearchIndexesAndStatistics =
    [
        "CREATE EXTENSION IF NOT EXISTS pg_trgm",
        "CREATE INDEX ix_programme_searchable ON programme USING gin (searchable gin_trgm_ops)",
        "CREATE INDEX ix_archived_programme_searchable ON archived_programme USING gin (searchable gin_trgm_ops)",
        "ALTER TABLE programme ALTER COLUMN searchable SET STATISTICS 1000",
        "ALTER TABLE archived_programme ALTER COLUMN searchable SET STATISTICS 1000",
    ];

    private const string HotLayer = """
        INSERT INTO programme (
            network_id, service_id, event_id, transport_stream_id, start_at, end_at,
            name, summary, is_shadow, genres, items, related, has_subtitles, source, updated_at)
        SELECT
            32736 + (n % 20) / 5,
            1024 + (n % 20),
            1 + (n / 20) * 20 + (n % 20),
            16 + (n % 20),
            slot.at,
            slot.at + interval '24 minutes',
            '番組' || lpad(n::text, 6, '0') || 'の放送回' || repeat('星', 9),
            '第' || (n % 97) || '回。' || repeat('紹介文', 15) || lpad(n::text, 6, '0'),
            n % 50 = 0,
            ('[{"kind":' || (n % 16) || ',"sort":' || (n % 8) || '},{"kind":' || ((n + 5) % 16)
                || ',"sort":3}]')::jsonb,
            ('[{"heading":"' || repeat('見出', 6) || '","text":"' || repeat('本文', 32)
                || lpad(n::text, 6, '0') || '"},{"heading":"' || repeat('補足', 6)
                || '","text":"' || repeat('注記', 32) || '"}]')::jsonb,
            ('[{"networkId":32736,"serviceId":' || (1024 + (n % 20)) || ',"eventId":'
                || (1 + (n / 20) * 20 + (n % 20)) || ',"kind":"Shared"}]')::jsonb,
            n % 3 = 0,
            'ScheduleExtended',
            @anchor
        FROM generate_series(0, @rows - 1) AS n,
        LATERAL (
            SELECT @anchor::timestamptz - interval '1 day'
                + make_interval(mins => ((n / 20) * @slot)::int) AS at) AS slot
        """;

    private const string ArchiveLayer = """
        INSERT INTO archived_programme (
            network_id, service_id, event_id, start_at, end_at,
            name, summary, has_subtitles, genres, items, archived_at)
        SELECT
            32736 + (n % 20) / 5,
            1024 + (n % 20),
            10000 + ((n / 20) % 2000) * 20 + (n % 20),
            slot.at,
            slot.at + interval '24 minutes',
            '番組' || lpad(n::text, 6, '0') || 'の放送回' || repeat('星', 9),
            '第' || (n % 97) || '回。' || repeat('紹介文', 15) || lpad(n::text, 6, '0'),
            n % 3 = 0,
            ('[{"kind":' || (n % 16) || ',"sort":' || (n % 8) || '},{"kind":' || ((n + 5) % 16)
                || ',"sort":3}]')::jsonb,
            ('[{"heading":"' || repeat('見出', 6) || '","text":"' || repeat('本文', 32)
                || lpad(n::text, 6, '0') || '"},{"heading":"' || repeat('補足', 6)
                || '","text":"' || repeat('注記', 32) || '"}]')::jsonb,
            @anchor
        FROM generate_series(0, @rows - 1) AS n,
        LATERAL (
            SELECT @anchor::timestamptz - interval '366 days'
                + make_interval(mins => ((n / 20) * @slot)::int) AS at) AS slot
        """;

    private const string HeldInBothLayers = """
        INSERT INTO archived_programme (
            network_id, service_id, event_id, start_at, end_at,
            name, summary, has_subtitles, genres, items, archived_at)
        SELECT
            network_id, service_id, event_id, start_at,
            coalesce(end_at, start_at + interval '24 minutes'),
            name, summary, has_subtitles, genres, items, updated_at
        FROM programme
        ORDER BY service_id, event_id
        LIMIT @rows
        """;
}
