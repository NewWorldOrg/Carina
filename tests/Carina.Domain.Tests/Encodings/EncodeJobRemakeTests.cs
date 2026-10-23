using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeJobRemakeTests
{
    private static readonly DateTime Queued = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Later = new(2026, 9, 5, 3, 10, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Primary = new("primary");

    private static EncodeJob Waiting()
        => EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), Primary, Queued);

    private static EncodeJob Running()
    {
        EncodeJob job = Waiting();
        job.Start(Started);

        return job;
    }

    [Fact(DisplayName = "a job makes nothing again until a run says what it made outgrew its recording")]
    public void AJobMakesNothingAgainUntilARunSaysSo()
    {
        Assert.Null(Waiting().Remade);
        Assert.Null(Running().Remade);
    }

    [Fact(DisplayName = "a running job whose artefact outgrew its recording writes down that it makes it again, and why")]
    public void ARunningJobWritesDownThatItMakesItAgain()
    {
        EncodeJob job = Running();

        job.Remaking();

        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
    }

    [Fact(DisplayName = "a job makes its artefact again once, and only while it runs")]
    public void AJobMakesItsArtefactAgainOnceAndOnlyWhileItRuns()
    {
        EncodeJob job = Running();
        job.Remaking();

        Assert.Throws<InvalidOperationException>(job.Remaking);
        Assert.Throws<InvalidOperationException>(Waiting().Remaking);
    }

    [Fact(DisplayName = "a job that made its artefact again says what it kept once, and only after it began making it again")]
    public void AJobThatMadeItsArtefactAgainSaysWhatItKept()
    {
        EncodeJob job = Running();

        Assert.Throws<InvalidOperationException>(() => job.MadeAgain(EncodeRemake.StillLargerThanTheSource));

        job.Remaking();
        job.MadeAgain(EncodeRemake.StillLargerThanTheSource);

        Assert.Equal(EncodeRemake.StillLargerThanTheSource, job.Remade);
        Assert.Throws<InvalidOperationException>(() => job.MadeAgain(EncodeRemake.NoSmallerTheSecondTime));
        Assert.Throws<ArgumentOutOfRangeException>(() => Running().MadeAgain((EncodeRemake)99));
    }

    [Fact(DisplayName = "the work file a job keeps is the first until it makes its artefact again, the second while it does and once it kept that, and the first once the second came out no smaller")]
    public void TheWorkFileAJobKeepsFollowsWhatItKept()
    {
        EncodeJob once = Running();
        EncodeJob smaller = Running();
        smaller.Remaking();
        EncodeJob still = Running();
        still.Remaking();
        still.MadeAgain(EncodeRemake.StillLargerThanTheSource);
        EncodeJob first = Running();
        first.Remaking();
        first.MadeAgain(EncodeRemake.NoSmallerTheSecondTime);

        Assert.Equal(once.WorkFileName, once.KeptWorkFileName);
        Assert.Equal(smaller.RemadeFileName, smaller.KeptWorkFileName);
        Assert.Equal(still.RemadeFileName, still.KeptWorkFileName);
        Assert.Equal(first.WorkFileName, first.KeptWorkFileName);
        Assert.Equal(first.WorkFileName, first.FileToPlace);
        Assert.Equal(smaller.RemadeFileName, smaller.FileToPlace);
    }

    [Fact(DisplayName = "the second work file is named for the recording, the job and the attempt, apart from the first")]
    public void TheSecondWorkFileIsNamedApartFromTheFirst()
    {
        EncodeJob job = Running();

        Assert.NotEqual(job.WorkFileName, job.RemadeFileName);
        Assert.True(job.RemadeFileName.Names(job.RecordingId));
        Assert.True(job.RemadeFileName.Names(job.Id));
        Assert.EndsWith($".attempt{job.Attempt}.remade{EncodeFileName.WorkExtension}", job.RemadeFileName.Value, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "what a job made again is kept when it ends, and forgotten when it goes back to the queue to start over")]
    public void WhatAJobMadeAgainIsForgottenWhenItStartsOver()
    {
        EncodeJob ended = Running();
        ended.Remaking();
        ended.Name(EncodeFileName.Artefact(ended.RecordingId, ended.ProfileId));
        ended.Complete(Later);

        EncodeJob putBack = Running();
        putBack.Remaking();
        putBack.Requeue(Later);

        Assert.Equal(EncodeRemake.LargerThanTheSource, ended.Remade);
        Assert.Null(putBack.Remade);
    }

    [Fact(DisplayName = "a row that says a waiting job made something again, or names no reason the ledger knows, is not one the ledger can hold")]
    public void ARowThatSaysAWaitingJobMadeSomethingAgainIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Rehydrated(EncodeJobStatus.Queued, EncodeRemake.LargerThanTheSource));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(EncodeJobStatus.Running, (EncodeRemake)99));
        Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, Rehydrated(EncodeJobStatus.Running, EncodeRemake.NoSmallerTheSecondTime).Remade);
    }

    private static EncodeJob Rehydrated(EncodeJobStatus status, EncodeRemake remade)
        => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Primary,
            status,
            EncodeJob.FirstAttempt,
            Queued,
            status is EncodeJobStatus.Queued ? null : Started,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            remade: remade);
}
