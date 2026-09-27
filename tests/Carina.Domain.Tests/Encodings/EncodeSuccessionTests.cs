using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeSuccessionTests
{
    private static readonly DateTime Queued = new(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 4, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime MadeFirst = new(2026, 9, 4, 4, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeAgain = new(2026, 9, 4, 5, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Now = new(2026, 9, 4, 6, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Encodes = new("encodes");

    [Fact(DisplayName = "the artefact of the job that completed last stands, and every earlier completed job is to be replaced")]
    public void TheJobThatCompletedLastStands()
    {
        RecordingId recording = RecordingId.New();
        EncodeJob first = Completed(recording, MadeFirst);
        EncodeJob last = Completed(recording, MadeAgain);
        EncodeJob second = Completed(recording, MadeFirst.AddMinutes(30));

        EncodeSuccession succession = EncodeSuccession.Of([first, last, second]);

        Assert.Same(last, succession.Standing);
        Assert.Equal([second, first], succession.ToReplace);
    }

    [Fact(DisplayName = "a job that failed, was called off or is still under way neither stands nor is replaced")]
    public void AJobThatDidNotCompleteNeitherStandsNorIsReplaced()
    {
        RecordingId recording = RecordingId.New();
        EncodeJob made = Completed(recording, MadeFirst);
        EncodeJob failed = Running(recording);
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "exit 1", MadeAgain);
        EncodeJob cancelled = Running(recording);
        cancelled.Cancel(MadeAgain);
        EncodeJob running = Running(recording);

        EncodeSuccession succession = EncodeSuccession.Of([made, failed, cancelled, running]);

        Assert.Same(made, succession.Standing);
        Assert.Empty(succession.ToReplace);
    }

    [Fact(DisplayName = "a job already replaced is not replaced again, and a recording no job completed for has no artefact")]
    public void AJobAlreadyReplacedIsNotReplacedAgain()
    {
        RecordingId recording = RecordingId.New();
        EncodeJob replaced = Completed(recording, MadeFirst);
        EncodeJob standing = Completed(recording, MadeAgain);
        replaced.Replaced(standing, Now);

        EncodeSuccession succession = EncodeSuccession.Of([replaced, standing]);

        Assert.Same(standing, succession.Standing);
        Assert.Empty(succession.ToReplace);
        Assert.Null(EncodeSuccession.Of([Running(recording)]).Standing);
        Assert.Null(EncodeSuccession.Of([]).Standing);
    }

    [Fact(DisplayName = "a succession is worked out among the jobs of one recording only")]
    public void ASuccessionIsWorkedOutAmongTheJobsOfOneRecording()
        => Assert.Throws<ArgumentException>(
            () => EncodeSuccession.Of([Completed(RecordingId.New(), MadeFirst), Completed(RecordingId.New(), MadeAgain)]));

    [Fact(DisplayName = "a replaced job still names what it made, stops standing as the artefact, and says when it was replaced")]
    public void AReplacedJobStillNamesWhatItMade()
    {
        RecordingId recording = RecordingId.New();
        EncodeJob earlier = Completed(recording, MadeFirst);
        EncodeFileName made = earlier.ArtefactName!;

        earlier.Replaced(Completed(recording, MadeAgain), Now);

        Assert.Equal(Now, earlier.ReplacedAt);
        Assert.Equal(made, earlier.ArtefactName);
        Assert.Equal(EncodeJobStatus.Completed, earlier.Status);
        Assert.False(earlier.StandsAsTheArtefact);
    }

    [Fact(DisplayName = "only a completed job whose artefact stands is replaced, and only by another completed job of the same recording, at a time after it was made")]
    public void OnlyAStandingArtefactIsReplacedByAnotherOfTheSameRecording()
    {
        RecordingId recording = RecordingId.New();
        EncodeJob earlier = Completed(recording, MadeFirst);
        EncodeJob newer = Completed(recording, MadeAgain);

        Assert.Throws<InvalidOperationException>(() => Running(recording).Replaced(newer, Now));
        Assert.Throws<ArgumentException>(() => earlier.Replaced(earlier, Now));
        Assert.Throws<ArgumentException>(() => earlier.Replaced(Completed(RecordingId.New(), MadeAgain), Now));
        Assert.Throws<ArgumentException>(() => earlier.Replaced(Running(recording), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => earlier.Replaced(newer, MadeFirst.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(() => earlier.Replaced(newer, DateTime.SpecifyKind(Now, DateTimeKind.Local)));

        earlier.Replaced(newer, Now);

        Assert.Throws<InvalidOperationException>(() => earlier.Replaced(newer, Now));
    }

    [Fact(DisplayName = "two jobs share an artefact when they name the same file under the same root, and not when either differs")]
    public void TwoJobsShareAnArtefactWhenTheyNameTheSameFileUnderTheSameRoot()
    {
        RecordingId recording = RecordingId.New();
        EncodeProfileId profile = EncodeProfileId.New();
        EncodeJob earlier = Completed(recording, MadeFirst, profile);

        Assert.True(earlier.SharesTheArtefactWith(Completed(recording, MadeAgain, profile)));
        Assert.False(earlier.SharesTheArtefactWith(Completed(recording, MadeAgain)));
        Assert.False(earlier.SharesTheArtefactWith(Completed(recording, MadeAgain, profile, new OutputRoot("annex"))));
        Assert.False(Running(recording).SharesTheArtefactWith(Running(recording)));
    }

    [Theory(DisplayName = "a job read back as replaced is one that completed and named what it made")]
    [InlineData(EncodeJobStatus.Running)]
    [InlineData(EncodeJobStatus.Failed)]
    [InlineData(EncodeJobStatus.Cancelled)]
    public void AJobReadBackAsReplacedIsOneThatCompleted(EncodeJobStatus status)
    {
        RecordingId recording = RecordingId.New();
        EncodeProfileId profile = EncodeProfileId.New();

        Assert.Throws<ArgumentException>(() => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            profile,
            EncodeDestinationId.New(),
            Encodes,
            status,
            EncodeJob.FirstAttempt,
            Queued,
            Started,
            status is EncodeJobStatus.Running ? null : MadeFirst,
            status is EncodeJobStatus.Failed ? new EncodeFailureDetail(EncodeFailure.FfmpegExitedNonZero, "exit 1", MadeFirst) : null,
            EncodeFileName.Artefact(recording, profile),
            null,
            null,
            null,
            null,
            null,
            replacedAt: Now));
    }

    private static EncodeJob Running(RecordingId recording)
    {
        EncodeJob job = EncodeJob.QueueAgain(
            EncodeJobId.New(),
            recording,
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Encodes,
            Queued);
        job.Start(Started);

        return job;
    }

    private static EncodeJob Completed(
        RecordingId recording,
        DateTime ended,
        EncodeProfileId? profile = null,
        OutputRoot? root = null)
    {
        EncodeProfileId made = profile ?? EncodeProfileId.New();

        return EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            made,
            EncodeDestinationId.New(),
            root ?? Encodes,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            Queued,
            Started,
            ended,
            null,
            EncodeFileName.Artefact(recording, made),
            null,
            null,
            null,
            null,
            null);
    }
}
