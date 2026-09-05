using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeDispatchTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-ED2-011: when the process comes up, every job the ledger holds as running goes back to the queue or is given up, and nothing else is touched")]
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

    [Fact(DisplayName = "BR-ED2-005: a look at an empty queue starts nothing and says so")]
    public async Task ALookAtAnEmptyQueueStartsNothing()
    {
        var held = new HeldEncodeJobs();

        EncodeLook look = await Dispatch(held, new EncodeSettings()).LookAsync(Cancel);

        Assert.Equal(EncodeClaimStanding.NothingWaiting, look.Standing);
        Assert.Null(look.Job);
        Assert.Null(look.Ended);
    }

    [Fact(DisplayName = "BR-ED2-005: while the ledger holds a running job, a look starts nothing and says another is running")]
    public async Task WhileTheLedgerHoldsARunningJobALookStartsNothing()
    {
        var held = new HeldEncodeJobs();
        held.Jobs.AddRange([Running(attempt: 1), Waiting()]);

        EncodeLook look = await Dispatch(held, new EncodeSettings()).LookAsync(Cancel);

        Assert.Equal(EncodeClaimStanding.AnotherIsRunning, look.Standing);
        Assert.Single(held.Jobs, job => job.Status is EncodeJobStatus.Running);
    }

    [Fact(DisplayName = "BR-ED2-011: a job whose run throws is put back in the queue with its attempt counted, so it never sits as running with nobody running it")]
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

    [Fact(DisplayName = "BR-ED2-011: a job whose run throws on its last attempt is given up as timed out")]
    public async Task AJobWhoseRunThrowsOnItsLastAttemptIsGivenUp()
    {
        var held = new HeldEncodeJobs();
        EncodeJob waiting = Waiting();
        held.Jobs.Add(waiting);

        EncodeLook look = await Dispatch(held, new EncodeSettings { MostAttempts = 1 }).LookAsync(Cancel);

        Assert.Equal(EncodeJobStatus.Failed, look.Ended);
        Assert.Equal(EncodeFailure.TimedOut, waiting.Failure!.Failure);
    }

    [Fact(DisplayName = "BR-ED2-012: a job called off while it ran is left as the ledger says, and what it still owes a removal for is swept")]
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
            null);

    /// <summary>
    /// A dispatch over the held ledger. The runner is built from nothing, so a claimed job's run
    /// throws at once: what these tests look at is what the dispatch does around a run, not the run.
    /// </summary>
    private static EncodeDispatch Dispatch(
        HeldEncodeJobs held,
        EncodeSettings settings,
        HeldEncodeScratch? scratch = null,
        Action<EncodeJob>? whenRun = null)
    {
        var clock = new HandTurnedClock(new DateTimeOffset(Now));
        var services = new ServiceCollection();
        services.AddScoped<IEncodeJobRepository>(_ => held);
        services.AddScoped(_ => new EncodeRestart(held, new ScriptedStrays(), settings, clock, NullLogger<EncodeRestart>.Instance));
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

        return new EncodeDispatch(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            settings,
            clock,
            NullLogger<EncodeDispatch>.Instance);
    }
}
