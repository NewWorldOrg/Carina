using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Integrity;

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
    public async Task BrEd2019WhatIsWrittenToPutATextTrackIntoAnArtefactAlreadyMadeIsDeclaredUntilItIsRemoved()
    {
        Recording recording = await RecordedAsync();
        EncodeJob job = await CompletedAsync(await QueuedAsync(recording.Id));
        EncodeScratchFile captions = await WritingAsync(job, EncodeScratchKind.Captions, job.CaptionTrackFileName);
        EncodeScratchFile captioned = await WritingAsync(job, EncodeScratchKind.CaptionedWork, job.CaptionedFileName);

        IReadOnlyList<DeclaredFile> declared = await ReadAsync();

        Assert.Contains(new DeclaredFile(Primary, captions.FileName.Value), declared);
        Assert.Contains(new DeclaredFile(Primary, captioned.FileName.Value), declared);

        await using (CarinaDbContext settling = database.Open())
        {
            EncodeScratchLedger ledger = new(settling);

            foreach (EncodeScratchFile owed in await ledger.ListOwedAsync(job.Id, Cancel))
            {
                owed.Settle(EncodeScratchFate.Removed, Ended);
                await ledger.SaveAsync(owed, Cancel);
            }
        }

        Assert.Equal([new DeclaredFile(Primary, job.ArtefactName!.Value)], await ReadAsync());
    }

    [Fact(DisplayName = "BR-CD-010: what is written to write the chapters of an artefact already made again is declared until it is removed")]
    public async Task WhatIsWrittenToWriteTheChaptersOfAnArtefactAgainIsDeclaredUntilItIsRemoved()
    {
        Recording recording = await RecordedAsync();
        EncodeJob job = await CompletedAsync(await QueuedAsync(recording.Id));
        EncodeScratchFileId tried = EncodeScratchFileId.New();
        EncodeScratchFile chapters = await WritingAsync(job, EncodeScratchKind.Chapters, EncodeFileName.Chapters(job.RecordingId, job.Id, job.Attempt, tried));
        EncodeScratchFile chaptered = await WritingAsync(job, EncodeScratchKind.ChapteredWork, EncodeFileName.Chaptered(job.RecordingId, job.Id, job.Attempt, tried));

        IReadOnlyList<DeclaredFile> declared = await ReadAsync();

        Assert.Contains(new DeclaredFile(Primary, chapters.FileName.Value), declared);
        Assert.Contains(new DeclaredFile(Primary, chaptered.FileName.Value), declared);

        await using (CarinaDbContext settling = database.Open())
        {
            EncodeScratchLedger ledger = new(settling);

            foreach (EncodeScratchFile owed in await ledger.ListOwedAsync(job.Id, Cancel))
            {
                owed.Settle(owed.Kind is EncodeScratchKind.ChapteredWork ? EncodeScratchFate.BecameTheArtefact : EncodeScratchFate.Removed, Ended);
                await ledger.SaveAsync(owed, Cancel);
            }
        }

        Assert.Equal([new DeclaredFile(Primary, job.ArtefactName!.Value)], await ReadAsync());
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

    [Fact(DisplayName = "BR-KD-025: the files the store says encode work claims are the ones each job and each scratch file says are claimed")]
    public async Task TheFilesTheStoreDeclaresAreTheOnesEachJobAndScratchFileSaysAreClaimed()
    {
        Recording held = await RecordedAsync();
        EncodeJob running = await QueuedAsync();
        await MoveAsync(running, started => started.Start(Started));
        EncodeJob failed = await QueuedAsync();
        await MoveAsync(failed, failing =>
        {
            failing.Start(Started);
            failing.Fail(EncodeFailure.SourceMissing, "the recording is not where the ledger says", Ended);
        });
        EncodeJob cancelled = await QueuedAsync(held.Id);
        await MoveAsync(cancelled, called => called.Cancel(Ended));
        EncodeJob older = await CompletedAsync(await QueuedAsync(held.Id));
        EncodeJob newer = await CompletedAsync(await QueuedAsync(held.Id));
        await MoveAsync(older, replaced => replaced.Replaced(newer, Ended.AddMinutes(1)));
        EncodeJob orphaned = await CompletedAsync(await QueuedAsync());
        EncodeJob[] jobs = [await QueuedAsync(), running, failed, cancelled, older, newer, orphaned];

        foreach (EncodeJob job in jobs)
        {
            foreach (EncodeScratchKind kind in Enum.GetValues<EncodeScratchKind>())
            {
                await WritingAsync(job, kind);
            }

            foreach (EncodeScratchFate fate in new[] { EncodeScratchFate.Removed, EncodeScratchFate.CouldNotBeRemoved })
            {
                await SettledAsync(await WritingAsync(job, EncodeScratchKind.Captions), fate);
            }
        }

        await using CarinaDbContext reading = database.Open();
        Dictionary<EncodeJobId, EncodeJob> byId = await reading.Set<EncodeJob>().AsNoTracking().ToDictionaryAsync(job => job.Id, Cancel);
        List<EncodeScratchFile> scratch = await reading.Set<EncodeScratchFile>().AsNoTracking().ToListAsync(Cancel);
        HashSet<RecordingId> recordings = [.. await reading.Set<Recording>().AsNoTracking().Select(recording => recording.Id).ToListAsync(Cancel)];

        IEnumerable<DeclaredFile> claimedScratch = scratch
            .Where(file => file.IsClaimedWhile(byId[file.JobId]))
            .Select(file => new DeclaredFile(file.OutputRoot, file.FileName.Value));
        IEnumerable<DeclaredFile> claimedArtefacts = byId.Values
            .Where(job => job.ClaimsItsArtefact(recordings.Contains(job.RecordingId)))
            .Select(job => new DeclaredFile(job.OutputRoot, job.ArtefactItMakes.Value));
        DeclaredFile[] expected = [.. claimedScratch.Concat(claimedArtefacts).Distinct().OrderBy(file => file.Path, StringComparer.Ordinal)];

        Assert.Contains(scratch, file => !file.IsClaimedWhile(byId[file.JobId]));
        Assert.Contains(byId.Values, job => !job.ClaimsItsArtefact(recordings.Contains(job.RecordingId)));
        Assert.Equal(expected, await ReadAsync());
    }

    private async Task<EncodeScratchFile> WritingAsync(EncodeJob job, EncodeScratchKind kind)
    {
        EncodeScratchFileId id = EncodeScratchFileId.New();
        EncodeScratchFile scratch = EncodeScratchFile.Record(
            id,
            job.Id,
            kind,
            Primary,
            EncodeFileName.Chaptered(job.RecordingId, job.Id, job.Attempt, id),
            Queued);

        await using CarinaDbContext writing = database.Open();
        await new EncodeScratchLedger(writing).RecordAsync(scratch, Cancel);

        return scratch;
    }

    private async Task SettledAsync(EncodeScratchFile scratch, EncodeScratchFate fate)
    {
        scratch.Settle(fate, Ended);

        await using CarinaDbContext writing = database.Open();
        await new EncodeScratchLedger(writing).SaveAsync(scratch, Cancel);
    }

    private async Task<IReadOnlyList<DeclaredFile>> ReadAsync()
    {
        await using ServiceProvider container = database.Infrastructure();
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<IEncodeWorkLedger>().ListAsync(Cancel);
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

    private Task<EncodeScratchFile> WritingAsync(EncodeJob job) => WritingAsync(job, EncodeScratchKind.WorkFile, job.WorkFileName);

    private async Task<EncodeScratchFile> WritingAsync(EncodeJob job, EncodeScratchKind kind, EncodeFileName name)
    {
        EncodeScratchFile scratch = EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            job.Id,
            kind,
            Primary,
            name,
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
