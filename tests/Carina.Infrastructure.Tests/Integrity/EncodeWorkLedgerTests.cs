using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Machines;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Integrity;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Integrity;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeWorkLedgerTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Defined = new(2026, 9, 5, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Queued = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly List<RecordingId> recorded = [];

    public Task InitializeAsync() => ClearAsync();

    public Task DisposeAsync() => ClearAsync();

    [Fact]
    public async Task AFileAJobHasNotStartedWritingYetIsAlreadyDeclared()
    {
        EncodeJob job = await QueuedAsync();
        EncodeScratchFile scratch = await WritingAsync(job);

        IReadOnlyList<DeclaredFile> declared = await ReadAsync();

        Assert.Contains(new DeclaredFile(Primary, scratch.FileName.Value), declared);
    }

    [Fact]
    public async Task AFileTheJobRunningRightNowIsWritingIsDeclared()
    {
        EncodeJob job = await QueuedAsync();
        EncodeScratchFile scratch = await WritingAsync(job);
        await MoveAsync(job, running => running.Start(Started));

        Assert.Contains(new DeclaredFile(Primary, scratch.FileName.Value), await ReadAsync());
    }

    [Fact]
    public async Task AFileOfAJobThatHasEndedIsNotDeclaredAnyMore()
    {
        EncodeJob job = await QueuedAsync();
        await WritingAsync(job);
        await MoveAsync(job, failing =>
        {
            failing.Start(Started);
            failing.Fail(EncodeFailure.SourceMissing, "the recording is not where the ledger says", Ended);
        });

        Assert.Empty(await ReadAsync());
    }

    [Fact]
    public async Task AFileOfAJobSomebodyCalledOffIsNotDeclaredAnyMore()
    {
        EncodeJob job = await QueuedAsync();
        await WritingAsync(job);
        await MoveAsync(job, called => called.Cancel(Ended));

        Assert.Empty(await ReadAsync());
    }

    [Fact]
    public async Task AFileAlreadyRemovedIsNotDeclaredEvenWhileItsJobRuns()
    {
        EncodeJob job = await QueuedAsync();
        EncodeScratchFile scratch = await WritingAsync(job);
        await MoveAsync(job, running => running.Start(Started));
        scratch.Settle(EncodeScratchFate.Removed, Ended);

        await using (CarinaDbContext writing = database.Open())
        {
            await new EncodeScratchLedger(writing).SaveAsync(scratch, Cancel);
        }

        Assert.DoesNotContain(new DeclaredFile(Primary, scratch.FileName.Value), await ReadAsync());
    }

    [Fact]
    public async Task EveryFileStillOwedARemovalComesBackInAnOrderThatDoesNotWander()
    {
        EncodeJob one = await QueuedAsync();
        EncodeJob other = await QueuedAsync();
        EncodeScratchFile first = await WritingAsync(one);
        EncodeScratchFile second = await WritingAsync(other);

        IReadOnlyList<DeclaredFile> declared = await ReadAsync();

        Assert.Contains(new DeclaredFile(Primary, first.FileName.Value), declared);
        Assert.Contains(new DeclaredFile(Primary, second.FileName.Value), declared);
        Assert.Equal(
            declared.Select(file => file.Path).Order(StringComparer.Ordinal).ToArray(),
            declared.Select(file => file.Path).ToArray());
    }

    [Fact]
    public async Task TheArtefactAJobInHandIsMakingIsClaimedBeforeItIsNamed()
    {
        EncodeJob job = await QueuedAsync();

        Assert.Contains(
            new DeclaredFile(Primary, EncodeFileName.Artefact(job.RecordingId, job.ProfileId).Value),
            await ReadAsync());
    }

    [Fact]
    public async Task TheArtefactThatStandsForARecordingTheLedgerHoldsIsClaimed()
    {
        Recording recording = await RecordedAsync();
        EncodeJob job = await CompletedAsync(await QueuedAsync(recording.Id));

        Assert.Equal([new DeclaredFile(Primary, job.ArtefactName!.Value)], await ReadAsync());
    }

    [Fact]
    public async Task AnArtefactWhoseRecordingIsGoneIsNotClaimed()
    {
        await CompletedAsync(await QueuedAsync());

        Assert.Empty(await ReadAsync());
    }

    [Fact]
    public async Task AnArtefactANewerOneReplacedIsNotClaimed()
    {
        Recording recording = await RecordedAsync();
        EncodeJob older = await CompletedAsync(await QueuedAsync(recording.Id));
        EncodeJob newer = await CompletedAsync(await QueuedAsync(recording.Id));
        await MoveAsync(older, replaced => replaced.Replaced(newer, Ended.AddMinutes(1)));

        Assert.Equal([new DeclaredFile(Primary, newer.ArtefactName!.Value)], await ReadAsync());
    }

    [Fact]
    public async Task TheArtefactOfAJobSomebodyCalledOffIsNotClaimed()
    {
        Recording recording = await RecordedAsync();
        await MoveAsync(await QueuedAsync(recording.Id), called => called.Cancel(Ended));

        Assert.Empty(await ReadAsync());
    }

    private async Task<IReadOnlyList<DeclaredFile>> ReadAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return await new EncodeWorkLedger(reading).ListAsync(Cancel);
    }

    private async Task<Recording> RecordedAsync()
    {
        var id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), Defined),
            Primary,
            RecordingFileName.For(id, ".ts"),
            Defined,
            Defined.AddMinutes(30),
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [],
                Defined,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Defined);

        await using CarinaDbContext writing = database.Open();
        await new RecordingRepository(writing).AddAsync(recording, Cancel);
        recorded.Add(id);

        return recording;
    }

    private async Task<EncodeJob> CompletedAsync(EncodeJob job)
    {
        await MoveAsync(job, running =>
        {
            running.Start(Started);
            running.Name(EncodeFileName.Artefact(running.RecordingId, running.ProfileId));
            running.Complete(Ended);
        });

        await using CarinaDbContext reading = database.Open();

        return (await new EncodeJobRepository(reading).FindAsync(job.Id, Cancel))!;
    }

    private async Task<EncodeJob> QueuedAsync(RecordingId? recordingId = null)
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
        EncodeJob job = EncodeJob.Queue(
            EncodeJobId.New(),
            recordingId ?? RecordingId.New(),
            profile.Id,
            destination.Id,
            Primary,
            Queued);

        await using CarinaDbContext writing = database.Open();
        await new EncodeProfileRepository(writing).AddAsync(profile, Cancel);
        await new EncodeDestinationRepository(writing).AddAsync(destination, Cancel);
        await new EncodeJobRepository(writing).AddAsync(job, Cancel);

        return job;
    }

    private async Task<EncodeScratchFile> WritingAsync(EncodeJob job)
    {
        EncodeScratchFile scratch = EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            job.Id,
            EncodeScratchKind.WorkFile,
            Primary,
            job.WorkFileName,
            Queued);

        await using CarinaDbContext writing = database.Open();
        await new EncodeScratchLedger(writing).RecordAsync(scratch, Cancel);

        return scratch;
    }

    private async Task MoveAsync(EncodeJob job, Action<EncodeJob> move)
    {
        await using CarinaDbContext writing = database.Open();
        var repository = new EncodeJobRepository(writing);
        EncodeJob held = (await repository.FindAsync(job.Id, Cancel))!;

        move(held);

        await repository.SaveAsync(held, Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<Recording>().Where(row => recorded.Contains(row.Id)).ExecuteDeleteAsync(Cancel);
        recorded.Clear();
    }
}
