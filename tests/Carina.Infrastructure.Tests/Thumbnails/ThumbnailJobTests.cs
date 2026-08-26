using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Tests.Integrity;
using Carina.Infrastructure.Tests.Scanning;
using Carina.Infrastructure.Thumbnails;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Thumbnails;

public sealed class ThumbnailJobTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly OutputRoot Bulk = new("bulk");

    private static readonly OutputRoot Elsewhere = new("elsewhere");

    [Fact]
    public async Task ARecordingThatFailedIsSkippedAndNothingIsEverRun()
    {
        HeldWorklist worklist = new HeldWorklist().Holding(Subject(RecordingOutcome.Failed));
        HeldRenderer renderer = new();
        using ThumbnailJob job = Job(worklist, renderer);

        ThumbnailPass pass = await job.RunAsync(Cancel);

        Assert.Empty(renderer.Asked);
        Assert.Equal(ThumbnailState.Skipped, Assert.Single(worklist.Written).State);
        Assert.Null(Assert.Single(worklist.Written).Fault);
        Assert.Equal((1, 0, 1, 0, 0), (pass.Read, pass.Drawn, pass.Skipped, pass.Failed, pass.OutOfReach));
    }

    [Fact]
    public async Task ARecordingThatWasCutShortIsIllustratedAllTheSame()
    {
        HeldWorklist worklist = new HeldWorklist().Holding(Subject(RecordingOutcome.Truncated));
        HeldRenderer renderer = new();
        using ThumbnailJob job = Job(worklist, renderer);

        ThumbnailPass pass = await job.RunAsync(Cancel);

        Assert.Single(renderer.Asked);
        Assert.Equal(ThumbnailState.Ready, Assert.Single(worklist.Written).State);
        Assert.Equal((1, 1, 0, 0, 0), (pass.Read, pass.Drawn, pass.Skipped, pass.Failed, pass.OutOfReach));
    }

    [Fact]
    public async Task ARecordingThatFinishedIsIllustratedToo()
    {
        HeldWorklist worklist = new HeldWorklist().Holding(Subject(RecordingOutcome.Complete));
        HeldRenderer renderer = new();
        using ThumbnailJob job = Job(worklist, renderer);

        await job.RunAsync(Cancel);

        Assert.Single(renderer.Asked);
        Assert.Equal(ThumbnailState.Ready, Assert.Single(worklist.Written).State);
    }

    [Fact]
    public async Task ThePictureIsTakenWhereTheRuleSaysAndReadOutOfTheRootTheLedgerNames()
    {
        ThumbnailSubject subject = Subject(RecordingOutcome.Truncated, TimeSpan.FromSeconds(90));
        HeldWorklist worklist = new HeldWorklist().Holding(subject);
        HeldRenderer renderer = new();
        using ThumbnailJob job = Job(worklist, renderer);

        await job.RunAsync(Cancel);

        Assert.True(renderer.Asked.TryDequeue(out ThumbnailRequest? asked));
        Assert.Equal(TimeSpan.FromSeconds(30), asked!.At);
        Assert.Equal($"/srv/bulk/{subject.FileName.Value}", asked.Source);
        Assert.Equal($"/srv/pictures/{subject.Id.Wire}.jpg", asked.Destination);
    }

    [Fact]
    public void ThePictureIsAJpeg() => Assert.Equal(".jpg", ThumbnailJob.Extension);

    [Fact]
    public async Task AFailureIsKeptOnTheRecordingWithTheClassItBelongsTo()
    {
        HeldWorklist worklist = new HeldWorklist().Holding(Subject(RecordingOutcome.Complete));
        using ThumbnailJob job = Job(
            worklist,
            new HeldRenderer(_ => ThumbnailRender.Refused(234, "invalid data")));

        ThumbnailPass pass = await job.RunAsync(Cancel);

        Illustrated written = Assert.Single(worklist.Written);
        Assert.Equal(ThumbnailState.Failed, written.State);
        Assert.Equal(ThumbnailFault.Refused, written.Fault);
        Assert.Equal((1, 0, 0, 1, 0), (pass.Read, pass.Drawn, pass.Skipped, pass.Failed, pass.OutOfReach));
    }

    [Fact]
    public async Task APassCountsEachOfTheThreeAnswersSeparately()
    {
        ThumbnailSubject unwanted = Subject(RecordingOutcome.Failed);
        ThumbnailSubject stubborn = Subject(RecordingOutcome.Complete);
        ThumbnailSubject willing = Subject(RecordingOutcome.Truncated);
        HeldWorklist worklist = new HeldWorklist().Holding(unwanted, stubborn, willing);
        using ThumbnailJob job = Job(
            worklist,
            new HeldRenderer(request =>
                request.Source.Contains(stubborn.Id.Wire, StringComparison.Ordinal)
                    ? ThumbnailRender.Failed(ThumbnailFault.TimedOut, "took too long")
                    : ThumbnailRender.Drawn()));

        ThumbnailPass pass = await job.RunAsync(Cancel);

        Assert.Equal((3, 1, 1, 1, 0), (pass.Read, pass.Drawn, pass.Skipped, pass.Failed, pass.OutOfReach));
        Assert.Equal(0, pass.LeftForNextTime);
        Assert.Equal(
            [
                (unwanted.Id, ThumbnailState.Skipped),
                (stubborn.Id, ThumbnailState.Failed),
                (willing.Id, ThumbnailState.Ready),
            ],
            worklist.Written.Select(written => (written.Id, written.State)).ToArray());
    }

    [Fact]
    public async Task ARendererThatFallsOverLeavesTheRecordingWhereItWasAndDoesNotEscape()
    {
        HeldWorklist worklist = new HeldWorklist().Holding(Subject(RecordingOutcome.Complete));
        using ThumbnailJob job = Job(worklist, new ThrowingRenderer());

        ThumbnailPass pass = await job.RunAsync(Cancel);

        Assert.Empty(worklist.Written);
        Assert.Equal((1, 0, 0, 0, 0), (pass.Read, pass.Drawn, pass.Skipped, pass.Failed, pass.OutOfReach));
        Assert.Equal(1, pass.LeftForNextTime);
    }

    [Fact]
    public async Task OneRecordingFallingOverDoesNotStopTheNextOne()
    {
        ThumbnailSubject first = Subject(RecordingOutcome.Complete);
        ThumbnailSubject second = Subject(RecordingOutcome.Complete);
        HeldWorklist worklist = new HeldWorklist().Holding(first, second);
        using ThumbnailJob job = Job(
            worklist,
            new HeldRenderer(request =>
                request.Source.Contains(first.Id.Wire, StringComparison.Ordinal)
                    ? throw new InvalidOperationException("the renderer fell over")
                    : ThumbnailRender.Drawn()));

        ThumbnailPass pass = await job.RunAsync(Cancel);

        Assert.Equal(second.Id, Assert.Single(worklist.Written).Id);
        Assert.Equal((2, 1, 0, 0, 0), (pass.Read, pass.Drawn, pass.Skipped, pass.Failed, pass.OutOfReach));
    }

    [Fact]
    public async Task ARootNobodyToldThisProcessAboutIsNeverReadAndIsCountedWhereAPersonCanSeeIt()
    {
        HeldWorklist worklist = new HeldWorklist().Holding(
            Subject(RecordingOutcome.Complete, root: Elsewhere),
            Subject(RecordingOutcome.Complete, root: Elsewhere),
            Subject(RecordingOutcome.Complete));
        HeldRenderer renderer = new();
        using ThumbnailJob job = Job(worklist, renderer, Settings with { AtMostAPass = 2 });

        ThumbnailPass pass = await job.RunAsync(Cancel);

        Assert.Equal(["bulk"], worklist.AskedWithin.Select(root => root.Value));
        Assert.Single(renderer.Asked);
        Assert.Equal((1, 1, 0, 0, 2), (pass.Read, pass.Drawn, pass.Skipped, pass.Failed, pass.OutOfReach));
        Assert.Equal(0, pass.LeftForNextTime);
    }

    [Fact]
    public async Task RecordingsUnderARootOutOfReachDoNotHoldUpTheOnesUnderARootWithinIt()
    {
        ThumbnailSubject within = Subject(RecordingOutcome.Complete);
        HeldWorklist worklist = new HeldWorklist();
        worklist.Holding([.. Enumerable
            .Range(0, 8)
            .Select(_ => Subject(RecordingOutcome.Complete, root: Elsewhere))
            .Append(within)]);
        HeldRenderer renderer = new();
        using ThumbnailJob job = Job(worklist, renderer, Settings with { AtMostAPass = 8 });

        ThumbnailPass pass = await job.RunAsync(Cancel);

        Assert.Equal(within.Id, Assert.Single(worklist.Written).Id);
        Assert.Equal(ThumbnailState.Ready, worklist.Written[0].State);
        Assert.Equal((1, 1, 0, 0, 8), (pass.Read, pass.Drawn, pass.Skipped, pass.Failed, pass.OutOfReach));
    }

    [Fact]
    public async Task APassAsksForNoMoreThanItWasToldTo()
    {
        HeldWorklist worklist = new HeldWorklist().Holding(
            Subject(RecordingOutcome.Complete),
            Subject(RecordingOutcome.Complete),
            Subject(RecordingOutcome.Complete));
        using ThumbnailJob job = Job(worklist, new HeldRenderer(), Settings with { AtMostAPass = 2 });

        ThumbnailPass pass = await job.RunAsync(Cancel);

        Assert.Equal(2, worklist.AskedFor);
        Assert.Equal((2, 2, 0, 0, 0), (pass.Read, pass.Drawn, pass.Skipped, pass.Failed, pass.OutOfReach));
    }

    [Fact]
    public async Task WithNowhereToPutThemNothingIsEvenRead()
    {
        HeldWorklist worklist = new HeldWorklist().Holding(Subject(RecordingOutcome.Complete));
        using ThumbnailJob job = Job(worklist, new HeldRenderer(), Settings with { WrittenTo = null });

        ThumbnailPass pass = await job.RunAsync(Cancel);

        Assert.True(pass.NowhereToPutThem);
        Assert.Equal(0, worklist.Reads);
    }

    [Fact]
    public async Task ASecondPassIsRefusedWhileTheFirstIsStillGoing()
    {
        var worklist = new HeldWorklist { Gate = new TaskCompletionSource() };
        worklist.Holding(Subject(RecordingOutcome.Complete));
        using ThumbnailJob job = Job(worklist, new HeldRenderer());

        Task<ThumbnailPass> first = job.RunAsync(Cancel);
        await Eventually.Happens(() => worklist.Reads is 1, "the first pass never reached the worklist");

        ThumbnailPass second = await job.RunAsync(Cancel).WaitAsync(Eventually.Patience);

        Assert.True(second.AlreadyRunning);
        Assert.Equal(1, worklist.Reads);

        worklist.Gate!.SetResult();

        Assert.False((await first).AlreadyRunning);
    }

    [Fact]
    public async Task TheLoopSweepsOnceItsFirstWaitIsOverAndKeepsSweepingAfterThat()
    {
        HurriedClock clock = new();
        HeldWorklist worklist = new HeldWorklist().Holding(Subject(RecordingOutcome.Complete));
        using ThumbnailJob job = Job(
            worklist,
            new HeldRenderer(),
            Settings with { BeforeFirstPass = TimeSpan.FromMinutes(3), BetweenPasses = TimeSpan.FromHours(2) },
            clock);
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await Eventually.Happens(() => worklist.Reads >= 2, "the loop never passed twice");
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal([TimeSpan.FromMinutes(3), TimeSpan.FromHours(2)], clock.Waits.Take(2).ToArray());
    }

    [Fact]
    public async Task TheLoopDoesNotStartWhenThereIsNowhereToPutThem()
    {
        HeldWorklist worklist = new HeldWorklist().Holding(Subject(RecordingOutcome.Complete));
        using ThumbnailJob job = Job(
            worklist,
            new HeldRenderer(),
            Settings with { WrittenTo = null },
            new HurriedClock());
        using var stopping = new CancellationTokenSource();

        await job.StartAsync(stopping.Token);
        await job.ExecuteTask!.WaitAsync(Eventually.Patience);
        await stopping.CancelAsync();
        await job.StopAsync(Cancel);

        Assert.Equal(0, worklist.Reads);
    }

    [Fact]
    public async Task AskingForAPictureAgainDrawsItThereAndThen()
    {
        ThumbnailSubject subject = Subject(RecordingOutcome.Truncated);
        HeldWorklist worklist = new HeldWorklist().Holding(subject);
        HeldRenderer renderer = new();
        using ThumbnailJob job = Job(worklist, renderer);

        Assert.Equal(ThumbnailRemake.Drawn, await job.RemakeAsync(subject.Id, Cancel));

        Assert.Equal([subject.Id], worklist.AskedAgain);
        Assert.Single(renderer.Asked);
        Assert.Equal(ThumbnailState.Ready, Assert.Single(worklist.Written).State);
    }

    [Fact]
    public async Task AskingAgainForARecordingTheLedgerCannotOfferDrawsNothing()
    {
        HeldWorklist worklist = new();
        HeldRenderer renderer = new();
        using ThumbnailJob job = Job(worklist, renderer);

        Assert.Equal(ThumbnailRemake.NothingToAskAbout, await job.RemakeAsync(RecordingId.New(), Cancel));

        Assert.Empty(renderer.Asked);
        Assert.Empty(worklist.Written);
    }

    [Fact]
    public async Task AskingAgainForARecordingThatFailedStillDrawsNothing()
    {
        ThumbnailSubject subject = Subject(RecordingOutcome.Failed);
        HeldWorklist worklist = new HeldWorklist().Holding(subject);
        HeldRenderer renderer = new();
        using ThumbnailJob job = Job(worklist, renderer);

        Assert.Equal(ThumbnailRemake.Skipped, await job.RemakeAsync(subject.Id, Cancel));

        Assert.Empty(renderer.Asked);
        Assert.Equal(ThumbnailState.Skipped, Assert.Single(worklist.Written).State);
    }

    [Fact]
    public async Task AskingAgainForARecordingUnderARootOutOfReachSaysSoRatherThanFailingIt()
    {
        ThumbnailSubject subject = Subject(RecordingOutcome.Complete, root: Elsewhere);
        HeldWorklist worklist = new HeldWorklist().Holding(subject);
        HeldRenderer renderer = new();
        using ThumbnailJob job = Job(worklist, renderer);

        Assert.Equal(ThumbnailRemake.OutOfReach, await job.RemakeAsync(subject.Id, Cancel));

        Assert.Empty(renderer.Asked);
        Assert.Empty(worklist.Written);
    }

    [Fact]
    public async Task AskingAgainWithNowhereToPutThemSaysSoBeforeTouchingTheLedger()
    {
        HeldWorklist worklist = new();
        using ThumbnailJob job = Job(worklist, new HeldRenderer(), Settings with { WrittenTo = null });

        Assert.Equal(ThumbnailRemake.NowhereToPutThem, await job.RemakeAsync(RecordingId.New(), Cancel));

        Assert.Empty(worklist.AskedAgain);
    }

    [Fact]
    public async Task AskingAgainForARecordingThatCannotBeDrawnCarriesTheFailureBack()
    {
        ThumbnailSubject subject = Subject(RecordingOutcome.Complete);
        HeldWorklist worklist = new HeldWorklist().Holding(subject);
        using ThumbnailJob job = Job(
            worklist,
            new HeldRenderer(_ => ThumbnailRender.Failed(ThumbnailFault.ProgrammeMissing, "no ffmpeg here")));

        Assert.Equal(ThumbnailRemake.Failed, await job.RemakeAsync(subject.Id, Cancel));

        Assert.Equal(ThumbnailFault.ProgrammeMissing, Assert.Single(worklist.Written).Fault);
    }

    private static ThumbnailSettings Settings { get; } = new()
    {
        WrittenTo = "/srv/pictures",
        Programme = "ffmpeg",
    };

    private static ThumbnailSubject Subject(
        RecordingOutcome outcome,
        TimeSpan? written = null,
        OutputRoot? root = null)
    {
        RecordingId id = RecordingId.New();

        return new ThumbnailSubject(
            id,
            root ?? Bulk,
            RecordingFileName.For(id, ".m2ts"),
            outcome,
            written ?? TimeSpan.FromHours(2));
    }

    private static ThumbnailJob Job(
        HeldWorklist worklist,
        IThumbnailRenderer renderer,
        ThumbnailSettings? settings = null,
        TimeProvider? clock = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IThumbnailWorklist>(_ => worklist);

        return new ThumbnailJob(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            renderer,
            settings ?? Settings,
            new IntegritySettings { OutputRoots = [new StorageRootPath(Bulk, "/srv/bulk")] },
            clock ?? new StoppedClock(new DateTime(2026, 8, 26, 4, 30, 0, DateTimeKind.Utc)),
            NullLogger<ThumbnailJob>.Instance);
    }
}
