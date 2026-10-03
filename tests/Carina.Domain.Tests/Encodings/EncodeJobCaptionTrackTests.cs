using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeJobCaptionTrackTests
{
    private static readonly DateTime Queued = new(2026, 10, 4, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeAt = new(2026, 10, 4, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeAgainAt = new(2026, 10, 4, 5, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    [Fact]
    public void BrEd2019AnArtefactThatStandsAwaitsATrackFromARecordNoTrackWasTriedFrom()
    {
        EncodeJob job = Completed();

        Assert.True(job.AwaitsCaptionTrack(MadeAt));

        job.Tracked(EncodeCaptionTrack.Added, MadeAt);

        Assert.False(job.AwaitsCaptionTrack(MadeAt));
        Assert.True(job.AwaitsCaptionTrack(MadeAgainAt));
        Assert.Equal((EncodeCaptionTrack.Added, MadeAt, 0), (job.CaptionTrack!.Value, job.CaptionTrackFrom!.Value, job.CaptionTrackAttempts));
    }

    [Fact]
    public void BrEd2019ATrackWithheldFromARecordIsNotTriedAgainFromTheSameRecord()
    {
        EncodeJob job = Completed();

        job.Tracked(EncodeCaptionTrack.Withheld, MadeAt);

        Assert.False(job.AwaitsCaptionTrack(MadeAt));
    }

    [Fact]
    public void BrEd2019AFailedTrackIsTriedThreeTimesFromTheSameRecordAndCountedAgainFromANewOne()
    {
        EncodeJob job = Completed();

        job.Tracked(EncodeCaptionTrack.Failed, MadeAt);
        job.Tracked(EncodeCaptionTrack.Failed, MadeAt);

        Assert.True(job.AwaitsCaptionTrack(MadeAt));

        job.Tracked(EncodeCaptionTrack.Failed, MadeAt);

        Assert.Equal(EncodeJob.CaptionTrackTriesAtMost, job.CaptionTrackAttempts);
        Assert.False(job.AwaitsCaptionTrack(MadeAt));

        job.Tracked(EncodeCaptionTrack.Failed, MadeAgainAt);

        Assert.Equal(1, job.CaptionTrackAttempts);
        Assert.True(job.AwaitsCaptionTrack(MadeAgainAt));
    }

    [Fact]
    public void BrEd2019ARunningJobWritesDownTheTrackOfTheArtefactItIsAboutToPlace()
    {
        EncodeJob job = Running();

        job.Tracked(EncodeCaptionTrack.Added, MadeAt);

        Assert.Equal(EncodeCaptionTrack.Added, job.CaptionTrack);
        Assert.False(job.AwaitsCaptionTrack(MadeAt));
    }

    [Fact]
    public void BrEd2019AJobWhoseArtefactDoesNotStandHasNoTrackPutIn()
    {
        EncodeJob replaced = Completed();
        EncodeJob newer = Completed(replaced.RecordingId);
        replaced.Replaced(newer, Queued.AddHours(3));
        EncodeJob failed = Running();
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "it broke", Queued.AddHours(1));
        EncodeJob waiting = Waiting();

        Assert.False(replaced.AwaitsCaptionTrack(MadeAt));
        Assert.Throws<InvalidOperationException>(() => replaced.Tracked(EncodeCaptionTrack.Added, MadeAt));
        Assert.Throws<InvalidOperationException>(() => failed.Tracked(EncodeCaptionTrack.Added, MadeAt));
        Assert.Throws<InvalidOperationException>(() => waiting.Tracked(EncodeCaptionTrack.Added, MadeAt));
    }

    [Fact]
    public void ATrackIsRehydratedWithWhatBecameOfItAndTheRecordItCameFromTogether()
    {
        EncodeJob job = Completed();

        Assert.Throws<ArgumentException>(() => Rehydrated(job, EncodeCaptionTrack.Added, null, 0));
        Assert.Throws<ArgumentException>(() => Rehydrated(job, null, MadeAt, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(job, EncodeCaptionTrack.Added, MadeAt, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(job, EncodeCaptionTrack.Failed, MadeAt, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(job, (EncodeCaptionTrack)9, MadeAt, 0));
        Assert.Equal(2, Rehydrated(job, EncodeCaptionTrack.Failed, MadeAt, 2).CaptionTrackAttempts);
    }

    [Fact]
    public void BrEd2019TheFilesATrackIsMadeWithAreNamedForTheRecordingTheJobAndTheAttempt()
    {
        EncodeJob job = Running();

        Assert.Equal($"{job.RecordingId.Wire}.{job.Id.Wire}.attempt1.vtt", job.CaptionTrackFileName.Value);
        Assert.Equal($"{job.RecordingId.Wire}.{job.Id.Wire}.attempt1.captioned", job.CaptionedFileName.Value);
    }

    private static EncodeJob Rehydrated(EncodeJob job, EncodeCaptionTrack? track, DateTime? from, int attempts)
        => EncodeJob.Rehydrate(
            job.Id,
            job.RecordingId,
            job.ProfileId,
            job.DestinationId,
            job.OutputRoot,
            job.Status,
            job.Attempt,
            job.QueuedAt,
            job.StartedAt,
            job.EndedAt,
            job.Failure,
            job.ArtefactName,
            job.Route,
            job.Programme,
            job.Headway,
            job.Timeline,
            job.Chapters,
            job.MakesItAgain,
            job.NameGivenUpAt,
            job.ReplacedAt,
            track,
            from,
            attempts);

    private static EncodeJob Waiting(RecordingId? recording = null)
        => EncodeJob.Queue(EncodeJobId.New(), recording ?? RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), Primary, Queued);

    private static EncodeJob Running(RecordingId? recording = null)
    {
        EncodeJob job = Waiting(recording);
        job.Start(Queued.AddSeconds(5));

        return job;
    }

    private static EncodeJob Completed(RecordingId? recording = null)
    {
        EncodeJob job = Running(recording);
        job.Name(EncodeFileName.Artefact(job.RecordingId, job.ProfileId));
        job.Complete(Queued.AddHours(1));

        return job;
    }
}
