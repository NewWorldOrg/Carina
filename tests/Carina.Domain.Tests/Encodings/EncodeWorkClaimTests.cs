using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeWorkClaimTests
{
    private static readonly DateTime Queued = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Encodes = new("encodes");

    [Theory(DisplayName = "BR-KD-025: a job still in hand claims the artefact it makes, whether or not the recording is still held")]
    [InlineData(true)]
    [InlineData(false)]
    public void AJobInHandClaimsItsArtefact(bool recordingHeld)
    {
        Assert.True(Queue().ClaimsItsArtefact(recordingHeld));
        Assert.True(Running().ClaimsItsArtefact(recordingHeld));
    }

    [Fact(DisplayName = "BR-KD-025: a completed job claims its artefact only while it stands for a recording the ledger still holds")]
    public void ACompletedJobClaimsItsArtefactWhileItStandsForARecordingHeld()
    {
        EncodeJob replaced = Completed(Ended);
        replaced.Replaced(Completed(Ended.AddMinutes(1), replaced.RecordingId), Ended.AddMinutes(2));

        Assert.True(Completed(Ended).ClaimsItsArtefact(true));
        Assert.False(Completed(Ended).ClaimsItsArtefact(false));
        Assert.False(replaced.ClaimsItsArtefact(true));
    }

    [Fact(DisplayName = "BR-KD-025: a job that failed or was called off claims no artefact")]
    public void AJobThatDidNotCompleteClaimsNoArtefact()
    {
        EncodeJob failed = Running();
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "exit 1", Ended);
        EncodeJob cancelled = Running();
        cancelled.Cancel(Ended);

        Assert.False(failed.ClaimsItsArtefact(true));
        Assert.False(cancelled.ClaimsItsArtefact(true));
    }

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

        Assert.True(Written(job, kind).IsClaimedWhile(job));
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

        Assert.Equal(claimed, Written(job, kind).IsClaimedWhile(job));
        Assert.Equal(claimed, EncodeScratchFile.IsWorkOnAStandingArtefact(kind));
    }

    [Theory(DisplayName = "BR-KD-025: a scratch file removed, or one that could not be removed, is claimed no more")]
    [InlineData(EncodeScratchFate.Removed)]
    [InlineData(EncodeScratchFate.CouldNotBeRemoved)]
    public void AScratchFileSettledIsNotClaimed(EncodeScratchFate fate)
    {
        EncodeJob job = Running();
        EncodeScratchFile scratch = Written(job, EncodeScratchKind.Captions);

        scratch.Settle(fate, Ended);

        Assert.False(scratch.IsClaimedWhile(job));
    }

    [Fact]
    public void AScratchFileIsJudgedOnlyByTheJobThatWroteIt()
        => Assert.Throws<ArgumentException>(() => Written(Running(), EncodeScratchKind.WorkFile).IsClaimedWhile(Running()));

    private static EncodeScratchFile Written(EncodeJob job, EncodeScratchKind kind)
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
