using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Encodings;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeScratchLedgerTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Defined = new(2026, 9, 5, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Queued = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeFirst = new(2026, 9, 5, 3, 10, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeAgain = new(2026, 9, 5, 3, 40, 0, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();

    [Fact(DisplayName = "what a job still owes a removal for is exactly what the ledger holds unsettled for that job")]
    public async Task WhatAJobStillOwesARemovalForIsWhatTheLedgerHoldsUnsettledForIt()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);
        EncodeJob other = Job(profile, destination);
        EncodeScratchFile first = Scratch(job, 1, Queued);
        EncodeScratchFile second = Scratch(job, 2, Queued.AddMinutes(1));
        EncodeScratchFile others = Scratch(other, 1, Queued);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeJobRepository jobs = Jobs(writing);
            await jobs.AddAsync(job, Cancel);
            await jobs.AddAsync(other, Cancel);

            IEncodeScratchLedger ledger = ScratchLedger(writing);
            await ledger.RecordAsync(first, Cancel);
            await ledger.RecordAsync(second, Cancel);
            await ledger.RecordAsync(others, Cancel);

            first.Settle(EncodeScratchFate.Removed, Ended);
            await ledger.SaveAsync(first, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<EncodeScratchFile> owed = await ScratchLedger(reading).ListOwedAsync(job.Id, Cancel);

        Assert.Equal([second.Id], owed.Select(scratch => scratch.Id));
        Assert.Equal(EncodeScratchKind.WorkFile, owed[0].Kind);
        Assert.Equal(second.FileName, owed[0].FileName);
        Assert.True(owed[0].IsOwedARemoval);
    }

    [Fact]
    public async Task AFileThatCouldNotBeRemovedIsListedAsOwedAgain()
    {
        await ClearAsync();
        (EncodeProfile profile, EncodeDestination destination) = await DefinedAsync();
        EncodeJob job = Job(profile, destination);
        EncodeScratchFile stuck = Scratch(job, 1, Queued);
        EncodeScratchFile gone = Scratch(job, 2, Queued.AddMinutes(1));

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Jobs(writing).AddAsync(job, Cancel);

            IEncodeScratchLedger ledger = ScratchLedger(writing);
            await ledger.RecordAsync(stuck, Cancel);
            await ledger.RecordAsync(gone, Cancel);

            stuck.Settle(EncodeScratchFate.CouldNotBeRemoved, Ended);
            await ledger.SaveAsync(stuck, Cancel);
            gone.Settle(EncodeScratchFate.Removed, Ended);
            await ledger.SaveAsync(gone, Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReadOnlyList<EncodeScratchFile> owed = await ScratchLedger(reading).ListOwedAsync(job.Id, Cancel);

        EncodeScratchFile listed = Assert.Single(owed);
        Assert.Equal(stuck.Id, listed.Id);
        Assert.Equal(EncodeScratchFate.CouldNotBeRemoved, listed.Fate);
    }

    [Fact(DisplayName = "the ledger owes a replaced artefact's removal at a name it has owed one at before, and still holds one work file per name")]
    public async Task TheLedgerOwesARemovalAtANameItHasOwedOneAtBefore()
    {
        await ClearAsync();
        RecordingId recording = RecordingId.New();
        EncodeJob first = Completed(recording, MadeFirst);
        EncodeJob second = Completed(recording, MadeFirst.AddMinutes(10));
        await AddAsync(first, second);
        EncodeFileName shared = first.ArtefactName!;

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IEncodeScratchLedger ledger = ScratchLedger(writing);
            EncodeScratchFile earlierRemoval = Owed(first, shared, MadeAgain);
            await ledger.RecordAsync(earlierRemoval, Cancel);
            earlierRemoval.Settle(EncodeScratchFate.Removed, MadeAgain);
            await ledger.SaveAsync(earlierRemoval, Cancel);
            await ledger.RecordAsync(Owed(second, shared, MadeAgain.AddMinutes(1)), Cancel);
        }

        await using (AsyncServiceScope counting = container.CreateAsyncScope())
        {
            Assert.Equal(2, await Context(counting).Set<EncodeScratchFile>().CountAsync(Cancel));
        }

        await using AsyncServiceScope refusing = container.CreateAsyncScope();
        IEncodeScratchLedger refused = ScratchLedger(refusing);
        await refused.RecordAsync(Work(first, MadeAgain), Cancel);

        await Assert.ThrowsAsync<DbUpdateException>(() => refused.RecordAsync(Work(first, MadeAgain.AddMinutes(1)), Cancel));
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

    private static EncodeScratchFile Scratch(EncodeJob job, int attempt, DateTime at)
        => EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            job.Id,
            EncodeScratchKind.WorkFile,
            Primary,
            EncodeFileName.Working(job.RecordingId, job.Id, attempt),
            at);

    private static IEncodeDestinationRepository Destinations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeDestinationRepository>();

    private static IEncodeJobRepository Jobs(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeJobRepository>();

    private static IEncodeProfileRepository Profiles(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeProfileRepository>();

    private static IEncodeScratchLedger ScratchLedger(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IEncodeScratchLedger>();

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

    private static EncodeScratchFile Owed(EncodeJob job, EncodeFileName name, DateTime at)
        => EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            job.Id,
            EncodeScratchKind.ReplacedArtefact,
            job.OutputRoot,
            name,
            at);

    private static EncodeScratchFile Work(EncodeJob job, DateTime at)
        => EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            job.Id,
            EncodeScratchKind.WorkFile,
            job.OutputRoot,
            job.WorkFileName,
            at);

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
