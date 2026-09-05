using Carina.Domain.Encodings;
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
