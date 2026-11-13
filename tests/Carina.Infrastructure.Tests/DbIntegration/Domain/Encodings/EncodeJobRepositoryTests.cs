using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Encodings;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeJobRepositoryTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Defined = new(2026, 9, 5, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Queued = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly DateTime CaptionsMadeAt = Queued.AddHours(2);

    private static readonly DateTime ChaptersDecided = Queued.AddDays(2);

    private static readonly EncodeTimeline ChapteredTimeline = new(TimeSpan.FromSeconds(3.2), TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(1799.5));

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    private static readonly DateTime MadeFirst = new(2026, 9, 5, 3, 10, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeAgain = new(2026, 9, 5, 3, 40, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "a job that failed comes back with its classification, its note and the time, and one that did not with none of them")]
    public async Task AJobComesBackAsItWasWrittenFailureAndAll()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob failed = Job(profile, destination);
        EncodeJob waiting = Job(profile, destination);
        failed.Start(Started);
        failed.Fail(EncodeFailure.SourceMissing, "the recording is not where the ledger says", Ended);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository repository = Jobs(writing);
            await repository.AddAsync(failed, Cancel);
            await repository.AddAsync(waiting, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob? readFailed = await Jobs(reading).FindAsync(failed.Id, Cancel);
        EncodeJob? readWaiting = await Jobs(reading).FindAsync(waiting.Id, Cancel);

        Assert.NotNull(readFailed);
        Assert.Equal(EncodeJobStatus.Failed, readFailed.Status);
        Assert.Equal(Primary, readFailed.OutputRoot);
        Assert.NotNull(readFailed.Failure);
        Assert.Equal(EncodeFailure.SourceMissing, readFailed.Failure.Failure);
        Assert.Equal("the recording is not where the ledger says", readFailed.Failure.Note);
        Assert.Equal(Ended, readFailed.Failure.NoticedAt);
        Assert.Null(readFailed.ArtefactName);

        Assert.NotNull(readWaiting);
        Assert.Equal(EncodeJobStatus.Queued, readWaiting.Status);
        Assert.Null(readWaiting.Failure);
        Assert.Null(readWaiting.StartedAt);
    }

    [Fact(DisplayName = "a job that asked for the NVIDIA card and ran on it goes into the ledger and comes back routed so, with no swerve")]
    public async Task AJobRoutedToTheNvidiaCardComesBackRoutedSo()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob running = Job(profile, destination);
        running.Start(Started);
        running.Routed(new EncodeRoute(EncodeEncoder.Nvenc, EncodeEncoder.Nvenc, null));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Jobs(writing).AddAsync(running, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob? read = await Jobs(reading).FindAsync(running.Id, Cancel);

        Assert.NotNull(read);
        Assert.Equal(new EncodeRoute(EncodeEncoder.Nvenc, EncodeEncoder.Nvenc, null), read.Route);
        Assert.False(read.Route!.WasDegraded);
    }

    [Fact(DisplayName = "where a job ran, the programme it started and how far it got go into the ledger and come back with the job, and go when the job's moves let them go")]
    public async Task WhereAJobRanItsProgrammeAndItsHeadwayComeBackWithTheJob()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob running = Job(profile, destination);
        running.Start(Started);
        running.Routed(new EncodeRoute(EncodeEncoder.Vaapi, EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach));
        running.Spawned(new RunningProgramme(4242, Started.AddSeconds(1)));
        running.Reached(EncodeProgress.Of(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120), 2, false), Started.AddSeconds(20));
        running.Aligned(new EncodeTimeline(TimeSpan.FromSeconds(30499.474078), TimeSpan.FromSeconds(0.5072), TimeSpan.FromSeconds(2097.502489), null));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Jobs(writing).AddAsync(running, Cancel);
        }

        await using (AsyncServiceScope reading = container.CreateAsyncScope())
        {
            EncodeJob? read = await Jobs(reading).FindAsync(running.Id, Cancel);

            Assert.NotNull(read);
            Assert.Equal(running.Route, read.Route);
            Assert.Equal(running.Programme, read.Programme);
            Assert.Equal(0.25, read.Headway!.Portion);
            Assert.Equal(TimeSpan.FromSeconds(45), read.Headway.Left);
            Assert.Equal(Started.AddSeconds(20), read.Headway.At);
            Assert.Equal(running.Timeline, read.Timeline);
            Assert.Equal(TimeSpan.FromSeconds(30499.981278), read.Timeline!.CaptionShift);

            read.Measured(TimeSpan.FromSeconds(2096.7947));
            read.Fail(EncodeFailure.TimedOut, "quiet", Ended);
            await Jobs(reading).SaveAsync(read, Cancel);
        }

        await using AsyncServiceScope again = container.CreateAsyncScope();
        EncodeJob? ended = await Jobs(again).FindAsync(running.Id, Cancel);

        Assert.NotNull(ended);
        Assert.Null(ended.Programme);
        Assert.Equal(running.Route, ended.Route);
        Assert.NotNull(ended.Headway);
        Assert.Equal(TimeSpan.FromSeconds(2096.7947), ended.Timeline!.ArtefactLength);
        Assert.True(ended.Timeline.LengthsAgree);
    }

    [Fact(DisplayName = "why a job made its artefact again goes into the ledger and comes back with the job, and goes when the job starts over")]
    public async Task WhyAJobMadeItsArtefactAgainComesBackWithTheJob()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob running = Job(profile, destination);
        running.Start(Started);
        running.Remaking();

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Jobs(writing).AddAsync(running, Cancel);
        }

        await using (AsyncServiceScope reading = container.CreateAsyncScope())
        {
            EncodeJob read = (await Jobs(reading).FindAsync(running.Id, Cancel))!;

            Assert.Equal(EncodeRemake.LargerThanTheSource, read.Remade);

            read.MadeAgain(EncodeRemake.StillLargerThanTheSource);
            await Jobs(reading).SaveAsync(read, Cancel);
        }

        await using (AsyncServiceScope reading = container.CreateAsyncScope())
        {
            EncodeJob read = (await Jobs(reading).FindAsync(running.Id, Cancel))!;

            Assert.Equal(EncodeRemake.StillLargerThanTheSource, read.Remade);

            read.Requeue(Ended);
            await Jobs(reading).SaveAsync(read, Cancel);
        }

        await using AsyncServiceScope again = container.CreateAsyncScope();
        EncodeJob? putBack = await Jobs(again).FindAsync(running.Id, Cancel);

        Assert.Null(putBack!.Remade);
    }

    [Fact(DisplayName = "the name goes into the ledger and is read back with the job")]
    public async Task TheNameGoesIntoTheLedgerAndIsReadBackWithTheJob()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);
        job.Start(Started);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository repository = Jobs(writing);
            await repository.AddAsync(job, Cancel);

            Assert.Equal(
                ArtefactClaim.Claimed,
                await repository.ClaimArtefactAsync(job, EncodeFileName.Artefact(job.RecordingId, profile.Id), Cancel));
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob? read = await Jobs(reading).FindAsync(job.Id, Cancel);

        Assert.NotNull(read);
        Assert.Equal(EncodeFileName.Artefact(job.RecordingId, profile.Id), read.ArtefactName);
        Assert.Equal(EncodeJobStatus.Running, read.Status);
    }

    [Fact(DisplayName = "a job that failed holding the name lets go of it in the ledger, and the next job on the same recording and profile claims it")]
    public async Task AJobThatFailedHoldingTheNameLetsTheNextJobClaimIt()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        var recording = RecordingId.New();
        EncodeJob first = Job(profile, destination, recording);
        EncodeJob second = Job(profile, destination, recording);
        EncodeFileName name = EncodeFileName.Artefact(recording, profile.Id);
        first.Start(Started);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        IEncodeJobRepository repository = Jobs(writing);
        await repository.AddAsync(first, Cancel);
        await repository.AddAsync(second, Cancel);

        Assert.Equal(ArtefactClaim.Claimed, await repository.ClaimArtefactAsync(first, name, Cancel));

        first.Fail(EncodeFailure.EndingNotKept, "the ledger refused the ending", Ended);
        await repository.SaveAsync(first, Cancel);
        second.Start(Ended);
        await repository.SaveAsync(second, Cancel);

        Assert.Equal(ArtefactClaim.Claimed, await repository.ClaimArtefactAsync(second, name, Cancel));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob? read = await Jobs(reading).FindAsync(first.Id, Cancel);

        Assert.NotNull(read);
        Assert.Equal(name, read.ArtefactName);
        Assert.Equal(Ended, read.NameGivenUpAt);
    }

    [Fact(DisplayName = "two jobs on one recording with one profile — the second to claim the name is refused, and left as it was")]
    public async Task TheSecondJobToClaimTheSameNameIsRefusedAndLeftAsItWas()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        var recording = RecordingId.New();
        EncodeJob first = Job(profile, destination, recording);
        EncodeJob second = Job(profile, destination, recording);
        EncodeFileName name = EncodeFileName.Artefact(recording, profile.Id);
        first.Start(Started);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        IEncodeJobRepository repository = Jobs(writing);
        await repository.AddAsync(first, Cancel);
        await repository.AddAsync(second, Cancel);

        Assert.Equal(ArtefactClaim.Claimed, await repository.ClaimArtefactAsync(first, name, Cancel));

        first.Complete(Ended);
        await repository.SaveAsync(first, Cancel);
        second.Start(Ended);
        await repository.SaveAsync(second, Cancel);

        Assert.Equal(ArtefactClaim.TakenByAnother, await repository.ClaimArtefactAsync(second, name, Cancel));
        Assert.Null(second.ArtefactName);
        Assert.Equal(EncodeJobStatus.Running, second.Status);

        second.Fail(EncodePlacements.WhatACollisionIsCalled, "another job holds that name", Ended);
        await repository.SaveAsync(second, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob? read = await Jobs(reading).FindAsync(second.Id, Cancel);

        Assert.NotNull(read);
        Assert.Equal(EncodeJobStatus.Failed, read.Status);
        Assert.Equal(EncodeFailure.DestinationCollision, read.Failure!.Failure);
        Assert.Null(read.ArtefactName);
    }

    [Fact(DisplayName = "a job claiming the name it already holds is not refused, which is what lets a later attempt recognise its own success")]
    public async Task AJobClaimingTheNameItAlreadyHoldsIsNotRefused()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);
        EncodeFileName name = EncodeFileName.Artefact(job.RecordingId, profile.Id);
        job.Start(Started);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        IEncodeJobRepository repository = Jobs(writing);
        await repository.AddAsync(job, Cancel);

        Assert.Equal(ArtefactClaim.Claimed, await repository.ClaimArtefactAsync(job, name, Cancel));

        job.Requeue(Ended);
        await repository.SaveAsync(job, Cancel);
        job.Start(Ended.AddMinutes(1));
        await repository.SaveAsync(job, Cancel);

        Assert.Equal(ArtefactClaim.Claimed, await repository.ClaimArtefactAsync(job, name, Cancel));
        Assert.Equal(2, job.Attempt);
    }

    [Fact(DisplayName = "a job the ledger does not hold as running cannot name an artefact")]
    public async Task AJobTheLedgerDoesNotHoldAsRunningCannotNameAnArtefact()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        IEncodeJobRepository repository = Jobs(writing);
        await repository.AddAsync(job, Cancel);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ClaimArtefactAsync(
            job, EncodeFileName.Artefact(job.RecordingId, profile.Id), Cancel));
        Assert.Null(job.ArtefactName);
    }

    [Fact(DisplayName = "the oldest waiting job is claimed by a conditional update, and comes back running with the time it started")]
    public async Task TheOldestWaitingJobIsClaimedAndComesBackRunning()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob later = Job(profile, destination);
        EncodeJob earlier = EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), profile.Id, destination.Id, Primary, Queued.AddMinutes(-5));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository repository = Jobs(writing);
            await repository.AddAsync(later, Cancel);
            await repository.AddAsync(earlier, Cancel);
        }

        await using AsyncServiceScope claiming = container.CreateAsyncScope();
        EncodeClaim claim = await Jobs(claiming).ClaimNextAsync(Started, Cancel);

        Assert.Equal(EncodeClaimStanding.Claimed, claim.Standing);
        Assert.NotNull(claim.Job);
        Assert.Equal(earlier.Id, claim.Job.Id);
        Assert.Equal(EncodeJobStatus.Running, claim.Job.Status);
        Assert.Equal(Started, claim.Job.StartedAt);
        Assert.Equal(EncodeJob.FirstAttempt, claim.Job.Attempt);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Assert.Equal(EncodeJobStatus.Running, (await Jobs(reading).FindAsync(earlier.Id, Cancel))!.Status);
        Assert.Equal(EncodeJobStatus.Queued, (await Jobs(reading).FindAsync(later.Id, Cancel))!.Status);
        Assert.Equal([earlier.Id], (await Jobs(reading).ListRunningAsync(Cancel)).Select(job => job.Id));
    }

    [Fact(DisplayName = "while the ledger holds a running job, a claim is refused by the index and answers that another is running")]
    public async Task WhileTheLedgerHoldsARunningJobAClaimIsRefusedByTheIndex()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob running = Job(profile, destination);
        EncodeJob waiting = Job(profile, destination);
        running.Start(Started);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository repository = Jobs(writing);
            await repository.AddAsync(running, Cancel);
            await repository.AddAsync(waiting, Cancel);
        }

        await using AsyncServiceScope claiming = container.CreateAsyncScope();
        EncodeClaim claim = await Jobs(claiming).ClaimNextAsync(Ended, Cancel);

        Assert.Equal(EncodeClaimStanding.AnotherIsRunning, claim.Standing);
        Assert.Null(claim.Job);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Assert.Equal(EncodeJobStatus.Queued, (await Jobs(reading).FindAsync(waiting.Id, Cancel))!.Status);
    }

    [Fact(DisplayName = "twenty claims at once start exactly one job; the rest are refused by the ledger, not by anything in memory")]
    public async Task TwentyClaimsAtOnceStartExactlyOneJob()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository repository = Jobs(writing);

            for (int queued = 0; queued < 5; queued++)
            {
                await repository.AddAsync(Job(profile, destination), Cancel);
            }
        }

        EncodeClaim[] claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using AsyncServiceScope claiming = container.CreateAsyncScope();

            return await Jobs(claiming).ClaimNextAsync(Started, Cancel);
        }));

        Assert.Single(claims, claim => claim.Standing is EncodeClaimStanding.Claimed);
        Assert.All(
            claims.Where(claim => claim.Standing is not EncodeClaimStanding.Claimed),
            claim => Assert.Contains(claim.Standing, new[] { EncodeClaimStanding.AnotherIsRunning, EncodeClaimStanding.TakenMeanwhile }));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Assert.Single(await Jobs(reading).ListRunningAsync(Cancel));
    }

    [Fact(DisplayName = "an empty queue answers that nothing is waiting")]
    public async Task AnEmptyQueueAnswersThatNothingIsWaiting()
    {
        await ClearAsync();

        await using AsyncServiceScope claiming = container.CreateAsyncScope();
        EncodeClaim claim = await Jobs(claiming).ClaimNextAsync(Started, Cancel);

        Assert.Equal(EncodeClaimStanding.NothingWaiting, claim.Standing);
        Assert.Empty(await Jobs(claiming).ListRunningAsync(Cancel));
    }

    [Fact(DisplayName = "a running job put back by a restart is claimed again on its next attempt")]
    public async Task ARunningJobPutBackByARestartIsClaimedAgain()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);

        await using (AsyncServiceScope first = container.CreateAsyncScope())
        {
            IEncodeJobRepository repository = Jobs(first);
            await repository.AddAsync(job, Cancel);

            EncodeJob claimed = (await repository.ClaimNextAsync(Started, Cancel)).Job!;
            Assert.Equal(EncodeRecovery.PutBack, claimed.Recover(3, Ended));
            await repository.SaveAsync(claimed, Cancel);
        }

        await using AsyncServiceScope second = container.CreateAsyncScope();
        EncodeClaim again = await Jobs(second).ClaimNextAsync(Ended.AddMinutes(1), Cancel);

        Assert.Equal(EncodeClaimStanding.Claimed, again.Standing);
        Assert.Equal(2, again.Job!.Attempt);
        Assert.Equal(Ended.AddMinutes(1), again.Job.StartedAt);
    }

    [Fact(DisplayName = "a page of the ledger comes newest first, narrowed to the standings asked for, and says how many there are")]
    public async Task APageOfTheLedgerComesNewestFirstNarrowedToTheStandingsAskedFor()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob oldest = Job(profile, destination);
        EncodeJob running = EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), profile.Id, destination.Id, Primary, Queued.AddMinutes(1));
        EncodeJob newest = EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), profile.Id, destination.Id, Primary, Queued.AddMinutes(5));
        running.Start(Started.AddMinutes(1));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository repository = Jobs(writing);
            await repository.AddAsync(oldest, Cancel);
            await repository.AddAsync(running, Cancel);
            await repository.AddAsync(newest, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IEncodeJobRepository reader = Jobs(reading);
        PaginatedList<EncodeJob> everything = await reader.ListAsync(EncodeJobQuery.For(null, null, 1, 2)!, Cancel);
        PaginatedList<EncodeJob> waiting = await reader.ListAsync(EncodeJobQuery.For([EncodeJobStatus.Queued], null, 1, 10)!, Cancel);

        Assert.Equal(3, everything.Total);
        Assert.Equal(2, everything.LastPage);
        Assert.Equal([newest.Id, running.Id], everything.Items.Select(job => job.Id));
        Assert.Equal(2, waiting.Total);
        Assert.Equal([newest.Id, oldest.Id], waiting.Items.Select(job => job.Id));
    }

    [Fact]
    public async Task TheJobsForOneRecordingAreListedAndNobodyElses()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        var recording = RecordingId.New();
        EncodeJob first = Job(profile, destination, recording);
        EncodeJob second = EncodeJob.Queue(EncodeJobId.New(), recording, profile.Id, destination.Id, Primary, Queued.AddMinutes(1));
        EncodeJob other = Job(profile, destination);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository repository = Jobs(writing);
            await repository.AddAsync(first, Cancel);
            await repository.AddAsync(second, Cancel);
            await repository.AddAsync(other, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<EncodeJob> listed = await Jobs(reading).ListForRecordingAsync(recording, Cancel);

        Assert.Equal([first.Id, second.Id], listed.Select(job => job.Id));
    }

    [Fact(DisplayName = "a job called off under the hand that runs it is not written over by that hand; the save says the row moved")]
    public async Task AJobCalledOffUnderTheHandThatRunsItIsNotWrittenOver()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);
        job.Start(Started);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Jobs(writing).AddAsync(job, Cancel);
        }

        await using AsyncServiceScope running = container.CreateAsyncScope();
        IEncodeJobRepository runner = Jobs(running);
        EncodeJob held = (await runner.FindAsync(job.Id, Cancel))!;

        await using (AsyncServiceScope cancelling = container.CreateAsyncScope())
        {
            IEncodeJobRepository hand = Jobs(cancelling);
            EncodeJob calledOff = (await hand.FindAsync(job.Id, Cancel))!;
            calledOff.Cancel(Ended);
            await hand.SaveAsync(calledOff, Cancel);
        }

        held.Fail(EncodeFailure.FfmpegExitedNonZero, "the programme exited 255", Ended.AddSeconds(1));
        EncodeJobMovedMeanwhileException moved = await Assert.ThrowsAsync<EncodeJobMovedMeanwhileException>(() => runner.SaveAsync(held, Cancel));

        Assert.Equal(job.Id, moved.JobId);
        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob? read = await Jobs(reading).FindAsync(job.Id, Cancel);
        Assert.Equal(EncodeJobStatus.Cancelled, read!.Status);
        Assert.Null(read.Failure);
    }

    [Fact]
    public async Task AnEndingTheRunningHandCouldNotSaveIsWrittenByAHandThatNeverReadTheRow()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Jobs(writing).AddAsync(job, Cancel);
        }

        EncodeJob ran;

        await using (AsyncServiceScope running = container.CreateAsyncScope())
        {
            ran = (await Jobs(running).ClaimNextAsync(Started, Cancel)).Job!;
        }

        ran.Fail(EncodeFailure.FfmpegExitedNonZero, "the programme exited 1", Ended);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            Assert.True(await Jobs(writing).WriteTheEndingAsync(ran, Cancel));
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob? read = await Jobs(reading).FindAsync(job.Id, Cancel);
        Assert.Equal(EncodeJobStatus.Failed, read!.Status);
        Assert.Equal(EncodeFailure.FfmpegExitedNonZero, read.Failure!.Failure);
        Assert.Equal(Ended, read.EndedAt);
        Assert.Equal(EncodeClaimStanding.NothingWaiting, (await Jobs(reading).ClaimNextAsync(Ended, Cancel)).Standing);
    }

    [Fact]
    public async Task AnEndingIsNotWrittenOverARowThatMovedOnMeanwhile()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Jobs(writing).AddAsync(job, Cancel);
        }

        EncodeJob ran;

        await using (AsyncServiceScope running = container.CreateAsyncScope())
        {
            ran = (await Jobs(running).ClaimNextAsync(Started, Cancel)).Job!;
        }

        await using (AsyncServiceScope cancelling = container.CreateAsyncScope())
        {
            IEncodeJobRepository hand = Jobs(cancelling);
            EncodeJob calledOff = (await hand.FindAsync(job.Id, Cancel))!;
            calledOff.Cancel(Ended);
            await hand.SaveAsync(calledOff, Cancel);
        }

        ran.Fail(EncodeFailure.FfmpegExitedNonZero, "the programme exited 1", Ended.AddSeconds(1));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            Assert.False(await Jobs(writing).WriteTheEndingAsync(ran, Cancel));
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob? read = await Jobs(reading).FindAsync(job.Id, Cancel);
        Assert.Equal(EncodeJobStatus.Cancelled, read!.Status);
        Assert.Null(read.Failure);
    }

    [Fact(DisplayName = "the ledger answers how long the jobs that completed took, newest first, and counts nothing else")]
    public async Task TheLedgerAnswersHowLongTheJobsThatCompletedTook()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();

        await WrittenAsync(profile, destination, EncodeJobStatus.Completed, Queued, Started, Started.AddMinutes(10));
        await WrittenAsync(profile, destination, EncodeJobStatus.Completed, Queued, Started.AddHours(1), Started.AddHours(1).AddMinutes(30));
        await WrittenAsync(profile, destination, EncodeJobStatus.Failed, Queued, Started.AddHours(2), Started.AddHours(2).AddMinutes(1));
        await WrittenAsync(profile, destination, EncodeJobStatus.Cancelled, Queued, Started.AddHours(3), Started.AddHours(3).AddMinutes(1));
        await WrittenAsync(profile, destination, EncodeJobStatus.Queued, Queued, null, null);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<EncodeSpell> spells = await Jobs(reading).RecentSpellsAsync(20, Cancel);

        Assert.Equal(2, spells.Count);
        Assert.Equal(TimeSpan.FromMinutes(30), spells[0].Took);
        Assert.Equal(TimeSpan.FromMinutes(10), spells[1].Took);
        Assert.Equal(Started.AddHours(1).AddMinutes(30), spells[0].EndedAt);
    }

    [Fact(DisplayName = "only the last few are asked after")]
    public async Task OnlyTheLastFewAreAskedAfter()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();

        for (int each = 0; each < 4; each++)
        {
            await WrittenAsync(
                profile,
                destination,
                EncodeJobStatus.Completed,
                Queued,
                Started.AddHours(each),
                Started.AddHours(each).AddMinutes(5 + each));
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<EncodeSpell> spells = await Jobs(reading).RecentSpellsAsync(2, Cancel);

        Assert.Equal(2, spells.Count);
        Assert.Equal(TimeSpan.FromMinutes(8), spells[0].Took);
        Assert.Equal(TimeSpan.FromMinutes(7), spells[1].Took);
    }

    [Fact]
    public async Task BrEd2019WhatBecameOfATrackIsKeptWithTheRecordItCameFromAndTheFailuresInARow()
    {
        await ClearAsync();
        EncodeJob standing = await StandingAsync(null, null, job => Failed(job, 2));

        await using AsyncServiceScope context = container.CreateAsyncScope();
        EncodeJob read = (await Jobs(context).FindAsync(standing.Id, Cancel))!;

        Assert.Equal((EncodeCaptionTrack.Failed, CaptionsMadeAt, 2), (read.CaptionTrack!.Value, read.CaptionTrackFrom!.Value, read.CaptionTrackAttempts));

        read.Tracked(EncodeCaptionTrack.Added, CaptionsMadeAt);
        await Jobs(context).SaveAsync(read, Cancel);

        await using AsyncServiceScope again = container.CreateAsyncScope();
        EncodeJob saved = (await Jobs(again).FindAsync(standing.Id, Cancel))!;

        Assert.Equal((EncodeCaptionTrack.Added, CaptionsMadeAt, 0), (saved.CaptionTrack!.Value, saved.CaptionTrackFrom!.Value, saved.CaptionTrackAttempts));
    }

    [Fact(DisplayName = "BR-CD-010: what the chapters of a standing artefact stand for is kept with the version it came from")]
    public async Task WhatTheChaptersStandForIsKept()
    {
        await ClearAsync();
        EncodeJob standing = await StandingAsync(ChapteredTimeline, new ChapterReading(ChapterDetectorName.Ffmpeg, ChapterVerdict.Marked, 0.1, Started));

        await using (AsyncServiceScope context = container.CreateAsyncScope())
        {
            IEncodeJobRepository jobs = Jobs(context);
            EncodeJob held = (await jobs.FindAsync(standing.Id, Cancel))!;
            held.Rechaptered(new ChapterReading(ChapterDetectorName.Segments, ChapterVerdict.Marked, 0.2, ChaptersDecided, 1));
            await jobs.SaveAsync(held, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        EncodeJob read = (await Jobs(reading).FindAsync(standing.Id, Cancel))!;

        Assert.Equal(
            (ChapterDetectorName.Segments, ChapterVerdict.Marked, 0.2, ChaptersDecided, (int?)1),
            (read.Chapters!.Detector, read.Chapters.Verdict, read.Chapters.BreakShare, read.Chapters.DecidedAt, read.Chapters.ModelVersion));
    }

    [Fact(DisplayName = "the recordings made more than once are the ones two jobs completed for, whether or not the earlier one is replaced yet")]
    public async Task TheRecordingsMadeMoreThanOnceAreTheOnesTwoJobsCompletedFor()
    {
        await ClearAsync();
        RecordingId twice = RecordingId.New();
        RecordingId settled = RecordingId.New();
        RecordingId once = RecordingId.New();
        EncodeJob settledEarlier = Completed(settled, MadeFirst);
        EncodeJob settledNewer = Completed(settled, MadeAgain);
        settledEarlier.Replaced(settledNewer, MadeAgain);
        EncodeJob failed = Running(once);
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "exit 1", MadeAgain);
        await AddAsync(Completed(twice, MadeFirst), Completed(twice, MadeAgain), settledEarlier, settledNewer, Completed(once, MadeFirst), failed);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<RecordingId> made = await Jobs(reading).ListRecordingsMadeMoreThanOnceAsync(Cancel);

        Assert.Equal(
            new HashSet<RecordingId> { twice, settled },
            made.ToHashSet());
    }

    [Fact(DisplayName = "the table itself refuses a job read as replaced that did not complete")]
    public async Task TheTableRefusesAReplacedJobThatDidNotComplete()
    {
        await ClearAsync();
        EncodeJob running = Running(RecordingId.New());
        await AddAsync(running);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => Context(writing).Database.ExecuteSqlAsync(
                $"UPDATE encode_job SET replaced_at = {MadeAgain} WHERE id = {running.Id.Value}",
                Cancel));

        Assert.Equal("ck_encode_job_replaced", refused.ConstraintName);
    }

    private async Task WrittenAsync(
        EncodeProfile profile,
        EncodeDestination destination,
        EncodeJobStatus status,
        DateTime queuedAt,
        DateTime? startedAt,
        DateTime? endedAt)
    {
        var recordingId = RecordingId.New();
        EncodeJob job = EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recordingId,
            profile.Id,
            destination.Id,
            destination.OutputRoot,
            status,
            EncodeJob.FirstAttempt,
            queuedAt,
            startedAt,
            endedAt,
            status is EncodeJobStatus.Failed
                ? new EncodeFailureDetail(EncodeFailure.FfmpegExitedNonZero, "it stopped", endedAt!.Value)
                : null,
            status is EncodeJobStatus.Completed ? EncodeFileName.Artefact(recordingId, profile.Id) : null,
            null,
            null,
            null,
            null,
            null);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Jobs(writing).AddAsync(job, Cancel);
    }

    private async Task<EncodeJob> StandingAsync(EncodeTimeline? timeline, ChapterReading? chapters, Action<EncodeJob>? tracked = null)
    {
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        RecordingId recording = RecordingId.New();
        EncodeJob job = EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            profile.Id,
            destination.Id,
            destination.OutputRoot,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            Queued,
            Started,
            Started.AddMinutes(30),
            null,
            EncodeFileName.Artefact(recording, profile.Id),
            null,
            null,
            null,
            timeline,
            chapters);

        tracked?.Invoke(job);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Jobs(writing).AddAsync(job, Cancel);

        return job;
    }

    private static void Failed(EncodeJob job, int times)
    {
        for (int time = 0; time < times; time++)
        {
            job.Tracked(EncodeCaptionTrack.Failed, CaptionsMadeAt);
        }
    }

    private async Task ClearAsync()
    {
        await using AsyncServiceScope clearing = container.CreateAsyncScope();
        await Context(clearing).Set<EncodeChapter>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await Context(clearing).Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
    }

    private async Task<(EncodeProfile, EncodeDestination)> DefinedAsync()
    {
        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Viewing"),
            EncodeCodec.H264,
            EncodeResolution.Hd,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(25),
            Defined);
        EncodeDestination destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Primary"),
            Primary,
            profile.Id,
            Defined);

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        await Profiles(writing).AddAsync(profile, Cancel);
        await Destinations(writing).AddAsync(destination, Cancel);

        return (profile, destination);
    }

    private static EncodeJob Job(EncodeProfile profile, EncodeDestination destination, RecordingId? recording = null)
        => EncodeJob.Queue(EncodeJobId.New(), recording ?? RecordingId.New(), profile.Id, destination.Id, Primary, Queued);

    private static IEncodeDestinationRepository Destinations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeDestinationRepository>();

    private static IEncodeJobRepository Jobs(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeJobRepository>();

    private static IEncodeProfileRepository Profiles(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeProfileRepository>();

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();

    private static EncodeJob Running(RecordingId recording)
    {
        EncodeJob job = EncodeJob.Queue(
            EncodeJobId.New(),
            recording,
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            EncodeHarness.Encodes,
            EncodeHarness.Queued);
        job.Start(EncodeHarness.Started);

        return job;
    }

    private static EncodeJob Completed(RecordingId recording, DateTime ended)
    {
        EncodeJob job = Running(recording);
        job.Name(EncodeFileName.Artefact(recording, job.ProfileId));
        job.Complete(ended);

        return job;
    }

    private async Task AddAsync(params EncodeJob[] jobs)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();
        IEncodeProfileRepository profiles = Profiles(writing);
        IEncodeDestinationRepository destinations = Destinations(writing);
        IEncodeJobRepository repository = Jobs(writing);

        foreach (EncodeJob job in jobs)
        {
            await profiles.AddAsync(
                EncodeProfile.Define(
                    job.ProfileId,
                    new EncodeLabel("Viewing"),
                    EncodeCodec.H264,
                    EncodeResolution.AsSource,
                    Deinterlace.EveryFrame,
                    new ConstantRateFactor(22),
                    new ConstantQuantiser(24),
                    EncodeHarness.Queued),
                Cancel);
            await destinations.AddAsync(
                EncodeDestination.Define(
                    job.DestinationId,
                    new EncodeLabel("Shelf"),
                    job.OutputRoot,
                    job.ProfileId,
                    EncodeHarness.Queued),
                Cancel);
            await repository.AddAsync(job, Cancel);
        }
    }
}
