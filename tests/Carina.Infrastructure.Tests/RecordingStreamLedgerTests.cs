using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Tests.Recordings;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using static Carina.Infrastructure.Tests.Recordings.RecordingStreamFixture;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingStreamLedgerTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Ended = Airs.AddMinutes(31);

    [Fact]
    public async Task TheReasonsTheJudgementNamedComeBackOutOfTheLedgerAndReachTheReservation()
    {
        Reservation reservation = Plan(6201);
        await Add(reservation);
        await Claim(reservation.Id);

        Recording recording = Begin(6201, reservation.Id);
        recording.Wrote(TimeSpan.FromSeconds(1750));
        await Add(recording);

        RecordingWatch watch = await Supervisor(
                new WatchedDriver(),
                new WeighedFiles { Weighs = 3_300_000_000 },
                new WatchClock(Ended),
                recording.Id,
                null)
            .WatchAsync(Cancel);

        await using CarinaDbContext reader = database.Open();
        Recording read = await reader.Set<Recording>().SingleAsync(row => row.Id == recording.Id);

        Assert.Equal(1, watch.Settled);
        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Equal(
            [RecordingFault.StoppedUnasked, RecordingFault.ShortOfTheWindow],
            read.OutcomeDetail.Select(detail => detail.Fault).ToArray());
        Assert.All(read.OutcomeDetail, detail => Assert.Equal(Ended, detail.NoticedAt));
        Assert.All(read.OutcomeDetail, detail => Assert.Equal(DateTimeKind.Utc, detail.NoticedAt.Kind));
        Assert.Contains("0.9722", Assert.Single(read.OutcomeDetail, detail => detail.Fault is RecordingFault.ShortOfTheWindow).Note, StringComparison.Ordinal);
        Assert.Equal(3_300_000_000, read.FileSizeObserved);
        Assert.Equal(Ended, read.StoppedAtActual);
        Assert.Equal("Truncated", await Projected(reservation.Id));
    }

    [Fact]
    public async Task ACountThatLandedOnARowSomethingElseMovedIsWrittenOnTopOfWhatLanded()
    {
        Recording recording = Begin(6202, null);
        await Add(recording);

        var driver = new WatchedDriver();
        driver.Holding[RecordingSessions.Named(recording.Id)] = Live(
            recording,
            Airs,
            new SessionCounters(Packets: 1000, Drops: 3, CcMeasured: true));

        RecordingWatch watch = await Supervisor(
                driver,
                new WeighedFiles { Weighs = 0 },
                new WatchClock(Airs.AddMinutes(10)),
                recording.Id,
                () => ExtendAsync(recording.Id, Airs.AddHours(2)))
            .WatchAsync(Cancel);

        await using CarinaDbContext reader = database.Open();
        Recording read = await reader.Set<Recording>().SingleAsync(row => row.Id == recording.Id);

        Assert.Equal(1, watch.Collisions);
        Assert.Equal(DropCounters.Counted(3, 1003), read.Counters);
        Assert.Equal(TimeSpan.FromMinutes(10), read.Written);
        Assert.Equal(Airs.AddHours(2), read.ExpectedWindowEnd);
        Assert.Equal(Airs.AddMinutes(10), read.MeasuredUpdatedAt);
    }

    private sealed class OnceOnly
    {
        private int done;

        public bool First() => Interlocked.Exchange(ref done, 1) is 0;
    }

    private sealed class OneRecordingLedger(
        IRecordingRepository inner,
        RecordingId watched,
        OnceOnly once,
        Func<Task>? collide) : IRecordingRepository
    {
        public async Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
        {
            Recording? found = await inner.FindAsync(id, cancellationToken);

            if (collide is not null && watched.Equals(id) && once.First())
            {
                await collide();
            }

            return found;
        }

        public async Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
            => [.. (await inner.ListInFlightAsync(cancellationToken)).Where(row => watched.Equals(row.Id))];

        public Task<IReadOnlyList<Recording>> ListForReservationAsync(
            ReservationId reservationId,
            CancellationToken cancellationToken)
            => inner.ListForReservationAsync(reservationId, cancellationToken);

        public Task AddAsync(Recording recording, CancellationToken cancellationToken)
            => inner.AddAsync(recording, cancellationToken);

        public Task SaveAsync(Recording recording, CancellationToken cancellationToken)
            => inner.SaveAsync(recording, cancellationToken);
    }

    private async Task ExtendAsync(RecordingId id, DateTime until)
    {
        await using CarinaDbContext other = database.Open();
        Recording theirs = await other.Set<Recording>().SingleAsync(row => row.Id == id);
        theirs.Extend(until);
        await other.SaveChangesAsync();
    }

    private RecordingStreamSupervisor Supervisor(
        WatchedDriver driver,
        WeighedFiles files,
        TimeProvider clock,
        RecordingId watched,
        Func<Task>? collide)
    {
        var services = new ServiceCollection();
        var once = new OnceOnly();

        services.AddScoped(_ => database.Open());
        services.AddScoped<IRecordingRepository>(provider => new OneRecordingLedger(
            new RecordingRepository(provider.GetRequiredService<CarinaDbContext>()),
            watched,
            once,
            collide));
        services.AddScoped<IServiceTuningDirectory>(_ => new ResolvedTuning(Terrestrial));

        return new RecordingStreamSupervisor(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            driver,
            new HeldStatus(Connected()),
            files,
            Settings,
            clock,
            NullLogger<RecordingStreamSupervisor>.Instance);
    }

    private static Reservation Plan(int eventId)
        => Reservation.Plan(
            ReservationId.New(),
            Programme(eventId),
            null,
            Priority.Default,
            Airs,
            Airs.AddMinutes(30),
            true,
            Margin.None,
            Margin.None,
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Airs);

    private static Recording Begin(int eventId, ReservationId? reservationId)
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            reservationId,
            Programme(eventId),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            Airs,
            Airs.AddMinutes(30),
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Airs,
            new TunerDeviceId("adapter1"));
    }

    private static ProgrammeRef Programme(int eventId)
        => new(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), Airs);

    private static ProgrammeSnapshot Snapshot()
        => new("A programme", "What it is about", string.Empty, [new ProgrammeGenre(7, 1)], Airs);

    private async Task Add<T>(T entity)
        where T : class
    {
        await using CarinaDbContext context = database.Open();
        context.Add(entity);
        await context.SaveChangesAsync();
    }

    private async Task Claim(ReservationId id)
        => await RunAsync(
            $"UPDATE reservation SET started_at = timestamptz '2026-08-26 20:00:00+00' WHERE id = '{id.Value}'");

    private async Task<string?> Projected(ReservationId id)
    {
        await using CarinaDbContext context = database.Open();
        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var reading = new NpgsqlCommand(
            $"SELECT recording_outcome FROM reservation WHERE id = '{id.Value}'",
            connection);
        object? read = await reading.ExecuteScalarAsync();

        return read is DBNull ? null : read as string;
    }

    private async Task RunAsync(string sql)
    {
        await using CarinaDbContext context = database.Open();
        await using var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var running = new NpgsqlCommand(sql, connection);
        await running.ExecuteNonQueryAsync();
    }
}
