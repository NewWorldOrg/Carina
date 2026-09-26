using Carina.Contracts;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Streaming;
using Carina.Infrastructure.Tests.Integrity;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeDispatchTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly MachineCapabilities WithACard = MachineCapabilities.Of(
        CardStanding.Usable,
        [Faculty.EncodeH264OnTheCard, Faculty.EncodeH264OnTheProcessor],
        string.Empty);

    private static readonly EncodeSettings OnTheCard = new() { Prefer = EncodeEncoder.Vaapi, MostAttempts = 3 };

    [Fact(DisplayName = "when the process comes up, every job the ledger holds as running goes back to the queue or is given up, and nothing else is touched")]
    public async Task WhenTheProcessComesUpEveryRunningJobIsPutBackOrGivenUp()
    {
        var held = new HeldEncodeJobs();
        EncodeJob firstTime = Running(attempt: 1);
        EncodeJob lastTime = Running(attempt: 3);
        EncodeJob waiting = Waiting();
        held.Jobs.AddRange([firstTime, lastTime, waiting]);
        var settings = new EncodeSettings { MostAttempts = 3 };

        EncodeRestartReport report = await Dispatch(held, settings).RecoverAsync(Cancel);

        Assert.Equal(1, report.PutBack);
        Assert.Equal(1, report.GivenUp);
        Assert.Equal(EncodeJobStatus.Queued, firstTime.Status);
        Assert.Equal(2, firstTime.Attempt);
        Assert.Equal(Now, firstTime.QueuedAt);
        Assert.Equal(EncodeJobStatus.Failed, lastTime.Status);
        Assert.Equal(EncodeFailure.TimedOut, lastTime.Failure!.Failure);
        Assert.Equal(EncodeJobStatus.Queued, waiting.Status);
        Assert.Equal(EncodeJob.FirstAttempt, waiting.Attempt);
        Assert.Equal(
            [$"saved {firstTime.Id.Wire} Queued", $"saved {lastTime.Id.Wire} Failed"],
            held.Moves);
    }

    [Fact(DisplayName = "a look at an empty queue starts nothing and says so")]
    public async Task ALookAtAnEmptyQueueStartsNothing()
    {
        var held = new HeldEncodeJobs();

        EncodeLook look = await Dispatch(held, new EncodeSettings()).LookAsync(Cancel);

        Assert.Equal(EncodeClaimStanding.NothingWaiting, look.Standing);
        Assert.Null(look.Job);
        Assert.Null(look.Ended);
    }

    [Fact(DisplayName = "while the ledger holds a running job, a look starts nothing and says another is running")]
    public async Task WhileTheLedgerHoldsARunningJobALookStartsNothing()
    {
        var held = new HeldEncodeJobs();
        held.Jobs.AddRange([Running(attempt: 1), Waiting()]);

        EncodeLook look = await Dispatch(held, new EncodeSettings()).LookAsync(Cancel);

        Assert.Equal(EncodeClaimStanding.AnotherIsRunning, look.Standing);
        Assert.Single(held.Jobs, job => job.Status is EncodeJobStatus.Running);
    }

    [Fact(DisplayName = "a job whose run throws is put back in the queue with its attempt counted, so it never sits as running with nobody running it")]
    public async Task AJobWhoseRunThrowsIsPutBackInTheQueue()
    {
        var held = new HeldEncodeJobs();
        EncodeJob waiting = Waiting();
        held.Jobs.Add(waiting);

        EncodeLook look = await Dispatch(held, new EncodeSettings { MostAttempts = 3 }).LookAsync(Cancel);

        Assert.Equal(EncodeClaimStanding.Claimed, look.Standing);
        Assert.Equal(waiting.Id, look.Job);
        Assert.Null(look.Ended);
        Assert.Equal(EncodeJobStatus.Queued, waiting.Status);
        Assert.Equal(2, waiting.Attempt);
    }

    [Fact(DisplayName = "a job whose run throws on its last attempt is given up as timed out")]
    public async Task AJobWhoseRunThrowsOnItsLastAttemptIsGivenUp()
    {
        var held = new HeldEncodeJobs();
        EncodeJob waiting = Waiting();
        held.Jobs.Add(waiting);

        EncodeLook look = await Dispatch(held, new EncodeSettings { MostAttempts = 1 }).LookAsync(Cancel);

        Assert.Equal(EncodeJobStatus.Failed, look.Ended);
        Assert.Equal(EncodeFailure.TimedOut, waiting.Failure!.Failure);
    }

    [Fact]
    public async Task AJobGivenUpAfterItsRunThrewHasWhatItStillOwesARemovalForSwept()
    {
        using TempTree shelf = new();
        HeldEncodeJobs held = new();
        EncodeJob waiting = Waiting();
        held.Jobs.Add(waiting);
        HeldEncodeScratch scratch = new();
        EncodeFileName work = EncodeFileName.Working(waiting.RecordingId, waiting.Id, 1);
        File.WriteAllText(shelf.Under(work.Value), "half a picture");
        EncodeScratchFile owed = EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            waiting.Id,
            EncodeScratchKind.WorkFile,
            EncodeHarness.Primary,
            work,
            Now);
        scratch.Files.Add(owed);

        EncodeLook look = await Dispatch(
                held,
                new EncodeSettings { MostAttempts = 1, OutputRoots = [new StorageRootPath(EncodeHarness.Primary, shelf.Root)] },
                scratch)
            .LookAsync(Cancel);

        Assert.Equal(EncodeJobStatus.Failed, look.Ended);
        Assert.False(File.Exists(shelf.Under(work.Value)), "the work file of a job given up was left on the disk");
        Assert.Equal(EncodeScratchFate.Removed, owed.Fate);
    }

    [Fact]
    public async Task AJobPutBackAfterItsRunThrewKeepsItsWorkFileOwedUntilItEnds()
    {
        using TempTree shelf = new();
        HeldEncodeJobs held = new();
        EncodeJob waiting = Waiting();
        held.Jobs.Add(waiting);
        HeldEncodeScratch scratch = new();
        EncodeFileName work = EncodeFileName.Working(waiting.RecordingId, waiting.Id, 1);
        File.WriteAllText(shelf.Under(work.Value), "half a picture");
        EncodeScratchFile owed = EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            waiting.Id,
            EncodeScratchKind.WorkFile,
            EncodeHarness.Primary,
            work,
            Now);
        scratch.Files.Add(owed);

        EncodeLook look = await Dispatch(
                held,
                new EncodeSettings { MostAttempts = 3, OutputRoots = [new StorageRootPath(EncodeHarness.Primary, shelf.Root)] },
                scratch)
            .LookAsync(Cancel);

        Assert.Null(look.Ended);
        Assert.True(File.Exists(shelf.Under(work.Value)));
        Assert.True(owed.IsOwedARemoval);
    }

    [Fact(DisplayName = "a job called off while it ran is left as the ledger says, and what it still owes a removal for is swept")]
    public async Task AJobCalledOffWhileItRanIsLeftAsTheLedgerSays()
    {
        var held = new HeldEncodeJobs();
        EncodeJob waiting = Waiting();
        held.Jobs.Add(waiting);
        var scratch = new HeldEncodeScratch();
        EncodeScratchFile owed = EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            waiting.Id,
            EncodeScratchKind.WorkFile,
            EncodeHarness.Primary,
            EncodeFileName.Working(waiting.RecordingId, waiting.Id, 1),
            Now);
        scratch.Files.Add(owed);

        EncodeLook look = await Dispatch(
                held,
                new EncodeSettings { MostAttempts = 3, OutputRoots = [new StorageRootPath(EncodeHarness.Primary, Path.GetTempPath())] },
                scratch,
                whenRun: claimed =>
                {
                    claimed.Cancel(Now);

                    throw new EncodeJobMovedMeanwhileException(claimed.Id);
                })
            .LookAsync(Cancel);

        Assert.Equal(EncodeClaimStanding.Claimed, look.Standing);
        Assert.Equal(waiting.Id, look.Job);
        Assert.Equal(EncodeJobStatus.Cancelled, look.Ended);
        Assert.Equal(EncodeJobStatus.Cancelled, waiting.Status);
        Assert.Equal(1, waiting.Attempt);
        Assert.Equal(EncodeScratchFate.AlreadyGone, owed.Fate);
        Assert.DoesNotContain(held.Moves, move => move.StartsWith("saved", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AJobThatEndedAndThenThrewHasItsEndingWrittenRatherThanLeftRunningInTheLedger()
    {
        var held = new HeldEncodeJobs();
        EncodeJob waiting = Waiting();
        held.Jobs.Add(waiting);
        var scratch = new HeldEncodeScratch();
        EncodeScratchFile owed = EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            waiting.Id,
            EncodeScratchKind.WorkFile,
            EncodeHarness.Primary,
            EncodeFileName.Working(waiting.RecordingId, waiting.Id, 1),
            Now);
        scratch.Files.Add(owed);

        EncodeLook look = await Dispatch(
                held,
                new EncodeSettings { MostAttempts = 3, OutputRoots = [new StorageRootPath(EncodeHarness.Primary, Path.GetTempPath())] },
                scratch,
                whenRun: claimed => claimed.Fail(EncodeFailure.FfmpegExitedNonZero, "the programme exited 1", Now))
            .LookAsync(Cancel);

        Assert.Equal(EncodeJobStatus.Failed, look.Ended);
        Assert.Equal(1, waiting.Attempt);
        Assert.Contains($"wrote the ending {waiting.Id.Wire} Failed", held.Moves);
        Assert.False(owed.IsOwedARemoval, "the work file of a job whose ending was written is still owed a removal");
    }

    [Fact]
    public async Task AnEndingTheLedgerCouldNotTakeIsWrittenAtTheNextLookBeforeAnotherJobIsClaimed()
    {
        var held = new HeldEncodeJobs();
        EncodeJob first = Waiting();
        held.Jobs.Add(first);
        bool ledgerIsAway = true;
        held.WhenWritingTheEnding = _ => ledgerIsAway ? throw new TimeoutException("the ledger did not answer") : true;
        EncodeDispatch dispatch = Dispatch(
            held,
            new EncodeSettings { MostAttempts = 3 },
            whenRun: claimed =>
            {
                if (claimed.Id.Equals(first.Id))
                {
                    claimed.Name(EncodeFileName.Artefact(claimed.RecordingId, claimed.ProfileId));
                    claimed.Complete(Now);
                }
            });

        await Assert.ThrowsAsync<TimeoutException>(() => dispatch.LookAsync(Cancel));
        ledgerIsAway = false;
        held.Jobs.Add(Waiting());
        await dispatch.LookAsync(Cancel);

        int written = held.Moves.IndexOf($"wrote the ending {first.Id.Wire} Completed");
        int claimedNext = held.Moves.FindLastIndex(move => move.StartsWith("claimed", StringComparison.Ordinal));
        Assert.True(written >= 0, "the ending of the job that completed was never written");
        Assert.True(written < claimedNext, "another job was claimed before the ending of the last one was written");
    }

    [Fact]
    public async Task AnEndingTheLedgerNeverTakesIsGivenUpAfterAFewLooksAndTheNextJobIsClaimed()
    {
        var held = new HeldEncodeJobs();
        EncodeJob first = Waiting();
        held.Jobs.Add(first);
        held.WhenWritingTheEnding = _ => throw new InvalidOperationException("the row breaks a constraint");
        EncodeDispatch dispatch = Dispatch(
            held,
            new EncodeSettings { MostAttempts = 3 },
            whenRun: claimed =>
            {
                if (claimed.Id.Equals(first.Id))
                {
                    claimed.Fail(EncodeFailure.FfmpegExitedNonZero, "the programme exited 1", Now);
                }
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() => dispatch.LookAsync(Cancel));
        EncodeJob next = Waiting();
        held.Jobs.Add(next);
        EncodeLook? claimedNext = null;

        for (int look = 1; look < EncodeDispatch.MostTriesAtAnEnding && claimedNext is null; look++)
        {
            try
            {
                EncodeLook answered = await dispatch.LookAsync(Cancel);
                claimedNext = answered.Standing is EncodeClaimStanding.Claimed ? answered : null;
            }
            catch (InvalidOperationException)
            {
            }
        }

        Assert.NotNull(claimedNext);
        Assert.Equal(next.Id, claimedNext.Job);
    }

    [Fact(DisplayName = "While the card is making a picture for someone watching, a look bound for the card asks the ledger nothing and says why")]
    public async Task WhileSomeoneIsWatchingALookBoundForTheCardAsksTheLedgerNothing()
    {
        var held = new HeldEncodeJobs();
        EncodeJob waiting = Waiting();
        held.Jobs.Add(waiting);

        EncodeLook look = await Dispatch(held, OnTheCard, viewers: new Watching(1).Budget).LookAsync(Cancel);

        Assert.Equal(EncodeClaimStanding.AViewerHoldsTheCard, look.Standing);
        Assert.Null(look.Job);
        Assert.Null(look.Ended);
        Assert.Equal(EncodeJobStatus.Queued, waiting.Status);
        Assert.Equal(EncodeJob.FirstAttempt, waiting.Attempt);
        Assert.Empty(held.Moves);
    }

    [Fact(DisplayName = "A look bound for the card takes the job as soon as nobody is watching any more")]
    public async Task ALookBoundForTheCardTakesTheJobOnceNobodyIsWatching()
    {
        var held = new HeldEncodeJobs();
        held.Jobs.Add(Waiting());
        var viewers = new Watching(1);
        EncodeDispatch dispatch = Dispatch(held, OnTheCard, viewers: viewers.Budget);

        EncodeLook waited = await dispatch.LookAsync(Cancel);
        viewers.AllLeave();
        EncodeLook took = await dispatch.LookAsync(Cancel);

        Assert.Equal(EncodeClaimStanding.AViewerHoldsTheCard, waited.Standing);
        Assert.Equal(EncodeClaimStanding.Claimed, took.Standing);
    }

    [Fact(DisplayName = "A look bound for the processor starts a job while someone is watching, because it takes no card")]
    public async Task ALookBoundForTheProcessorStartsAJobWhileSomeoneIsWatching()
    {
        var held = new HeldEncodeJobs();
        held.Jobs.Add(Waiting());

        EncodeLook look = await Dispatch(held, new EncodeSettings { MostAttempts = 3 }, viewers: new Watching(2).Budget).LookAsync(Cancel);

        Assert.Equal(EncodeClaimStanding.Claimed, look.Standing);
    }

    [Fact(DisplayName = "A look bound for a card this machine cannot use starts a job while someone is watching, because it will swerve to the processor")]
    public async Task ALookBoundForACardThisMachineCannotUseStartsAJob()
    {
        var held = new HeldEncodeJobs();
        held.Jobs.Add(Waiting());

        EncodeLook look = await Dispatch(held, OnTheCard, viewers: new Watching(1).Budget, cardIsUsable: false).LookAsync(Cancel);

        Assert.Equal(EncodeClaimStanding.Claimed, look.Standing);
    }

    [Fact]
    public async Task ALookThatGaveWayToSomeoneWatchingTellsTheScreensNothing()
    {
        var held = new HeldEncodeJobs();
        held.Jobs.Add(Waiting());
        var events = new SilentEvents();

        await Dispatch(held, OnTheCard, viewers: new Watching(1).Budget, events: events).LookAsync(Cancel);

        Assert.Empty(events.Signalled);
    }

    private sealed class Watching
    {
        private readonly List<ITranscodeSeat> seats = [];

        public Watching(int viewers)
        {
            for (int seated = 0; seated < viewers; seated++)
            {
                seats.Add(Budget.Claim(TranscodePurpose.Live).Seat!);
            }
        }

        public TranscodeBudget Budget { get; } = new(new TranscodeBudgetSettings { AtOnce = 4 });

        public void AllLeave()
        {
            foreach (ITranscodeSeat seat in seats)
            {
                seat.Dispose();
            }

            seats.Clear();
        }
    }

    private static EncodeJob Waiting()
        => EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), EncodeHarness.Primary, EncodeHarness.Queued);

    private static EncodeJob Running(int attempt)
        => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            EncodeHarness.Primary,
            EncodeJobStatus.Running,
            attempt,
            EncodeHarness.Queued,
            EncodeHarness.Started,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

    /// <summary>
    /// A dispatch over the held ledger. The runner is built from nothing, so a claimed job's run
    /// throws at once: what these tests look at is what the dispatch does around a run, not the run.
    /// </summary>
    [Fact]
    public async Task ALookThatStartedAJobTellsTheScreensTheJobsMoved()
    {
        var held = new HeldEncodeJobs();
        held.Jobs.Add(Waiting());
        var events = new SilentEvents();

        await Dispatch(held, new EncodeSettings { MostAttempts = 3 }, events: events).LookAsync(Cancel);

        Assert.Equal([AppEventName.EncodeJobs], events.Signalled);
    }

    [Fact]
    public async Task ALookAtAnEmptyQueueTellsTheScreensNothing()
    {
        var held = new HeldEncodeJobs();
        var events = new SilentEvents();
        EncodeDispatch dispatch = Dispatch(held, new EncodeSettings(), events: events);

        await dispatch.LookAsync(Cancel);
        await dispatch.LookAsync(Cancel);
        await dispatch.LookAsync(Cancel);

        Assert.Empty(events.Signalled);
    }

    [Fact]
    public async Task AStartUpThatPutNoJobBackTellsTheScreensNothing()
    {
        var held = new HeldEncodeJobs();
        held.Jobs.Add(Waiting());
        var events = new SilentEvents();

        await Dispatch(held, new EncodeSettings(), events: events).RecoverAsync(Cancel);

        Assert.Empty(events.Signalled);
    }

    [Fact]
    public async Task AStartUpThatPutAJobBackTellsTheScreensTheJobsMoved()
    {
        var held = new HeldEncodeJobs();
        held.Jobs.Add(Running(attempt: 1));
        var events = new SilentEvents();

        await Dispatch(held, new EncodeSettings { MostAttempts = 3 }, events: events).RecoverAsync(Cancel);

        Assert.Equal([AppEventName.EncodeJobs], events.Signalled);
    }

    private static EncodeDispatch Dispatch(
        HeldEncodeJobs held,
        EncodeSettings settings,
        HeldEncodeScratch? scratch = null,
        Action<EncodeJob>? whenRun = null,
        SilentEvents? events = null,
        ITranscodeBudget? viewers = null,
        bool cardIsUsable = true)
    {
        var clock = new HandTurnedClock(new DateTimeOffset(Now));
        var services = new ServiceCollection();
        services.AddScoped<IEncodeJobRepository>(_ => held);
        services.AddScoped(provider => new EncodeRestart(
            held,
            new ScriptedStrays(),
            provider.GetRequiredService<EncodeScratchCleaner>(),
            settings,
            clock,
            NullLogger<EncodeRestart>.Instance));
        services.AddScoped(_ => new EncodeScratchCleaner(
            scratch ?? new HeldEncodeScratch(),
            new EncodePlaces(new IntegritySettings(), settings),
            clock,
            NullLogger<EncodeScratchCleaner>.Instance));
        services.AddScoped<EncodeJobRunner>(_ =>
        {
            whenRun?.Invoke(held.Jobs.Single(job => job.Status is EncodeJobStatus.Running));

            throw new InvalidOperationException("this run cannot be built");
        });

        var machine = new AskedMachine();

        if (cardIsUsable)
        {
            machine.Can = WithACard;
        }

        return new EncodeDispatch(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            settings,
            new EncodeQueueTurn(settings, viewers ?? new TranscodeBudget(new TranscodeBudgetSettings()), machine),
            events ?? new SilentEvents(),
            clock,
            NullLogger<EncodeDispatch>.Instance);
    }
}
