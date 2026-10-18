using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class ChapterRewriteJobTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Recorded = TimeSpan.FromMinutes(30);

    private readonly ChapterRewriteHarness harness = new();

    private readonly HeldSegmentSettings settings = new();

    private readonly HeldSegmentModelVersions versions = new();

    private readonly ServiceProvider provider;

    private Busyness busy = new(false, false, null);

    public ChapterRewriteJobTests()
    {
        provider = new ServiceCollection()
            .AddSingleton<ISegmentSettingsRepository>(settings)
            .AddSingleton<IBusynessReader>(new HeldBusyness(() => busy))
            .AddScoped<ILearningSwitch, LearningSwitch>()
            .AddScoped<IOccupancyReader, OccupancyReader>()
            .AddSingleton<ISegmentModelVersionRepository>(versions)
            .AddScoped(_ => harness.Rewrites)
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        provider.Dispose();
        harness.Dispose();
    }

    [Fact(DisplayName = "BR-CD-013: while learning is off no artefact's chapters are compared or written again")]
    public async Task WhileLearningIsOffNothingIsWrittenAgain()
    {
        ChapterRewriteLook look = await Job().LookAsync(Cancel);

        Assert.Equal(SpareTimeVerdict.LearningOff, look.Verdict);
        Assert.Null(look.Round);
        Assert.Empty(harness.Worklist.Asked);
    }

    [Fact(DisplayName = "BR-CD-010: while something is recorded or watched no artefact's chapters are written again")]
    public async Task WhileTheMachineIsBusyNothingIsWrittenAgain()
    {
        Learning(true);
        busy = new Busyness(true, false, null);

        Assert.Equal(SpareTimeVerdict.Recording, (await Job().LookAsync(Cancel)).Verdict);

        busy = new Busyness(false, true, null);

        Assert.Equal(SpareTimeVerdict.Watching, (await Job().LookAsync(Cancel)).Verdict);
        Assert.Empty(harness.Worklist.Asked);
    }

    [Fact(DisplayName = "BR-CD-010: in spare time the artefacts are compared with the detections by the version in use, a few a look")]
    public async Task InSpareTimeTheArtefactsAreComparedWithTheVersionInUse()
    {
        Learning(true);
        versions.Active = HeldSegmentModelVersions.InUse(3, HeldSegmentModelVersions.Installed);

        ChapterRewriteLook look = await Job().LookAsync(Cancel);

        Assert.Equal(SpareTimeVerdict.Spare, look.Verdict);
        Assert.NotNull(look.Round);
        Assert.Equal((3, ChapterRewriteSettings.Default.AtMostALook), (harness.Worklist.Asked.Single().ModelVersion, harness.Worklist.Asked.Single().AtMost));
    }

    [Fact(DisplayName = "BR-CD-010: an artefact that could not be written again is passed over for a day")]
    public async Task AnArtefactThatFailedIsPassedOverForADay()
    {
        Learning(true);
        EncodeJob failing = harness.Stood(Recorded, [], [1, 2, 3]);
        harness.Awaiting(failing, SegmentDetectionState.Marked, null, Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, Recorded.TotalSeconds, SegmentKind.Programme));
        harness.Encodes.Standing("exit 1");
        ChapterRewriteJob job = Job();

        Assert.Equal([failing.Id], (await job.LookAsync(Cancel)).Round!.Failed);
        Assert.Equal(0, (await job.LookAsync(Cancel)).Round!.Read);
        Assert.Contains(failing.Id, harness.Worklist.Asked[1].PassedOver);

        ((HandTurnedClock)harness.Encodes.Clock).Turn(ChapterRewrite.OncePer);

        Assert.Equal(1, (await job.LookAsync(Cancel)).Round!.Read);
    }

    [Fact(DisplayName = "BR-CD-010: a round gives way once the machine is needed")]
    public async Task ARoundGivesWayOnceTheMachineIsNeeded()
    {
        Learning(true);
        EncodeJob job = harness.Stood(Recorded, [], [1, 2, 3]);
        harness.Awaiting(job, SegmentDetectionState.Marked, null, Span(0, 300, SegmentKind.Programme), Span(300, 360, SegmentKind.Break), Span(360, Recorded.TotalSeconds, SegmentKind.Programme));
        int asked = 0;
        ServiceProvider watched = new ServiceCollection()
            .AddSingleton<ISegmentSettingsRepository>(settings)
            .AddSingleton<IBusynessReader>(new HeldBusyness(() => new Busyness(false, ++asked > 1, null)))
            .AddScoped<ILearningSwitch, LearningSwitch>()
            .AddScoped<IOccupancyReader, OccupancyReader>()
            .AddSingleton<ISegmentModelVersionRepository>(versions)
            .AddScoped(_ => harness.Rewrites)
            .BuildServiceProvider();

        await using (watched)
        {
            ChapterRewriteLook look = await new ChapterRewriteJob(
                watched.GetRequiredService<IServiceScopeFactory>(),
                ChapterRewriteSettings.Default,
                harness.Encodes.Clock,
                NullLogger<ChapterRewriteJob>.Instance).LookAsync(Cancel);

            Assert.True(look.Round!.Yielded);
            Assert.Empty(harness.Encodes.Scratch.Files);
        }
    }

    private ChapterRewriteJob Job()
        => new(provider.GetRequiredService<IServiceScopeFactory>(), ChapterRewriteSettings.Default, harness.Encodes.Clock, NullLogger<ChapterRewriteJob>.Instance);

    private void Learning(bool on)
        => settings.SaveAsync(SegmentSettings.LearningSwitched(on, harness.Now), Cancel).GetAwaiter().GetResult();

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, 0.9);
}
