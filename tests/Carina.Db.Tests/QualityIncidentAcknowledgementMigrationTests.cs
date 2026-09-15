using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class QualityIncidentAcknowledgementMigrationTests
{
    private const string ScratchDatabase = "carina_quality_incident_acknowledgement_test";

    private const string WhileAcknowledgingWasKept = "20260915074823_WhenTooMuchIsLeftScrambledToWatch";

    private const string Detected = "timestamptz '2026-08-08 03:00:00+00'";

    private const string Notified = "timestamptz '2026-08-08 03:01:00+00'";

    private const string Acknowledged = "timestamptz '2026-08-08 03:02:00+00'";

    private const string Resolved = "timestamptz '2026-08-08 03:03:00+00'";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-QS-002: an incident acknowledged while acknowledging was kept stands as told about, or resolved if it was")]
    public async Task AnIncidentAcknowledgedWhileAcknowledgingWasKeptStandsAsToldAboutOrResolvedIfItWas()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(WhileAcknowledgingWasKept, Cancel);

        Guid standing = Guid.NewGuid();
        Guid settled = Guid.NewGuid();

        await using (NpgsqlConnection writing = await OpenAsync())
        {
            await IncidentAsync(writing, standing, "Acknowledged", "NULL");
            await IncidentAsync(writing, settled, "Resolved", Resolved);
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection reading = await OpenAsync();

        Assert.Equal(
            1L,
            await CountAsync(
                reading,
                $"SELECT count(*) FROM quality_incident WHERE id = '{standing}' AND state = 'Notified' AND notified_at = {Notified} AND resolved_at IS NULL"));
        Assert.Equal(
            1L,
            await CountAsync(
                reading,
                $"SELECT count(*) FROM quality_incident WHERE id = '{settled}' AND state = 'Resolved' AND resolved_at = {Resolved}"));
        Assert.Equal(
            0L,
            await CountAsync(
                reading,
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'quality_incident' AND column_name LIKE 'acknowledged%'"));
    }

    private static async Task IncidentAsync(NpgsqlConnection connection, Guid id, string state, string resolvedAt)
    {
        await using var writing = new NpgsqlCommand(
            $"""
            INSERT INTO quality_incident (
                id, detected_at, breached, subject_kind, subject_key, observed, owner, classification, silence,
                applied_default, applied_current, applied_provisional, applied_observations, applied_updated_at,
                state, notified_at, acknowledged_at, acknowledged_by, resolved_at)
            VALUES (
                '{id}', {Detected}, 'PacketsLostWarning', 'Recording', '{id:N}', 0.004,
                'Quality', NULL, NULL, 0.0002, 0.0002, true, 0, {Detected},
                '{state}', {Notified}, {Acknowledged}, 'someone', {resolvedAt})
            """,
            connection);

        await writing.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var reading = new NpgsqlCommand(sql, connection);

        return (long)(await reading.ExecuteScalarAsync(Cancel))!;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(Scratch());
        await connection.OpenAsync(Cancel);

        return connection;
    }

    private static string Scratch()
    {
        string? configured = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable);

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {CarinaDbContextFactory.ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }
}
