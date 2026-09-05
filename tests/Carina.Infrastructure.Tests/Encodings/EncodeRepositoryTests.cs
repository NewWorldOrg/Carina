using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime Defined = new(2026, 9, 5, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Queued = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AProfileAndADestinationWrittenDownAreFoundAgainAsTheyWereWritten()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();

        await using CarinaDbContext reading = database.Open();
        EncodeProfile? readProfile = await new EncodeProfileRepository(reading).FindAsync(profile.Id, Cancel);
        EncodeDestination? readDestination = await new EncodeDestinationRepository(reading).FindAsync(destination.Id, Cancel);

        Assert.NotNull(readProfile);
        Assert.Equal(new EncodeLabel("Viewing"), readProfile.Label);
        Assert.Equal(EncodeCodec.H264, readProfile.Codec);
        Assert.Equal(EncodeResolution.Hd, readProfile.Resolution);
        Assert.Equal(Deinterlace.EveryFrame, readProfile.Deinterlace);
        Assert.Equal(new ConstantRateFactor(22), readProfile.SoftwareRateControl);
        Assert.Equal(new ConstantQuantiser(25), readProfile.VaapiRateControl);
        Assert.Equal(Defined, readProfile.DefinedAt);

        Assert.NotNull(readDestination);
        Assert.Equal(Primary, readDestination.OutputRoot);
        Assert.Equal(profile.Id, readDestination.DefaultProfileId);
        Assert.Contains(await new EncodeProfileRepository(reading).ListAsync(Cancel), listed => listed.Id.Equals(profile.Id));
        Assert.Contains(await new EncodeDestinationRepository(reading).ListAsync(Cancel), listed => listed.Id.Equals(destination.Id));
    }

    [Fact(DisplayName = "BR-ED2-012: a job that failed comes back with its classification, its note and the time, and one that did not with none of them")]
    public async Task AJobComesBackAsItWasWrittenFailureAndAll()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob failed = Job(profile, destination);
        EncodeJob waiting = Job(profile, destination);
        failed.Start(Started);
        failed.Fail(EncodeFailure.SourceMissing, "the recording is not where the ledger says", Ended);

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new EncodeJobRepository(writing);
            await repository.AddAsync(failed, Cancel);
            await repository.AddAsync(waiting, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        EncodeJob? readFailed = await new EncodeJobRepository(reading).FindAsync(failed.Id, Cancel);
        EncodeJob? readWaiting = await new EncodeJobRepository(reading).FindAsync(waiting.Id, Cancel);

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

    [Fact(DisplayName = "BR-ED2-011: where a job ran, the programme it started and how far it got go into the ledger and come back with the job, and go when the job's moves let them go")]
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

        await using (CarinaDbContext writing = database.Open())
        {
            await new EncodeJobRepository(writing).AddAsync(running, Cancel);
        }

        await using (CarinaDbContext reading = database.Open())
        {
            EncodeJob? read = await new EncodeJobRepository(reading).FindAsync(running.Id, Cancel);

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
            await new EncodeJobRepository(reading).SaveAsync(read, Cancel);
        }

        await using CarinaDbContext again = database.Open();
        EncodeJob? ended = await new EncodeJobRepository(again).FindAsync(running.Id, Cancel);

        Assert.NotNull(ended);
        Assert.Null(ended.Programme);
        Assert.Equal(running.Route, ended.Route);
        Assert.NotNull(ended.Headway);
        Assert.Equal(TimeSpan.FromSeconds(2096.7947), ended.Timeline!.ArtefactLength);
        Assert.True(ended.Timeline.LengthsAgree);
    }

    [Fact(DisplayName = "BR-ED2-009: the name goes into the ledger and is read back with the job")]
    public async Task TheNameGoesIntoTheLedgerAndIsReadBackWithTheJob()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);
        job.Start(Started);

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new EncodeJobRepository(writing);
            await repository.AddAsync(job, Cancel);

            Assert.Equal(
                ArtefactClaim.Claimed,
                await repository.ClaimArtefactAsync(job, EncodeFileName.Artefact(job.RecordingId, profile.Id), Cancel));
        }

        await using CarinaDbContext reading = database.Open();
        EncodeJob? read = await new EncodeJobRepository(reading).FindAsync(job.Id, Cancel);

        Assert.NotNull(read);
        Assert.Equal(EncodeFileName.Artefact(job.RecordingId, profile.Id), read.ArtefactName);
        Assert.Equal(EncodeJobStatus.Running, read.Status);
    }

    [Fact(DisplayName = "BR-ED2-009: two jobs on one recording with one profile — the second to claim the name is refused, and left as it was")]
    public async Task TheSecondJobToClaimTheSameNameIsRefusedAndLeftAsItWas()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        var recording = RecordingId.New();
        EncodeJob first = Job(profile, destination, recording);
        EncodeJob second = Job(profile, destination, recording);
        EncodeFileName name = EncodeFileName.Artefact(recording, profile.Id);
        first.Start(Started);

        await using CarinaDbContext writing = database.Open();
        var repository = new EncodeJobRepository(writing);
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

        await using CarinaDbContext reading = database.Open();
        EncodeJob? read = await new EncodeJobRepository(reading).FindAsync(second.Id, Cancel);

        Assert.NotNull(read);
        Assert.Equal(EncodeJobStatus.Failed, read.Status);
        Assert.Equal(EncodeFailure.DestinationCollision, read.Failure!.Failure);
        Assert.Null(read.ArtefactName);
    }

    [Fact(DisplayName = "BR-ED2-009: a job claiming the name it already holds is not refused, which is what lets a later attempt recognise its own success")]
    public async Task AJobClaimingTheNameItAlreadyHoldsIsNotRefused()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);
        EncodeFileName name = EncodeFileName.Artefact(job.RecordingId, profile.Id);
        job.Start(Started);

        await using CarinaDbContext writing = database.Open();
        var repository = new EncodeJobRepository(writing);
        await repository.AddAsync(job, Cancel);

        Assert.Equal(ArtefactClaim.Claimed, await repository.ClaimArtefactAsync(job, name, Cancel));

        job.Requeue(Ended);
        await repository.SaveAsync(job, Cancel);
        job.Start(Ended.AddMinutes(1));
        await repository.SaveAsync(job, Cancel);

        Assert.Equal(ArtefactClaim.Claimed, await repository.ClaimArtefactAsync(job, name, Cancel));
        Assert.Equal(2, job.Attempt);
    }

    [Fact(DisplayName = "BR-ED2-009: a job the ledger does not hold as running cannot name an artefact")]
    public async Task AJobTheLedgerDoesNotHoldAsRunningCannotNameAnArtefact()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);

        await using CarinaDbContext writing = database.Open();
        var repository = new EncodeJobRepository(writing);
        await repository.AddAsync(job, Cancel);

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ClaimArtefactAsync(
            job, EncodeFileName.Artefact(job.RecordingId, profile.Id), Cancel));
        Assert.Null(job.ArtefactName);
    }

    [Fact(DisplayName = "BR-ES-001: the oldest waiting job is claimed by a conditional update, and comes back running with the time it started")]
    public async Task TheOldestWaitingJobIsClaimedAndComesBackRunning()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob later = Job(profile, destination);
        EncodeJob earlier = EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), profile.Id, destination.Id, Primary, Queued.AddMinutes(-5));

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new EncodeJobRepository(writing);
            await repository.AddAsync(later, Cancel);
            await repository.AddAsync(earlier, Cancel);
        }

        await using CarinaDbContext claiming = database.Open();
        EncodeClaim claim = await new EncodeJobRepository(claiming).ClaimNextAsync(Started, Cancel);

        Assert.Equal(EncodeClaimStanding.Claimed, claim.Standing);
        Assert.NotNull(claim.Job);
        Assert.Equal(earlier.Id, claim.Job.Id);
        Assert.Equal(EncodeJobStatus.Running, claim.Job.Status);
        Assert.Equal(Started, claim.Job.StartedAt);
        Assert.Equal(EncodeJob.FirstAttempt, claim.Job.Attempt);

        await using CarinaDbContext reading = database.Open();
        Assert.Equal(EncodeJobStatus.Running, (await new EncodeJobRepository(reading).FindAsync(earlier.Id, Cancel))!.Status);
        Assert.Equal(EncodeJobStatus.Queued, (await new EncodeJobRepository(reading).FindAsync(later.Id, Cancel))!.Status);
        Assert.Equal([earlier.Id], (await new EncodeJobRepository(reading).ListRunningAsync(Cancel)).Select(job => job.Id));
    }

    [Fact(DisplayName = "BR-ED2-005: while the ledger holds a running job, a claim is refused by the index and answers that another is running")]
    public async Task WhileTheLedgerHoldsARunningJobAClaimIsRefusedByTheIndex()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob running = Job(profile, destination);
        EncodeJob waiting = Job(profile, destination);
        running.Start(Started);

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new EncodeJobRepository(writing);
            await repository.AddAsync(running, Cancel);
            await repository.AddAsync(waiting, Cancel);
        }

        await using CarinaDbContext claiming = database.Open();
        EncodeClaim claim = await new EncodeJobRepository(claiming).ClaimNextAsync(Ended, Cancel);

        Assert.Equal(EncodeClaimStanding.AnotherIsRunning, claim.Standing);
        Assert.Null(claim.Job);

        await using CarinaDbContext reading = database.Open();
        Assert.Equal(EncodeJobStatus.Queued, (await new EncodeJobRepository(reading).FindAsync(waiting.Id, Cancel))!.Status);
    }

    [Fact(DisplayName = "BR-ED2-005: twenty claims at once start exactly one job; the rest are refused by the ledger, not by anything in memory")]
    public async Task TwentyClaimsAtOnceStartExactlyOneJob()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new EncodeJobRepository(writing);

            for (int queued = 0; queued < 5; queued++)
            {
                await repository.AddAsync(Job(profile, destination), Cancel);
            }
        }

        EncodeClaim[] claims = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            await using CarinaDbContext claiming = database.Open();

            return await new EncodeJobRepository(claiming).ClaimNextAsync(Started, Cancel);
        }));

        Assert.Single(claims, claim => claim.Standing is EncodeClaimStanding.Claimed);
        Assert.All(
            claims.Where(claim => claim.Standing is not EncodeClaimStanding.Claimed),
            claim => Assert.Contains(claim.Standing, new[] { EncodeClaimStanding.AnotherIsRunning, EncodeClaimStanding.TakenMeanwhile }));

        await using CarinaDbContext reading = database.Open();
        Assert.Single(await new EncodeJobRepository(reading).ListRunningAsync(Cancel));
    }

    [Fact(DisplayName = "BR-ED2-005: an empty queue answers that nothing is waiting")]
    public async Task AnEmptyQueueAnswersThatNothingIsWaiting()
    {
        await ClearAsync();

        await using CarinaDbContext claiming = database.Open();
        EncodeClaim claim = await new EncodeJobRepository(claiming).ClaimNextAsync(Started, Cancel);

        Assert.Equal(EncodeClaimStanding.NothingWaiting, claim.Standing);
        Assert.Empty(await new EncodeJobRepository(claiming).ListRunningAsync(Cancel));
    }

    [Fact(DisplayName = "BR-ED2-011: a running job put back by a restart is claimed again on its next attempt")]
    public async Task ARunningJobPutBackByARestartIsClaimedAgain()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);

        await using (CarinaDbContext first = database.Open())
        {
            var repository = new EncodeJobRepository(first);
            await repository.AddAsync(job, Cancel);

            EncodeJob claimed = (await repository.ClaimNextAsync(Started, Cancel)).Job!;
            Assert.Equal(EncodeRecovery.PutBack, claimed.Recover(3, Ended));
            await repository.SaveAsync(claimed, Cancel);
        }

        await using CarinaDbContext second = database.Open();
        EncodeClaim again = await new EncodeJobRepository(second).ClaimNextAsync(Ended.AddMinutes(1), Cancel);

        Assert.Equal(EncodeClaimStanding.Claimed, again.Standing);
        Assert.Equal(2, again.Job!.Attempt);
        Assert.Equal(Ended.AddMinutes(1), again.Job.StartedAt);
    }

    [Fact(DisplayName = "BR-ED2-010: what a job still owes a removal for is exactly what the ledger holds unsettled for that job")]
    public async Task WhatAJobStillOwesARemovalForIsWhatTheLedgerHoldsUnsettledForIt()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);
        EncodeJob other = Job(profile, destination);
        EncodeScratchFile first = Scratch(job, 1, Queued);
        EncodeScratchFile second = Scratch(job, 2, Queued.AddMinutes(1));
        EncodeScratchFile others = Scratch(other, 1, Queued);

        await using (CarinaDbContext writing = database.Open())
        {
            var jobs = new EncodeJobRepository(writing);
            await jobs.AddAsync(job, Cancel);
            await jobs.AddAsync(other, Cancel);

            var ledger = new EncodeScratchLedger(writing);
            await ledger.RecordAsync(first, Cancel);
            await ledger.RecordAsync(second, Cancel);
            await ledger.RecordAsync(others, Cancel);

            first.Settle(EncodeScratchFate.Removed, Ended);
            await ledger.SaveAsync(first, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<EncodeScratchFile> owed = await new EncodeScratchLedger(reading).ListOwedAsync(job.Id, Cancel);

        Assert.Equal([second.Id], owed.Select(scratch => scratch.Id));
        Assert.Equal(EncodeScratchKind.WorkFile, owed[0].Kind);
        Assert.Equal(second.FileName, owed[0].FileName);
        Assert.True(owed[0].IsOwedARemoval);
    }

    [Fact(DisplayName = "BR-ES-002: a page of the ledger comes newest first, narrowed to the standings asked for, and says how many there are")]
    public async Task APageOfTheLedgerComesNewestFirstNarrowedToTheStandingsAskedFor()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob oldest = Job(profile, destination);
        EncodeJob running = EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), profile.Id, destination.Id, Primary, Queued.AddMinutes(1));
        EncodeJob newest = EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), profile.Id, destination.Id, Primary, Queued.AddMinutes(5));
        running.Start(Started.AddMinutes(1));

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new EncodeJobRepository(writing);
            await repository.AddAsync(oldest, Cancel);
            await repository.AddAsync(running, Cancel);
            await repository.AddAsync(newest, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        var reader = new EncodeJobRepository(reading);
        PaginatedList<EncodeJob> everything = await reader.ListAsync(EncodeJobQuery.For(null, 1, 2)!, Cancel);
        PaginatedList<EncodeJob> waiting = await reader.ListAsync(EncodeJobQuery.For([EncodeJobStatus.Queued], 1, 10)!, Cancel);

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

        await using (CarinaDbContext writing = database.Open())
        {
            var repository = new EncodeJobRepository(writing);
            await repository.AddAsync(first, Cancel);
            await repository.AddAsync(second, Cancel);
            await repository.AddAsync(other, Cancel);
        }

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<EncodeJob> listed = await new EncodeJobRepository(reading).ListForRecordingAsync(recording, Cancel);

        Assert.Equal([first.Id, second.Id], listed.Select(job => job.Id));
    }

    [Fact(DisplayName = "BR-ED2-012: a job called off under the hand that runs it is not written over by that hand; the save says the row moved")]
    public async Task AJobCalledOffUnderTheHandThatRunsItIsNotWrittenOver()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);
        job.Start(Started);

        await using (CarinaDbContext writing = database.Open())
        {
            await new EncodeJobRepository(writing).AddAsync(job, Cancel);
        }

        await using CarinaDbContext running = database.Open();
        var runner = new EncodeJobRepository(running);
        EncodeJob held = (await runner.FindAsync(job.Id, Cancel))!;

        await using (CarinaDbContext cancelling = database.Open())
        {
            var hand = new EncodeJobRepository(cancelling);
            EncodeJob calledOff = (await hand.FindAsync(job.Id, Cancel))!;
            calledOff.Cancel(Ended);
            await hand.SaveAsync(calledOff, Cancel);
        }

        held.Fail(EncodeFailure.FfmpegExitedNonZero, "the programme exited 255", Ended.AddSeconds(1));
        EncodeJobMovedMeanwhileException moved = await Assert.ThrowsAsync<EncodeJobMovedMeanwhileException>(() => runner.SaveAsync(held, Cancel));

        Assert.Equal(job.Id, moved.JobId);
        await using CarinaDbContext reading = database.Open();
        EncodeJob? read = await new EncodeJobRepository(reading).FindAsync(job.Id, Cancel);
        Assert.Equal(EncodeJobStatus.Cancelled, read!.Status);
        Assert.Null(read.Failure);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
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

        await using CarinaDbContext writing = database.Open();
        await new EncodeProfileRepository(writing).AddAsync(profile, Cancel);
        await new EncodeDestinationRepository(writing).AddAsync(destination, Cancel);

        return (profile, destination);
    }

    private static EncodeJob Job(EncodeProfile profile, EncodeDestination destination, RecordingId? recording = null)
        => EncodeJob.Queue(EncodeJobId.New(), recording ?? RecordingId.New(), profile.Id, destination.Id, Primary, Queued);

    private static EncodeScratchFile Scratch(EncodeJob job, int attempt, DateTime at)
        => EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            job.Id,
            EncodeScratchKind.WorkFile,
            Primary,
            EncodeFileName.Working(job.RecordingId, job.Id, attempt),
            at);
}
