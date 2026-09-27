using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

namespace Carina.Infrastructure.Tests.Encodings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeSuccessionDatabaseTests(RepositoryDatabase database)
{
    private static readonly DateTime MadeFirst = new(2026, 9, 5, 3, 10, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeAgain = new(2026, 9, 5, 3, 40, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "settling through the ledger marks the earlier job replaced and writes down the removal it owes in one write, the file goes, and it reads back so")]
    public async Task SettlingThroughTheLedgerMarksTheEarlierJobReplacedAndItsFileGoes()
    {
        await ClearAsync();
        using EncodeHarness harness = new();
        RecordingId recording = RecordingId.New();
        EncodeJob earlier = Completed(recording, MadeFirst);
        EncodeJob newer = Completed(recording, MadeAgain);
        await AddAsync(earlier, newer);
        File.WriteAllText(harness.ArtefactPathOf(earlier), "the first picture");
        File.WriteAllText(harness.ArtefactPathOf(newer), "the second picture");

        await using (CarinaDbContext settling = database.Open())
        {
            EncodeSuccessionReport report = await Succession(settling, harness).SettleAsync(recording, Cancel);

            Assert.Equal(1, report.Replaced);
            Assert.Equal(1, report.FilesRemoved);
        }

        Assert.False(File.Exists(harness.ArtefactPathOf(earlier)));
        Assert.Equal("the second picture", File.ReadAllText(harness.ArtefactPathOf(newer)));

        await using CarinaDbContext reading = database.Open();
        EncodeJobRepository jobs = new(reading);
        EncodeJob readEarlier = (await jobs.FindAsync(earlier.Id, Cancel))!;
        EncodeJob readNewer = (await jobs.FindAsync(newer.Id, Cancel))!;
        Assert.Equal(harness.Clock.GetUtcNow().UtcDateTime, readEarlier.ReplacedAt);
        Assert.Equal(earlier.ArtefactName, readEarlier.ArtefactName);
        Assert.True(readNewer.StandsAsTheArtefact);

        EncodeScratchFile owed = await reading.Set<EncodeScratchFile>().SingleAsync(Cancel);
        Assert.Equal(EncodeScratchKind.ReplacedArtefact, owed.Kind);
        Assert.Equal(earlier.Id, owed.JobId);
        Assert.Equal(EncodeScratchFate.Removed, owed.Fate);
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

        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<RecordingId> made = await new EncodeJobRepository(reading).ListRecordingsMadeMoreThanOnceAsync(Cancel);

        Assert.Equal(
            new HashSet<RecordingId> { twice, settled },
            made.ToHashSet());
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

        await using (CarinaDbContext writing = database.Open())
        {
            EncodeScratchLedger ledger = new(writing);
            EncodeScratchFile earlierRemoval = Owed(first, shared, MadeAgain);
            await ledger.RecordAsync(earlierRemoval, Cancel);
            earlierRemoval.Settle(EncodeScratchFate.Removed, MadeAgain);
            await ledger.SaveAsync(earlierRemoval, Cancel);
            await ledger.RecordAsync(Owed(second, shared, MadeAgain.AddMinutes(1)), Cancel);
        }

        await using (CarinaDbContext counting = database.Open())
        {
            Assert.Equal(2, await counting.Set<EncodeScratchFile>().CountAsync(Cancel));
        }

        await using CarinaDbContext refusing = database.Open();
        EncodeScratchLedger refused = new(refusing);
        await refused.RecordAsync(Work(first, MadeAgain), Cancel);

        await Assert.ThrowsAsync<DbUpdateException>(() => refused.RecordAsync(Work(first, MadeAgain.AddMinutes(1)), Cancel));
    }

    [Fact(DisplayName = "the table itself refuses a job read as replaced that did not complete")]
    public async Task TheTableRefusesAReplacedJobThatDidNotComplete()
    {
        await ClearAsync();
        EncodeJob running = Running(RecordingId.New());
        await AddAsync(running);

        await using CarinaDbContext writing = database.Open();
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => writing.Database.ExecuteSqlAsync(
                $"UPDATE encode_job SET replaced_at = {MadeAgain} WHERE id = {running.Id.Value}",
                Cancel));

        Assert.Equal("ck_encode_job_replaced", refused.ConstraintName);
    }

    private static EncodeArtefactSuccession Succession(CarinaDbContext context, EncodeHarness harness)
    {
        EncodeScratchLedger ledger = new(context);

        return new EncodeArtefactSuccession(
            new EncodeJobRepository(context),
            ledger,
            new DatabaseAtomicWrite(context),
            new EncodeScratchCleaner(ledger, harness.Places, harness.Clock, NullLogger<EncodeScratchCleaner>.Instance),
            harness.Clock,
            NullLogger<EncodeArtefactSuccession>.Instance);
    }

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
        await using CarinaDbContext writing = database.Open();
        EncodeProfileRepository profiles = new(writing);
        EncodeDestinationRepository destinations = new(writing);
        EncodeJobRepository repository = new(writing);

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

    private async Task ClearAsync()
    {
        await using CarinaDbContext clearing = database.Open();
        await clearing.Set<EncodeChapter>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeScratchFile>().ExecuteDeleteAsync(Cancel);
        await clearing.Set<EncodeJob>().ExecuteDeleteAsync(Cancel);
    }
}
