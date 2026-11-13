using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using static Carina.Infrastructure.Tests.DbIntegration.Domain.Segments.ActionsLaidWithinAnHour;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Segments;

[Trait("Category", "DbIntegration")]
public sealed class SkipEventRepositoryTests(MigratedRepositoryDatabase database)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private const int Other = 52;

    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime WeekAgo = Now - SegmentLearning.ReturnsOver;

    private static readonly DateTime Installed = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly ServiceProvider container = database.Infrastructure();

    public async Task InitializeAsync()
    {
        await using (AsyncServiceScope scope = container.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISegmentModelVersionRepository>().KeepBuiltInAsync(SegmentModel.BuiltIn, Installed, CancellationToken.None);
        }

        await ExecuteAsync(
            $$"""
            DELETE FROM segment_skip_event;
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at)
            VALUES ({{Other}}, 'Candidate', false, 2, '{}', '{}', '{}', '{}', '{}', now())
            ON CONFLICT DO NOTHING;
            """);
    }

    public async Task DisposeAsync()
    {
        await ExecuteAsync($"DELETE FROM segment_skip_event; DELETE FROM segment_model_version WHERE number = {Other};");
        await container.DisposeAsync();
    }

    [Theory(DisplayName = "BR-CV-003: the store keeps a skip event exactly when the event says it is taken beside those its recording kept")]
    [MemberData(nameof(Arms), MemberType = typeof(ActionsLaidWithinAnHour))]
    public async Task TheStoreKeepsASkipWhenTheEventSaysSo(string arm, int withinTheHalfHour, double[] secondsFromTheNew)
    {
        RecordingId recording = RecordingId.New();
        DateTime[] laid = Laid(withinTheHalfHour, secondsFromTheNew);
        await LayAsync(container, "INSERT INTO segment_skip_event (recording_id, happened_at, event, kind, from_at, to_at) SELECT @recording, at, 'Skipped', 'Break', interval '915 seconds', interval '1002 seconds' FROM unnest(@laid) AS at", recording, laid);
        SkipEvent skip = SkipEvent.Of(recording, new ViewerAction(Noon, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(915), TimeSpan.FromSeconds(1002), null));

        await using AsyncServiceScope scope = container.CreateAsyncScope();

        Assert.True(skip.IsTakenBeside(laid) == await Skips(scope).TakeAsync(skip, CancellationToken.None), arm);
    }

    [Fact(DisplayName = "BR-CV-003: the arms reach both answers, so that agreeing is not agreeing on one")]
    public void TheArmsReachBothAnswers()
    {
        SkipEvent skip = SkipEvent.Of(RecordingId.New(), new ViewerAction(Noon, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(915), TimeSpan.FromSeconds(1002), null));
        bool[] answers = [.. Arms.Select(arm => skip.IsTakenBeside(Laid((int)arm[1], (double[])arm[2])))];

        Assert.Equal([true, false, false, true, true, true], answers);
    }

    [Fact(DisplayName = "BR-CD-008: what viewers did with one version's buttons over the last seven days counts its skips and returns, and nothing older, of another version, of no version or closing a button")]
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

    private async Task SkipAsync(ViewerActionKind action, int count, DateTime at, int? version)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        ISkipEventRepository skips = Skips(scope);

        foreach (int _ in Enumerable.Range(0, count))
        {
            ViewerAction done = new(at, action, SegmentKind.Break, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360), version);

            Assert.True(await skips.TakeAsync(SkipEvent.Of(RecordingId.New(), done), CancellationToken.None));
        }
    }

    private async Task<SkipReturns> ReturnsAsync(int version)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Skips(scope).ReturnsAsync(version, WeekAgo, Now, CancellationToken.None);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        CarinaDbContext context = scope.ServiceProvider.GetRequiredService<CarinaDbContext>();
        await context.Database.OpenConnectionAsync();
        await using System.Data.Common.DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        await command.ExecuteNonQueryAsync();
    }

    private static ISkipEventRepository Skips(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ISkipEventRepository>();
}
