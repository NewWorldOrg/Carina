using System.Globalization;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CandidatesLetGoTests : IAsyncLifetime
{
    private const string ScratchDatabase = "carina_candidates_let_go_test";

    private const string Profile = "4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b";

    private const string Destination = "5e6f7081-92a3-4b4c-85d6-e7f8091a2b3c";

    private static readonly DateTime Installed = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);

    private static readonly SegmentEvaluation Evaluated = new(
        new SegmentMetrics(0.5, 0.4, [new KindMetrics(SegmentKind.Break, 0.8)], null, 0),
        [],
        new EvaluationEvidence(12, 56, Installed));

    private static readonly CancellationToken Cancel = CancellationToken.None;

    public async Task InitializeAsync()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Cancel);
    }

    public async Task DisposeAsync()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
    }

    [Fact(DisplayName = "BR-CS-003: learning lets go of the candidates before its latest that nothing names; one a detection, a viewer's skip, the chapters of an encode or a round measuring the version in use names stays, and so do a version built in, the versions used and the latest, under their numbers")]
    public async Task LearningLetsGoOfTheCandidatesNothingNames()
    {
        await KeepBuiltInAsync(Installed);

        foreach (int number in new[] { 2, 3, 4, 5, 6, 9, 10 })
        {
            await AddAsync(SegmentModelVersion.Learned(number, HandMadeModels.First, Evaluated, Installed.AddDays(number)));
        }

        await AddAsync(SegmentModelVersion.Rehydrate(7, SegmentModelVersionState.Candidate, false, HandMadeModels.First, Installed.AddDays(7), null, null, null, null, builtIn: true));
        await AddAsync(SegmentModelVersion.Rehydrate(8, SegmentModelVersionState.Retired, false, HandMadeModels.First, Installed.AddDays(8), Installed.AddDays(8), Installed.AddDays(9), null, null, Evaluated));
        await ExecuteAsync(Detected(3));
        await SkippedAsync(4);
        await ExecuteAsync(Encoded(5));
        await ExecuteAsync(Round("2026-10-20", candidate: 9, inUseVersion: 6));

        IReadOnlyList<int> letGo = await LetGoBeforeAsync(10);

        Assert.Equal([2, 9], letGo);
        Assert.Equal("1:Active 3:Candidate 4:Candidate 5:Candidate 6:Candidate 7:Candidate 8:Retired 10:Candidate", await StatesAsync());
        Assert.Empty(await LetGoBeforeAsync(10));
    }

    [Fact(DisplayName = "BR-CS-003: a candidate chosen by hand is in use and not let go, nor the version it took the place of")]
    public async Task ACandidateChosenMeanwhileIsNotLetGo()
    {
        await KeepBuiltInAsync(Installed);
        await AddAsync(SegmentModelVersion.Learned(2, HandMadeModels.First, Evaluated, Installed.AddDays(1)));
        await AddAsync(SegmentModelVersion.Learned(3, HandMadeModels.First, Evaluated, Installed.AddDays(2)));

        await ChangeAsync(SegmentVersionChange.ChosenByHand(2), Started);

        Assert.Empty(await LetGoBeforeAsync(3));
        Assert.Equal("1:Retired 2:Active 3:Candidate", await StatesAsync());
    }

    [Fact(DisplayName = "BR-CS-003: after the candidates before it are let go, each next candidate goes on after the highest number given, and the numbers kept do not move")]
    public async Task TheNextCandidateGoesOnAfterThoseLetGo()
    {
        await KeepBuiltInAsync(Installed);
        await AddAsync(SegmentModelVersion.Learned(2, HandMadeModels.First, Evaluated, Installed.AddDays(1)));
        await AddAsync(SegmentModelVersion.Learned(3, HandMadeModels.First, Evaluated, Installed.AddDays(2)));

        SegmentModelVersion first = await AddCandidateAsync(Started);
        IReadOnlyList<int> firstLetGo = await LetGoBeforeAsync(first.Number);
        SegmentModelVersion second = await AddCandidateAsync(Started.AddDays(1));
        IReadOnlyList<int> secondLetGo = await LetGoBeforeAsync(second.Number);

        Assert.Equal((4, 5), (first.Number, second.Number));
        Assert.Equal([2, 3], firstLetGo);
        Assert.Equal([4], secondLetGo);
        Assert.Equal("1:Active 5:Candidate", await StatesAsync());
    }

    [Fact(DisplayName = "BR-CS-003: with the candidate of the highest number gone, a new version is numbered after the number a round of learning still remembers, never under it again")]
    public async Task TheHighestNumberGoneIsNotGivenAgain()
    {
        await KeepBuiltInAsync(Installed);
        SegmentModelVersion highest = await AddCandidateAsync(Started);
        await ExecuteAsync(Round("2026-11-03", candidate: highest.Number, inUseVersion: 1));
        await ExecuteAsync($"DELETE FROM segment_model_version WHERE number = {highest.Number}");

        SegmentModelVersion next = await AddCandidateAsync(Started.AddDays(1));

        Assert.Equal((2, 3), (highest.Number, next.Number));
        Assert.Equal("1:Active 3:Candidate", await StatesAsync());
    }

    [Fact(DisplayName = "BR-CS-003: on a version table emptied while rounds of learning still remember numbers, the built-in version goes on from the highest remembered, and so does the next candidate")]
    public async Task OnAnEmptiedTableTheBuiltInGoesOnFromTheRounds()
    {
        await KeepBuiltInAsync(Installed);
        await ExecuteAsync(Round("2026-10-24", candidate: 2, inUseVersion: 1));
        await ExecuteAsync(Round("2026-10-25", candidate: 4, inUseVersion: 3));
        await ExecuteAsync("TRUNCATE segment_model_version CASCADE");

        SegmentBuiltInKept? kept = await KeepBuiltInAsync(Started);
        SegmentModelVersion candidate = await AddCandidateAsync(Started.AddDays(1));

        Assert.Equal((5, 5, 6), (kept?.Added.Number, kept?.Active.Number, candidate.Number));
        Assert.Equal("5:Active 6:Candidate", await StatesAsync());
    }

    [Fact(DisplayName = "BR-CS-003: on a version table emptied while the chapters of an encode still remember a number, the built-in version goes on after it")]
    public async Task OnAnEmptiedTableTheBuiltInGoesOnFromTheChapters()
    {
        await KeepBuiltInAsync(Installed);
        await ExecuteAsync(Encoded(7));
        await ExecuteAsync("TRUNCATE segment_model_version CASCADE");

        SegmentBuiltInKept? kept = await KeepBuiltInAsync(Started);

        Assert.Equal((8, 8), (kept?.Added.Number, kept?.Active.Number));
    }

    [Fact(DisplayName = "BR-CS-003: the built-in version kept as a candidate beside a learned version in use is numbered after every number given, and the next candidate never lets it go, while it lets go of the learned candidate before it")]
    public async Task TheBuiltInCandidateIsNeverLetGo()
    {
        await AddAsync(SegmentModelVersion.Rehydrate(1, SegmentModelVersionState.Retired, false, HandMadeModels.First, Installed, Installed, Installed.AddDays(2), null, null, builtIn: true));
        await AddAsync(SegmentModelVersion.Rehydrate(3, SegmentModelVersionState.Active, false, HandMadeModels.First, Installed.AddDays(1), Installed.AddDays(2), null, null, null, Evaluated));
        await AddAsync(SegmentModelVersion.Learned(4, HandMadeModels.First, Evaluated, Installed.AddDays(5)));
        await ExecuteAsync(Round("2026-10-04", candidate: 2, inUseVersion: 1));
        await ExecuteAsync(Round("2026-10-05", candidate: 3, inUseVersion: 1));
        await ExecuteAsync(Round("2026-10-06", candidate: 4, inUseVersion: 3));

        SegmentBuiltInKept? kept = await KeepBuiltInAsync(Started);
        SegmentModelVersion candidate = await AddCandidateAsync(Started.AddDays(1));

        Assert.Equal((5, SegmentModelVersionState.Candidate, 3), (kept?.Added.Number, kept?.Added.State, kept?.Active.Number));
        Assert.Equal(6, candidate.Number);
        Assert.Equal([4], await LetGoBeforeAsync(candidate.Number));
        Assert.Equal("1:Retired 3:Active 5:Candidate 6:Candidate", await StatesAsync());
    }

    private static string Detected(int version)
        => $"INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at) VALUES (gen_random_uuid(), {version}, 'NothingFound', '[{{\"startsAtSec\": 0, \"endsAtSec\": 1800, \"kind\": \"Programme\", \"probability\": 0.9}}]', 0, '[]', 2, now(), now())";

    private static string Encoded(int version)
    {
        Guid recording = Guid.NewGuid();

        return $"""
                INSERT INTO encode_profile (id, label, codec, resolution, deinterlace, rate_factor, quantiser, defined_at)
                VALUES ('{Profile}', 'Viewing', 'H264', 'AsSource', 'Leave', 23, 24, timestamptz '2026-10-01 00:00:00+00');
                INSERT INTO encode_destination (id, label, output_root, default_profile_id, defined_at)
                VALUES ('{Destination}', 'Shelf', 'encodes', '{Profile}', timestamptz '2026-10-01 00:00:00+00');
                INSERT INTO encode_job (
                    id, recording_id, profile_id, destination_id, output_root, status, attempt,
                    queued_at, started_at, ended_at, artefact_name,
                    chapters_detector, chapters_verdict, chapters_break_share, chapters_decided_at, chapters_model_version)
                VALUES (
                    gen_random_uuid(), '{recording}', '{Profile}', '{Destination}', 'encodes', 'Completed', 1,
                    timestamptz '2026-10-01 03:00:00+00', timestamptz '2026-10-01 03:00:05+00', timestamptz '2026-10-01 03:30:00+00',
                    '{recording:N}.{Guid.Parse(Profile):N}.mp4', 'Segments', 'Marked', 0.2, timestamptz '2026-10-01 05:00:00+00', {version});
                """;
    }

    private static string Round(string day, int candidate, int inUseVersion)
        => $"""
            INSERT INTO segment_learning_round (day, taken_at, candidate, outcome, verdict, in_use_metrics, in_use_version)
            VALUES ('{day}', '{day}T00:00:00Z', {candidate}, 'Kept', 'NoBetterAtBoundaries', NULL, {inUseVersion})
            """;

    private static string Scratch()
    {
        string connection = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable)
                            ?? throw new InvalidOperationException($"{CarinaDbContextFactory.ConnectionStringVariable} names the database the tests make theirs beside.");

        return new NpgsqlConnectionStringBuilder(connection) { Database = ScratchDatabase }.ConnectionString;
    }

    private static async Task<SegmentBuiltInKept?> KeepBuiltInAsync(DateTime at)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());

        return await new SegmentModelVersionRepository(context).KeepBuiltInAsync(SegmentModel.BuiltIn, at, Cancel);
    }

    private static async Task<SegmentModelVersion> AddCandidateAsync(DateTime at)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());

        return await new SegmentModelVersionRepository(context).AddCandidateAsync(HandMadeModels.First, Evaluated, at, Cancel);
    }

    private static async Task<IReadOnlyList<int>> LetGoBeforeAsync(int latest)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());

        return await new SegmentModelVersionRepository(context).LetGoOfCandidatesBeforeAsync(latest, Cancel);
    }

    private static async Task ChangeAsync(SegmentVersionChange change, DateTime at)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());

        await new SegmentModelVersionRepository(context).ChangeAsync(change, at, Cancel);
    }

    private static async Task AddAsync(SegmentModelVersion version)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        context.Add(version);
        await context.SaveChangesAsync(Cancel);
    }

    private static async Task SkippedAsync(int version)
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());

        await new SkipEventRepository(context).TakeAsync(
            SkipEvent.Of(RecordingId.New(), new ViewerAction(Started, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), version)),
            Cancel);
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = new(Scratch());
        await connection.OpenAsync(Cancel);
        await using NpgsqlCommand command = new(sql, connection);

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<string> StatesAsync()
    {
        await using NpgsqlConnection connection = new(Scratch());
        await connection.OpenAsync(Cancel);
        await using NpgsqlCommand reading = new("SELECT string_agg(number || ':' || state, ' ' ORDER BY number) FROM segment_model_version", connection);

        return Convert.ToString(await reading.ExecuteScalarAsync(Cancel), CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
