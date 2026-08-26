using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingTickTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Airs = new(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Made = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    private static readonly RecordingSettings Settings = new(
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        new OutputRoot("primary"));

    [Fact]
    public async Task TwoRecordersReachingForTheSameReservationStartOneRecording()
    {
        await Clear();
        ReservationId airing = await Plan(51001, ReservationState.Scheduled);
        var driver = new RecordingDriver();

        await using CarinaDbContext one = CarinaDbContextFactory.Create(database.ConnectionString);
        await using CarinaDbContext other = CarinaDbContextFactory.Create(database.ConnectionString);

        RecordingRun[] runs = await Task.WhenAll(
            Round(one, driver).RunAsync(CancellationToken.None),
            Round(other, driver).RunAsync(CancellationToken.None));

        Assert.Equal(1, runs.Sum(run => run.Started.Count));
        Assert.Single(driver.Started);
        Assert.Equal(1L, await Count("SELECT count(*) FROM recording"));
        Assert.Equal(Airs, await Read(airing, "started_at"));
        Assert.All(runs.SelectMany(run => run.Refused), refusal
            => Assert.Equal(RecordingRefusalKind.ClaimLostToAnother, refusal.Kind));
    }

    [Fact]
    public async Task ARecorderThatLostTheClaimBetweenReadingAndTakingStartsNothing()
    {
        await Clear();
        ReservationId airing = await Plan(51501, ReservationState.Scheduled);
        var driver = new RecordingDriver();

        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);
        await using CarinaDbContext other = CarinaDbContextFactory.Create(database.ConnectionString);

        RecordingRun run = await Round(
            context,
            driver,
            new ClaimedByAnother(
                new ReservationRecordingContract(context),
                () => new ReservationRecordingContract(other)
                    .ClaimAsync(airing, Airs, CancellationToken.None))).RunAsync(CancellationToken.None);

        Assert.Empty(run.Started);
        Assert.Empty(driver.Started);
        Assert.Equal(0L, await Count("SELECT count(*) FROM recording"));
        Assert.Equal(RecordingRefusalKind.ClaimLostToAnother, Assert.Single(run.Refused).Kind);
        Assert.Equal(Airs, await Read(airing, "started_at"));
    }

    [Fact]
    public async Task AReservationInConflictIsNotStartedWhenTheTickReachesIt()
    {
        await Clear();
        ReservationId contested = await Plan(51101, ReservationState.Conflict);
        var driver = new RecordingDriver();

        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);
        RecordingRun run = await Round(context, driver).RunAsync(CancellationToken.None);

        Assert.Empty(run.Started);
        Assert.Empty(driver.Started);
        Assert.Equal(0L, await Count("SELECT count(*) FROM recording"));
        Assert.Null(await Read(contested, "started_at"));
    }

    [Fact]
    public async Task TheRowTheTickWritesIsOneTheDatabaseTakes()
    {
        await Clear();
        ReservationId airing = await Plan(51201, ReservationState.Scheduled);
        var driver = new RecordingDriver();

        await using (CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString))
        {
            await Round(context, driver).RunAsync(CancellationToken.None);
        }

        await using CarinaDbContext reading = CarinaDbContextFactory.Create(database.ConnectionString);
        Recording written = await reading.Set<Recording>().SingleAsync();

        Assert.Equal(airing, written.ReservationId);
        Assert.Equal(new DateTime(2026, 8, 26, 20, 0, 15, DateTimeKind.Utc), written.ExpectedWindowStart);
        Assert.Equal(new DateTime(2026, 8, 26, 21, 0, 0, DateTimeKind.Utc), written.ExpectedWindowEnd);
        Assert.Equal("A programme", written.SnapshotName);
        Assert.Equal("What it is about", written.SnapshotSummary);
        Assert.Equal("adapter0", written.TunerDeviceId!.Value);
        Assert.Equal($"{written.Id.Wire}.ts", written.FileName.Value);
        Assert.True(written.IsInFlight);
        Assert.Equal("Recording", await Read(airing, "composite_state"));
        Assert.Null(await Read(airing, "recording_outcome"));
    }

    [Fact]
    public async Task AReservationTheDriverWouldNotTakeIsLeftForTheNextTick()
    {
        await Clear();
        ReservationId airing = await Plan(51301, ReservationState.Scheduled);
        var driver = new RecordingDriver
        {
            RefusesToStart = Carina.Domain.Driver.DriverCall<Carina.Contracts.SessionSnapshot>.Refused(
                new Carina.Contracts.DriverProblem("noDeviceFree", [])),
        };

        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);
        RecordingRun run = await Round(context, driver).RunAsync(CancellationToken.None);

        Assert.Empty(run.Started);
        Assert.Equal(0L, await Count("SELECT count(*) FROM recording"));
        Assert.Null(await Read(airing, "started_at"));
        Assert.Equal("Scheduled", await Read(airing, "composite_state"));
    }

    private RecordingRound Round(
        CarinaDbContext context,
        RecordingDriver driver,
        IReservationRecordingContract? reservations = null)
    {
        var clock = new HeldTick(Airs);

        return new RecordingRound(
            reservations ?? new ReservationRecordingContract(context),
            new RecordingRepository(context),
            new ResolvedTuning(TuningResolution.Tunable(
                new CandidateChannelId(Guid.NewGuid()),
                TuningParameters.Terrestrial(27),
                impaired: false)),
            new DiskPrecheckService(new StorageMonitor(driver, clock, StorageMonitorSettings.Default)),
            driver,
            Settings,
            clock);
    }

    private async Task<ReservationId> Plan(int eventId, ReservationState state)
    {
        var id = Guid.NewGuid();

        await Execute(
            $"""
            INSERT INTO reservation (
                id, network_id, service_id, event_id, programme_start_at, rule_id, priority,
                start_at, end_at, end_at_confirmed, margin_before, margin_after,
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres, captured_at,
                epg_diverged, epg_diverged_detail, epg_missing, acknowledged_at,
                broadcast_group_key, broadcast_group_role, state, started_at, recording_outcome, created_at)
            VALUES (
                '{id}', 32736, 1024, {eventId}, '{Sql(Airs)}', NULL, 10,
                '{Sql(Airs)}', '{Sql(Airs.AddHours(1))}', true, 0, 0,
                'A programme', 'What it is about', '', '[]'::jsonb, '{Sql(Made)}',
                false, '[]'::jsonb, false, NULL,
                NULL, 'Standalone', '{state}', NULL, NULL, '{Sql(Made)}')
            """);

        return new ReservationId(id);
    }

    private static string Sql(DateTime at) => at.ToString("yyyy-MM-dd HH:mm:sszzz", null);

    private async Task Clear()
    {
        await Execute("DELETE FROM recording");
        await Execute("DELETE FROM reservation");
    }

    private async Task Execute(string sql)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> Count(string sql)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), null);
    }

    private async Task<object?> Read(ReservationId id, string column)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"SELECT {column} FROM reservation WHERE id = '{id.Value}'",
            connection);
        object? read = await command.ExecuteScalarAsync();

        return read is DBNull ? null : read;
    }

    private sealed class HeldTick(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ClaimedByAnother(IReservationRecordingContract inner, Func<Task<bool>> intruder)
        : IReservationRecordingContract
    {
        public async Task<IReadOnlyList<RecordingTick>> DueAtAsync(DateTime at, CancellationToken cancellationToken)
        {
            IReadOnlyList<RecordingTick> due = await inner.DueAtAsync(at, cancellationToken);

            await intruder();

            return due;
        }

        public Task<bool> ClaimAsync(ReservationId id, DateTime at, CancellationToken cancellationToken)
            => inner.ClaimAsync(id, at, cancellationToken);

        public Task<bool> ReleaseAsync(ReservationId id, DateTime claimedAt, CancellationToken cancellationToken)
            => inner.ReleaseAsync(id, claimedAt, cancellationToken);
    }
}
