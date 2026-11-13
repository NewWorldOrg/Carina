using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Encodings;

[Trait("Category", "Unit")]
public sealed class EncodeScratchFileTests
{
    private static readonly DateTime Written = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Removed = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly RecordingId Recording = RecordingId.New();

    private static readonly EncodeJobId Job = EncodeJobId.New();

    private static readonly DateTime Queued = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Encodes = new("encodes");

    [Fact(DisplayName = "a scratch file is written into the ledger before it exists, and is then still owed a removal")]
    public void AScratchFileIsWrittenIntoTheLedgerBeforeItExistsAndIsOwedARemoval()
    {
        EncodeScratchFile scratch = Recorded();

        Assert.Equal(Job, scratch.JobId);
        Assert.Equal(EncodeScratchKind.WorkFile, scratch.Kind);
        Assert.Equal(Written, scratch.WrittenAt);
        Assert.Null(scratch.RemovedAt);
        Assert.Null(scratch.Fate);
        Assert.True(scratch.IsOwedARemoval);
    }

    [Theory]
    [InlineData(EncodeScratchFate.Removed)]
    [InlineData(EncodeScratchFate.AlreadyGone)]
    [InlineData(EncodeScratchFate.BecameTheArtefact)]
    public void EveryFateButOneThatLeftTheFileSettlesTheRemovalOwed(EncodeScratchFate fate)
    {
        EncodeScratchFile scratch = Recorded();

        scratch.Settle(fate, Removed);

        Assert.Equal(fate, scratch.Fate);
        Assert.Equal(Removed, scratch.RemovedAt);
        Assert.False(scratch.IsOwedARemoval);
    }

    [Fact]
    public void AFileThatCouldNotBeRemovedIsStillOwedARemovalAndIsSettledAgainOnceItGoes()
    {
        EncodeScratchFile scratch = Recorded();
        scratch.Settle(EncodeScratchFate.CouldNotBeRemoved, Removed);

        Assert.Equal(EncodeScratchFate.CouldNotBeRemoved, scratch.Fate);
        Assert.Equal(Removed, scratch.RemovedAt);
        Assert.True(scratch.IsOwedARemoval);

        scratch.Settle(EncodeScratchFate.Removed, Removed.AddHours(1));

        Assert.Equal(EncodeScratchFate.Removed, scratch.Fate);
        Assert.Equal(Removed.AddHours(1), scratch.RemovedAt);
        Assert.False(scratch.IsOwedARemoval);
    }

    [Fact(DisplayName = "a removal is owed once and settled once")]
    public void ARemovalIsOwedOnceAndSettledOnce()
    {
        EncodeScratchFile scratch = Recorded();
        scratch.Settle(EncodeScratchFate.Removed, Removed);

        Assert.Throws<InvalidOperationException>(() => scratch.Settle(EncodeScratchFate.AlreadyGone, Removed));
    }

    [Fact]
    public void AFateNobodyNamedIsNotAFate()
    {
        EncodeScratchFile scratch = Recorded();

        Assert.Throws<ArgumentOutOfRangeException>(() => scratch.Settle((EncodeScratchFate)99, Removed));
    }

    [Fact]
    public void AKindNobodyNamedIsNotAKind()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            Job,
            (EncodeScratchKind)99,
            new OutputRoot("primary"),
            EncodeFileName.Working(Recording, Job, 1),
            Written));

    [Fact]
    public void ARemovalCannotBeSettledBeforeTheFileWasWritten()
    {
        EncodeScratchFile scratch = Recorded();

        Assert.Throws<ArgumentOutOfRangeException>(() => scratch.Settle(EncodeScratchFate.Removed, Written.AddSeconds(-1)));
    }

    [Fact]
    public void AScratchFileCannotBeMadeWithoutGoingThroughTheOneWayIn()
        => Assert.Empty(typeof(EncodeScratchFile).GetConstructors());

    [Theory(DisplayName = "BR-KD-025: a scratch file not yet removed is claimed while its job is in hand, of every kind")]
    [InlineData(EncodeScratchKind.WorkFile)]
    [InlineData(EncodeScratchKind.Chapters)]
    [InlineData(EncodeScratchKind.ReplacedArtefact)]
    [InlineData(EncodeScratchKind.Captions)]
    [InlineData(EncodeScratchKind.CaptionedWork)]
    [InlineData(EncodeScratchKind.ChapteredWork)]
    public void AScratchFileIsClaimedWhileItsJobIsInHand(EncodeScratchKind kind)
    {
        EncodeJob job = Running();

        Assert.True(WrittenForClaims(job, kind).IsClaimedWhile(job));
    }

    [Theory(DisplayName = "BR-ED2-019: once its job has ended, a scratch file is claimed only as work on an artefact already made")]
    [InlineData(EncodeScratchKind.WorkFile, false)]
    [InlineData(EncodeScratchKind.Chapters, true)]
    [InlineData(EncodeScratchKind.ReplacedArtefact, false)]
    [InlineData(EncodeScratchKind.Captions, true)]
    [InlineData(EncodeScratchKind.CaptionedWork, true)]
    [InlineData(EncodeScratchKind.ChapteredWork, true)]
    public void AScratchFileOfAnEndedJobIsClaimedOnlyAsWorkOnAStandingArtefact(EncodeScratchKind kind, bool claimed)
    {
        EncodeJob job = Completed(Ended);

        Assert.Equal(claimed, WrittenForClaims(job, kind).IsClaimedWhile(job));
        Assert.Equal(claimed, EncodeScratchFile.IsWorkOnAStandingArtefact(kind));
    }

    [Theory(DisplayName = "BR-KD-025: a scratch file removed, or one that could not be removed, is claimed no more")]
    [InlineData(EncodeScratchFate.Removed)]
    [InlineData(EncodeScratchFate.CouldNotBeRemoved)]
    public void AScratchFileSettledIsNotClaimed(EncodeScratchFate fate)
    {
        EncodeJob job = Running();
        EncodeScratchFile scratch = WrittenForClaims(job, EncodeScratchKind.Captions);

        scratch.Settle(fate, Ended);

        Assert.False(scratch.IsClaimedWhile(job));
    }

    [Fact]
    public void AScratchFileIsJudgedOnlyByTheJobThatWroteIt()
        => Assert.Throws<ArgumentException>(() => WrittenForClaims(Running(), EncodeScratchKind.WorkFile).IsClaimedWhile(Running()));

    private static EncodeScratchFile Recorded()
        => EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            Job,
            EncodeScratchKind.WorkFile,
            new OutputRoot("primary"),
            EncodeFileName.Working(Recording, Job, 1),
            Written);

    private static EncodeScratchFile WrittenForClaims(EncodeJob job, EncodeScratchKind kind)
        => EncodeScratchFile.Record(
            EncodeScratchFileId.New(),
            job.Id,
            kind,
            Encodes,
            EncodeFileName.Working(job.RecordingId, job.Id, 1),
            Started);

    private static EncodeJob Queue()
        => EncodeJob.QueueAgain(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Encodes,
            Queued);

    private static EncodeJob Running()
    {
        EncodeJob job = Queue();
        job.Start(Started);

        return job;
    }

    private static EncodeJob Completed(DateTime ended, RecordingId? recording = null)
    {
        RecordingId of = recording ?? RecordingId.New();
        EncodeProfileId made = EncodeProfileId.New();

        return EncodeJob.Rehydrate(
            EncodeJobId.New(),
            of,
            made,
            EncodeDestinationId.New(),
            Encodes,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            Queued,
            Started,
            ended,
            null,
            EncodeFileName.Artefact(of, made),
            null,
            null,
            null,
            null,
            null);
    }
}
