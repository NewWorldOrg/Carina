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

    private const string BeforeWhatBecameOfThem = "20261019121457_VersionsWeighNothingTheCatalogHears";

    private const string BeforeTheVersionTheyMeasured = "20261021090000_AVersionMadeByHandTakesAdvertsInPods";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly string[] VersionsInUseOverTime =
    [
        """
        INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at, retired_at, rolled_back_at, rollback) VALUES
            (1, 'Retired', false, 2, '{}', '{}', '{}', '{}', '{}', '2026-08-01T00:00:00Z', '2026-08-01T00:00:00Z', '2026-09-10T12:00:00Z', NULL, NULL),
            (2, 'Active', false, 2, '{}', '{}', '{}', '{}', '{}', '2026-09-01T00:00:00Z', '2026-09-28T03:00:20Z', NULL, NULL, NULL),
            (3, 'Retired', false, 2, '{}', '{}', '{}', '{}', '{}', '2026-09-15T00:00:00Z', '2026-09-20T12:00:00Z', '2026-09-28T03:00:20Z', '2026-09-28T03:00:20Z', '{}'::jsonb),
            (4, 'Retired', false, 2, '{}', '{}', '{}', '{}', '{}', '2026-09-30T00:00:00Z', '2026-10-01T00:00:00Z', '2026-10-03T00:00:00Z', NULL, NULL)
        """,
        """
        INSERT INTO segment_learning_round (day, taken_at, candidate, outcome, verdict, in_use_metrics) VALUES
            ('2026-07-02', '2026-07-01T18:00:00Z', 5, 'Kept', 'TooLittleEvidence', '{}'::jsonb),
            ('2026-08-16', '2026-08-15T18:00:00Z', 5, 'Kept', 'TooLittleEvidence', '{}'::jsonb),
            ('2026-09-13', '2026-09-12T18:00:00Z', 5, 'Kept', 'TooLittleEvidence', '{}'::jsonb),
            ('2026-09-26', '2026-09-25T18:00:00Z', 5, 'Kept', 'TooLittleEvidence', '{}'::jsonb),
            ('2026-09-28', '2026-09-28T03:00:00Z', 5, 'Kept', 'TooLittleEvidence', '{}'::jsonb),
            ('2026-09-30', '2026-09-29T18:00:00Z', 5, 'Kept', 'TooLittleEvidence', '{}'::jsonb),
            ('2026-10-01', '2026-09-30T18:00:00Z', 5, 'Kept', 'TooLittleEvidence', NULL),
            ('2026-10-03', '2026-10-02T18:00:00Z', 5, 'Kept', 'TooLittleEvidence', '{}'::jsonb)
        """,
    ];

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

    [Fact(DisplayName = "BR-CD-008: a round is kept with what became of it, the verdict on its candidate and the figures of the version in use, and the latest round that says what became of it is read back with all three")]
    public async Task ARoundIsKeptWithWhatBecameOfIt()
    {
        DateTime first = new(2026, 8, 9, 18, 6, 0, DateTimeKind.Utc);
        SegmentMetrics inUse = new(0.08, 0.05, [new KindMetrics(SegmentKind.Break, 0.38)], null, 0);
        await TakeAsync(SegmentLearningRound.Ended(SegmentLearningRound.DayOf(first), first, SegmentTrainingOutcome.Kept, 4, AdoptionVerdict.TooLittleEvidence, inUse));
        await TakeAsync(SegmentLearningRound.Taken(first.AddDays(1), 5));

        SegmentLearningRound? latest = await LatestAsync();

        Assert.NotNull(latest);
        Assert.Equal(
            (new DateOnly(2026, 8, 10), first, (int?)4, (SegmentTrainingOutcome?)SegmentTrainingOutcome.Kept, (AdoptionVerdict?)AdoptionVerdict.TooLittleEvidence),
            (latest.Day, latest.TakenAt, latest.Candidate, latest.Outcome, latest.Verdict));
        Assert.Equal((0.08, 0.05, 0.38), (latest.InUseMetrics?.BoundaryRecallWithin2s, latest.InUseMetrics?.BoundaryRecallWithin1s, latest.InUseMetrics?.BreakRecall));
        Assert.Equal(
            "Kept TooLittleEvidence 0.08 4 | - - - 5",
            await ScalarAsync(
                """
                SELECT string_agg(concat_ws(' ', coalesce(outcome, '-'), coalesce(verdict, '-'), coalesce(in_use_metrics->>'boundaryRecallWithin2s', '-'), candidate), ' | ' ORDER BY day)
                FROM segment_learning_round WHERE day BETWEEN '2026-08-10' AND '2026-08-11'
                """));
    }

    [Fact(DisplayName = "BR-CD-008: the latest round that measured a version is read back whatever came after it, round by round for each version, and the latest round that says what became of it stays the last of them")]
    public async Task TheLatestRoundThatMeasuredAVersionIsReadBackWhateverCameAfterIt()
    {
        DateTime first = new(2028, 3, 1, 18, 6, 0, DateTimeKind.Utc);
        SegmentMetrics older = new(0.31, 0.2, [new KindMetrics(SegmentKind.Break, 0.5)], null, 0);
        SegmentMetrics newer = new(0.44, 0.3, [new KindMetrics(SegmentKind.Break, 0.6)], null, 0);
        SegmentMetrics latest = new(0.52, 0.4, [new KindMetrics(SegmentKind.Break, 0.7)], null, 0);

        try
        {
            await TakeAsync(Round(first, SegmentTrainingOutcome.Kept, 6, older, 1));
            await TakeAsync(Round(first.AddDays(1), SegmentTrainingOutcome.NothingNew));
            await TakeAsync(Round(first.AddDays(2), SegmentTrainingOutcome.Failed));
            await TakeAsync(SegmentLearningRound.Taken(first.AddDays(3)));

            SegmentLearningRound? measured = await LatestMeasuredAsync(1);

            Assert.NotNull(measured);
            Assert.Equal((first, (int?)6, (int?)1, 0.31, 0.5), (measured.TakenAt, measured.Candidate, measured.InUseVersion, measured.InUseMetrics?.BoundaryRecallWithin2s, measured.InUseMetrics?.BreakRecall));
            Assert.Null(await LatestMeasuredAsync(2));
            Assert.Equal(SegmentTrainingOutcome.Failed, (await LatestAsync())?.Outcome);

            await TakeAsync(Round(first.AddDays(4), SegmentTrainingOutcome.Kept, 7, newer, 2));
            await TakeAsync(Round(first.AddDays(5), SegmentTrainingOutcome.NothingToLearnFrom));

            Assert.Equal(first, (await LatestMeasuredAsync(1))?.TakenAt);
            Assert.Equal((first.AddDays(4), (int?)7, 0.44), ((await LatestMeasuredAsync(2))?.TakenAt, (await LatestMeasuredAsync(2))?.Candidate, (await LatestMeasuredAsync(2))?.InUseMetrics?.BoundaryRecallWithin2s));
            Assert.Equal(SegmentTrainingOutcome.NothingToLearnFrom, (await LatestAsync())?.Outcome);

            await TakeAsync(Round(first.AddDays(6), SegmentTrainingOutcome.Kept, 8, latest, 1));

            Assert.Equal((first.AddDays(6), 0.52), ((await LatestMeasuredAsync(1))?.TakenAt, (await LatestMeasuredAsync(1))?.InUseMetrics?.BoundaryRecallWithin2s));
            Assert.Equal(first.AddDays(4), (await LatestMeasuredAsync(2))?.TakenAt);
            Assert.Equal(
                "1 - - - 2 - 1",
                await ScalarAsync("SELECT string_agg(coalesce(in_use_version::text, '-'), ' ' ORDER BY day) FROM segment_learning_round WHERE day >= '2028-01-01'"));
        }
        finally
        {
            await ScalarAsync("DELETE FROM segment_learning_round WHERE day >= '2028-01-01'");
        }
    }

    [Fact(DisplayName = "BR-CD-008: a round whose figures were kept without the number of the version they are of is read back for no version")]
    public async Task AFiguresRoundWithoutAVersionNumberIsReadBackForNoVersion()
    {
        DateTime at = new(2028, 5, 1, 18, 6, 0, DateTimeKind.Utc);

        try
        {
            await TakeAsync(Round(at, SegmentTrainingOutcome.Kept, 6, new SegmentMetrics(0.31, 0.2, [], null, 0), null));

            Assert.Null(await LatestMeasuredAsync(1));
            Assert.Null(await LatestMeasuredAsync(6));
        }
        finally
        {
            await ScalarAsync("DELETE FROM segment_learning_round WHERE day >= '2028-01-01'");
        }
    }

    [Theory(DisplayName = "BR-CD-008: no round keeps the number of the version it measured without having kept a candidate, nor a number below the first")]
    [InlineData("2027-05-01", "NULL", "'NothingNew'", "1")]
    [InlineData("2027-05-02", "3", "'Kept'", "0")]
    public async Task NoRoundKeepsTheNumberOfAVersionItDidNotMeasureOrWhichIsNoNumber(string day, string candidate, string outcome, string inUseVersion)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync(
            connection,
            $"INSERT INTO segment_learning_round (day, taken_at, candidate, outcome, in_use_version) VALUES ('{day}', '{day}T00:00:00Z', {candidate}, {outcome}, {inUseVersion})"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
    }

    [Theory(DisplayName = "BR-CD-004: no round is kept as stopped, and none says how a candidate was judged without having kept one")]
    [InlineData("2027-04-01", "NULL", "'Stopped'", "NULL", "NULL")]
    [InlineData("2027-04-02", "NULL", "'NothingNew'", "'Adopted'", "NULL")]
    [InlineData("2027-04-03", "NULL", "'NothingNew'", "NULL", "'{}'::jsonb")]
    [InlineData("2027-04-04", "3", "'Kept'", "'Unheard'", "NULL")]
    [InlineData("2027-04-05", "3", "'Kept'", "'Adopted'", "'[]'::jsonb")]
    public async Task NoRoundIsKeptAsStoppedOrJudgedWithoutACandidate(string day, string candidate, string outcome, string verdict, string inUse)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => ScalarAsync(
            connection,
            $"INSERT INTO segment_learning_round (day, taken_at, candidate, outcome, verdict, in_use_metrics) VALUES ('{day}', '{day}T00:00:00Z', {candidate}, {outcome}, {verdict}, {inUse})"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
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

    [Fact(DisplayName = "what became of the rounds goes back with the migration that kept it, leaving the rounds and their candidates, and comes forward again empty")]
    public async Task WhatBecameOfTheRoundsGoesBackAndComesForwardAgain()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: Cancel);
        DateTime at = new(2026, 9, 2, 10, 30, 0, DateTimeKind.Utc);
        await new SegmentLearningRoundRepository(context).TakeAsync(
            SegmentLearningRound.Ended(SegmentLearningRound.DayOf(at), at, SegmentTrainingOutcome.Kept, 3, AdoptionVerdict.NoBetterAtBoundaries, new SegmentMetrics(0.5, 0.4, [], null, 0)),
            Cancel);

        await migrator.MigrateAsync(BeforeWhatBecameOfThem, Cancel);

        await using (NpgsqlConnection back = await OpenAsync(Scratch()))
        {
            Assert.Equal(
                "0",
                await ScalarAsync(back, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'segment_learning_round' AND column_name IN ('outcome', 'verdict', 'in_use_metrics')"));
            Assert.Equal("2026-09-02 3", await ScalarAsync(back, "SELECT concat_ws(' ', day::text, candidate) FROM segment_learning_round"));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection forward = await OpenAsync(Scratch());
        Assert.Equal(
            "3 t",
            await ScalarAsync(forward, "SELECT concat_ws(' ', candidate, outcome IS NULL AND verdict IS NULL AND in_use_metrics IS NULL) FROM segment_learning_round"));
        await context.Database.EnsureDeletedAsync(Cancel);
    }

    [Fact(DisplayName = "BR-CD-008: the number of the version a round measured comes forward as the one version in use when the round was taken, and is left empty when none or two were, when the round kept no figures, or when a version was rolled back from within the hour after it; going back drops it")]
    public async Task TheNumberOfTheVersionMeasuredComesForwardAsTheOneVersionInUse()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheVersionTheyMeasured, Cancel);

        await using (NpgsqlConnection before = await OpenAsync(Scratch()))
        {
            foreach (string sql in VersionsInUseOverTime)
            {
                await ScalarAsync(before, sql);
            }

            Assert.Equal(
                "0",
                await ScalarAsync(before, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'segment_learning_round' AND column_name = 'in_use_version'"));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection forward = await OpenAsync(Scratch()))
        {
            Assert.Equal(
                "- 1 - 3 - 2 - -",
                await ScalarAsync(forward, "SELECT string_agg(coalesce(in_use_version::text, '-'), ' ' ORDER BY day) FROM segment_learning_round"));
        }

        await migrator.MigrateAsync(BeforeTheVersionTheyMeasured, Cancel);

        await using (NpgsqlConnection back = await OpenAsync(Scratch()))
        {
            Assert.Equal(
                "0 8",
                await ScalarAsync(back, "SELECT concat_ws(' ', (SELECT count(*) FROM information_schema.columns WHERE table_name = 'segment_learning_round' AND column_name = 'in_use_version'), (SELECT count(*) FROM segment_learning_round))"));
        }

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

    private async Task<SegmentLearningRound?> LatestAsync()
    {
        await using CarinaDbContext context = Context();

        return await new SegmentLearningRoundRepository(context).LatestAsync(Cancel);
    }

    private async Task<SegmentLearningRound?> LatestMeasuredAsync(int version)
    {
        await using CarinaDbContext context = Context();

        return await new SegmentLearningRoundRepository(context).LatestMeasuredAsync(version, Cancel);
    }

    private static SegmentLearningRound Round(DateTime at, SegmentTrainingOutcome outcome, int? candidate = null, SegmentMetrics? inUse = null, int? inUseVersion = null)
        => SegmentLearningRound.Ended(
            SegmentLearningRound.DayOf(at),
            at,
            outcome,
            candidate,
            candidate is null ? null : AdoptionVerdict.TooLittleEvidence,
            inUse,
            inUseVersion);

    private async Task<bool> TakeAsync(SegmentLearningRound round)
    {
        await using CarinaDbContext context = Context();

        return await new SegmentLearningRoundRepository(context).TakeAsync(round, Cancel);
    }
}
