using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Tests.Integrity;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class EncodeScratchCleanerTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Ended = new(2026, 9, 5, 3, 30, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "BR-ED2-010: what is removed is what the ledger names for the job, and nothing beside it")]
    public async Task WhatIsRemovedIsWhatTheLedgerNamesForTheJobAndNothingBesideIt()
    {
        using var harness = new EncodeHarness();
        EncodeJob failed = harness.Running();
        EncodeJob running = harness.Running();
        string failedWork = harness.WorkFileOf(failed, "half a picture");
        string runningWork = harness.WorkFileOf(running, "a picture in progress");
        string lookAlike = harness.Shelf.Under($"{failed.RecordingId.Wire}.{EncodeJobId.New().Wire}.attempt1.encoding");
        File.WriteAllText(lookAlike, "a work file the ledger never heard of");
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "exit 1", Ended);

        IReadOnlyList<EncodeScratchFile> cleared = await harness.Cleaner.ClearAsync(failed, Cancel);

        Assert.False(File.Exists(failedWork));
        Assert.True(File.Exists(runningWork), "another job's work file was not swept");
        Assert.True(File.Exists(lookAlike), "a look-alike no ledger row names was not swept");
        EncodeScratchFile settled = Assert.Single(cleared);
        Assert.Equal(EncodeScratchFate.Removed, settled.Fate);
        Assert.Equal(harness.Clock.GetUtcNow().UtcDateTime, settled.RemovedAt);
        Assert.Equal([$"settled {failed.WorkFileName.Value} Removed"], harness.Scratch.Moves);
    }

    [Fact(DisplayName = "BR-ED2-010: a file that is already gone is written down as gone, not as an error")]
    public async Task AFileThatIsAlreadyGoneIsWrittenDownAsGone()
    {
        using var harness = new EncodeHarness();
        EncodeJob cancelled = harness.Running();
        string work = harness.WorkFileOf(cancelled, "a picture");
        File.Delete(work);
        cancelled.Cancel(Ended);

        IReadOnlyList<EncodeScratchFile> cleared = await harness.Cleaner.ClearAsync(cancelled, Cancel);

        Assert.Equal(EncodeScratchFate.AlreadyGone, Assert.Single(cleared).Fate);
        Assert.Empty(harness.CleanerLog.Warnings);
    }

    [Fact(DisplayName = "BR-ED2-010: what was already settled is not owed again")]
    public async Task WhatWasAlreadySettledIsNotOwedAgain()
    {
        using var harness = new EncodeHarness();
        EncodeJob job = harness.Running();
        harness.WorkFileOf(job, "a picture");
        harness.Scratch.Files[0].Settle(EncodeScratchFate.BecameTheArtefact, Ended);
        job.Name(EncodeFileName.Artefact(job.RecordingId, job.ProfileId));
        job.Complete(Ended);

        Assert.Empty(await harness.Cleaner.ClearAsync(job, Cancel));
        Assert.Empty(harness.Scratch.Moves);
    }

    [Fact(DisplayName = "BR-ED2-010: scratch is cleared once a job has ended, never while it runs")]
    public async Task ScratchIsClearedOnceAJobHasEndedNeverWhileItRuns()
    {
        using var harness = new EncodeHarness();
        EncodeJob running = harness.Running();
        string work = harness.WorkFileOf(running, "a picture in progress");

        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Cleaner.ClearAsync(running, Cancel));
        Assert.True(File.Exists(work));
    }

    [Fact]
    public async Task AScratchFileUnderARootThisProcessCannotPlaceIsSettledAsNotRemoved()
    {
        using var harness = new EncodeHarness();
        EncodeJob job = harness.Running();
        job.Fail(EncodeFailure.SourceMissing, "gone", Ended);
        harness.Scratch.Files.Add(EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            job.Id,
            EncodeScratchKind.Chapters,
            new OutputRoot("elsewhere"),
            new EncodeFileName($"{job.RecordingId.Wire}.chapters"),
            EncodeHarness.Queued));

        IReadOnlyList<EncodeScratchFile> cleared = await harness.Cleaner.ClearAsync(job, Cancel);

        Assert.Equal(EncodeScratchFate.CouldNotBeRemoved, Assert.Single(cleared).Fate);
        Assert.Single(harness.CleanerLog.Warnings);
    }

    [Fact]
    public async Task AScratchFileInAWorkingDirectoryOfItsOwnIsRemovedFromThere()
    {
        using var harness = new EncodeHarness(workingBeside: false);
        EncodeJob job = harness.Running();
        string work = harness.WorkFileOf(job, "a picture");
        job.Fail(EncodeFailure.TimedOut, "late", Ended);

        await harness.Cleaner.ClearAsync(job, Cancel);

        Assert.False(File.Exists(work));
        Assert.Empty(harness.Workshop!.Snapshot());
    }
}
