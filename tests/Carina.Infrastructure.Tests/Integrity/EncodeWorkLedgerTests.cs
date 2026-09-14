using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
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

    public Task InitializeAsync() => ClearAsync();

    public Task DisposeAsync() => ClearAsync();

    [Fact]
    public async Task AFileAJobHasNotStartedWritingYetIsAlreadyDeclared()
    {
        EncodeJob job = await QueuedAsync();
        EncodeScratchFile scratch = await WritingAsync(job);

        IReadOnlyList<DeclaredFile> declared = await ReadAsync();

        DeclaredFile only = Assert.Single(declared);
        Assert.Equal(Primary, only.Root);
        Assert.Equal(scratch.FileName.Value, only.Path);
    }

    [Fact]
    public async Task AFileTheJobRunningRightNowIsWritingIsDeclared()
    {
        EncodeJob job = await QueuedAsync();
        EncodeScratchFile scratch = await WritingAsync(job);
        await MoveAsync(job, running => running.Start(Started));

        Assert.Equal(scratch.FileName.Value, Assert.Single(await ReadAsync()).Path);
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

        Assert.Empty(await ReadAsync());
    }

    [Fact]
    public async Task EveryFileStillOwedARemovalComesBackInAnOrderThatDoesNotWander()
    {
        EncodeJob one = await QueuedAsync();
        EncodeJob other = await QueuedAsync();
        await WritingAsync(one);
        await WritingAsync(other);

        IReadOnlyList<DeclaredFile> declared = await ReadAsync();

        Assert.Equal(2, declared.Count);
        Assert.Equal(
            declared.Select(file => file.Path).Order(StringComparer.Ordinal).ToArray(),
            declared.Select(file => file.Path).ToArray());
    }

    private async Task<IReadOnlyList<DeclaredFile>> ReadAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return await new EncodeWorkLedger(reading).ListAsync(Cancel);
    }

    private async Task<EncodeJob> QueuedAsync()
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
            RecordingId.New(),
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
    }
}
