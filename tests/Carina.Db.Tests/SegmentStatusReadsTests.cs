using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentStatusReadsTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>, IAsyncLifetime
{
    private const int Other = 52;

    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime WeekAgo = Now - SegmentLearning.ReturnsOver;

    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    public async Task InitializeAsync()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await ExecuteAsync(
            connection,
            $$"""
            DELETE FROM segment_answer;
            DELETE FROM segment_skip_event;
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at)
            VALUES ({{Other}}, 'Candidate', false, 2, '{}', '{}', '{}', '{}', '{}', now())
            ON CONFLICT DO NOTHING;
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "BR-CD-008: the answers give an evaluation the confirmed ones that stand and the boundaries of breaks they draw; a withdrawn answer and one not confirmed are left out, and one sent again stands again")]
    public async Task TheAnswersGiveTheConfirmedOnesThatStand()
    {
        RecordingId sentAgain = RecordingId.New();
        RecordingId withdrawn = RecordingId.New();
        await AnswerAsync(Given(RecordingId.New(), true, Now.AddHours(-5), (0, 300, SegmentKind.Programme), (300, 360, SegmentKind.Break), (360, 900, SegmentKind.Programme), (900, 960, SegmentKind.Break), (960, 1800, SegmentKind.Programme)));
        await AnswerAsync(Given(RecordingId.New(), false, Now.AddHours(-4), (0, 300, SegmentKind.Programme), (300, 360, SegmentKind.Break), (360, 1800, SegmentKind.Programme)));
        await AnswerAsync(Given(withdrawn, true, Now.AddHours(-3), (0, 300, SegmentKind.Programme), (300, 360, SegmentKind.Break), (360, 1800, SegmentKind.Programme)));
        await WithdrawAsync(withdrawn, Now.AddHours(-2));
        await AnswerAsync(Given(sentAgain, true, Now.AddHours(-6), (0, 1740, SegmentKind.Programme), (1740, 1800, SegmentKind.Break)));
        await WithdrawAsync(sentAgain, Now.AddHours(-5));
        await AnswerAsync(Given(sentAgain, true, Now.AddHours(-1), (0, 600, SegmentKind.Programme), (600, 660, SegmentKind.Break), (660, 1800, SegmentKind.Programme)));

        EvaluationEvidence evidence = await EvidenceAsync();

        Assert.Equal(new EvaluationEvidence(2, 6, Now.AddHours(-1)), evidence);
    }

    [Fact(DisplayName = "with no answer kept the answers give an evaluation nothing to stand on")]
    public async Task WithNoAnswerKeptTheAnswersGiveNothing()
        => Assert.Equal(new EvaluationEvidence(0, 0, null), await EvidenceAsync());

    [Fact(DisplayName = "BR-CD-008: what viewers did with one version's buttons over the last seven days counts its skips and returns, and nothing older, of another version, of an answer or closing a button")]
    public async Task OneVersionsSkipsAndReturnsOverTheLastSevenDaysAreCounted()
    {
        await SkipAsync(ViewerActionKind.Skipped, 8, Now.AddDays(-1), SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Skipped, 1, WeekAgo, SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Skipped, 1, Now, SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Returned, 3, Now.AddDays(-2), SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Dismissed, 4, Now.AddDays(-2), SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Skipped, 5, WeekAgo.AddTicks(-TimeSpan.TicksPerSecond), SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Returned, 5, Now.AddDays(-8), SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Skipped, 6, Now.AddDays(-1), Other);
        await SkipAsync(ViewerActionKind.Returned, 6, Now.AddDays(-1), Other);
        await SkipAsync(ViewerActionKind.Skipped, 7, Now.AddDays(-1), null);

        SkipReturns returns = await ReturnsAsync(SegmentModelVersion.FirstNumber);
        SkipReturns other = await ReturnsAsync(Other);

        Assert.Equal((10, 3, 0.3), (returns.Skips, returns.Returned, returns.Share!.Value));
        Assert.Equal((6, 6, 1.0), (other.Skips, other.Returned, other.Share!.Value));
    }

    [Fact(DisplayName = "a version whose buttons nobody pressed lately has no share that came back")]
    public async Task AVersionNobodySkippedWithHasNoShare()
    {
        await SkipAsync(ViewerActionKind.Returned, 2, Now.AddDays(-1), Other);

        SkipReturns returns = await ReturnsAsync(Other);

        Assert.Equal((0, 2), (returns.Skips, returns.Returned));
        Assert.Null(returns.Share);
        Assert.Equal(SkipReturns.None.Skips, (await ReturnsAsync(SegmentModelVersion.FirstNumber)).Skips);
    }

    private static RecordingAnswer Given(RecordingId recording, bool confirmed, DateTime at, params (double Starts, double Ends, SegmentKind Kind)[] stretches)
        => RecordingAnswer.Given(
            recording,
            AnsweredStretches.Read([.. stretches.Select(stretch => new SentStretch(stretch.Starts, stretch.Ends, stretch.Kind))], HalfAnHour).Stretches!,
            confirmed,
            at);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string statement)
    {
        await using var command = new NpgsqlCommand(statement, connection);

        await command.ExecuteNonQueryAsync();
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task AnswerAsync(RecordingAnswer answer)
    {
        await using CarinaDbContext context = Context();

        await new SegmentAnswerRepository(context).AnswerAsync(answer, CancellationToken.None);
    }

    private async Task WithdrawAsync(RecordingId recording, DateTime at)
    {
        await using CarinaDbContext context = Context();

        Assert.True(await new SegmentAnswerRepository(context).WithdrawAsync(recording, at, CancellationToken.None));
    }

    private async Task<EvaluationEvidence> EvidenceAsync()
    {
        await using CarinaDbContext context = Context();

        return await new SegmentAnswerRepository(context).EvidenceAsync(CancellationToken.None);
    }

    private async Task SkipAsync(ViewerActionKind action, int count, DateTime at, int? version)
    {
        await using CarinaDbContext context = Context();
        SkipEventRepository skips = new(context);

        foreach (int _ in Enumerable.Range(0, count))
        {
            ViewerAction done = new(at, action, SegmentKind.Break, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360), version);

            Assert.True(await skips.TakeAsync(SkipEvent.Of(RecordingId.New(), done), CancellationToken.None));
        }
    }

    private async Task<SkipReturns> ReturnsAsync(int version)
    {
        await using CarinaDbContext context = Context();

        return await new SkipEventRepository(context).ReturnsAsync(version, WeekAgo, Now, CancellationToken.None);
    }
}
