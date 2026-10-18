using System.Globalization;

using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentLearningRoundSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string ScratchDatabase = "carina_segment_learning_round_rollback_test";

    private const string BeforeTheRounds = "20261018181106_SkipButtonsAreOfferedFromAThresholdTheSettingsKeep";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-004: the round of a day is taken once and kept, and a second taker of the same day is told it was not")]
    public async Task TheRoundOfADayIsTakenOnce()
    {
        DateTime at = new(2026, 9, 2, 10, 30, 0, DateTimeKind.Utc);
        SegmentLearningRound round = SegmentLearningRound.Taken(at);

        Assert.False(await TakenAsync(round.Day));
        Assert.True(await TakeAsync(round));
        Assert.False(await TakeAsync(SegmentLearningRound.Taken(at.AddHours(5))));
        Assert.True(await TakenAsync(round.Day));
        Assert.False(await TakenAsync(round.Day.AddDays(1)));
        Assert.Equal(
            "1 t",
            await ScalarAsync("SELECT concat_ws(' ', count(*), bool_and(taken_at = '2026-09-02T10:30:00Z'::timestamptz)) FROM segment_learning_round WHERE day = '2026-09-02'"));
    }

    [Fact(DisplayName = "BR-CD-004: a round is kept with the candidate it kept, and the last round that kept one is the latest so kept")]
    public async Task ARoundIsKeptWithTheCandidateItKept()
    {
        DateTime first = new(2026, 9, 5, 10, 30, 0, DateTimeKind.Utc);
        await TakeAsync(SegmentLearningRound.Taken(first, 2));
        await TakeAsync(SegmentLearningRound.Taken(first.AddDays(1), 3));
        await TakeAsync(SegmentLearningRound.Taken(first.AddDays(2)));

        Assert.Equal(first.AddDays(1), await LastLearnedAsync());
        Assert.Equal(
            "2 3 NULL",
            await ScalarAsync("SELECT string_agg(coalesce(candidate::text, 'NULL'), ' ' ORDER BY day) FROM segment_learning_round WHERE day BETWEEN '2026-09-05' AND '2026-09-07'"));
    }

    [Fact(DisplayName = "BR-CD-004: one process holds the turn to take a round at a time, without waiting, until it lets go")]
    public async Task OneProcessHoldsTheTurnAtATime()
    {
        await using CarinaDbContext first = Context();
        await using CarinaDbContext second = Context();
        await using CarinaDbContext third = Context();

        IAsyncDisposable? held = await new SegmentLearningRoundRepository(first).HoldAsync(Cancel);
        IAsyncDisposable? meanwhile = await new SegmentLearningRoundRepository(second).HoldAsync(Cancel);
        await held!.DisposeAsync();
        await using IAsyncDisposable? after = await new SegmentLearningRoundRepository(third).HoldAsync(Cancel);

        Assert.NotNull(held);
        Assert.Null(meanwhile);
        Assert.NotNull(after);
    }

    [Fact(DisplayName = "the rounds go back with the migration that made them and come forward again empty")]
    public async Task TheRoundsGoBackAndComeForwardAgain()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: Cancel);
        await new SegmentLearningRoundRepository(context).TakeAsync(SegmentLearningRound.Taken(new DateTime(2026, 9, 2, 10, 30, 0, DateTimeKind.Utc)), Cancel);

        await migrator.MigrateAsync(BeforeTheRounds, Cancel);

        await using (NpgsqlConnection back = await OpenAsync(Scratch()))
        {
            Assert.Equal("0", await ScalarAsync(back, "SELECT count(*) FROM pg_tables WHERE tablename = 'segment_learning_round'"));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection forward = await OpenAsync(Scratch());
        Assert.Equal("0", await ScalarAsync(forward, "SELECT count(*) FROM segment_learning_round"));
        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string Scratch()
    {
        string connection = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable)
                            ?? throw new InvalidOperationException($"{CarinaDbContextFactory.ConnectionStringVariable} names the database the tests make theirs beside.");

        return new NpgsqlConnectionStringBuilder(connection) { Database = ScratchDatabase }.ConnectionString;
    }

    private static async Task<NpgsqlConnection> OpenAsync(string connectionString)
    {
        NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(Cancel);

        return connection;
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand reading = new(sql, connection);

        return Convert.ToString(await reading.ExecuteScalarAsync(Cancel), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task<string> ScalarAsync(string sql)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        return await ScalarAsync(connection, sql);
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task<bool> TakenAsync(DateOnly day)
    {
        await using CarinaDbContext context = Context();

        return await new SegmentLearningRoundRepository(context).TakenAsync(day, Cancel);
    }

    private async Task<DateTime?> LastLearnedAsync()
    {
        await using CarinaDbContext context = Context();

        return await new SegmentLearningRoundRepository(context).LastLearnedAsync(Cancel);
    }

    private async Task<bool> TakeAsync(SegmentLearningRound round)
    {
        await using CarinaDbContext context = Context();

        return await new SegmentLearningRoundRepository(context).TakeAsync(round, Cancel);
    }
}
