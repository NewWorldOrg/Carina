using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeStandingBoardTests
{
    private static readonly RecordingId Asked = RecordingId.New();

    private static readonly RecordingId Another = RecordingId.New();

    public static TheoryData<EncodeStanding, EncodeJobStatus[]> WhatARecordingStandsAtForTheJobsBehindIt() =>
        new()
        {
            { EncodeStanding.NotEncoded, [] },
            { EncodeStanding.NotEncoded, [EncodeJobStatus.Cancelled] },
            { EncodeStanding.Queued, [EncodeJobStatus.Queued] },
            { EncodeStanding.Running, [EncodeJobStatus.Running] },
            { EncodeStanding.Completed, [EncodeJobStatus.Completed] },
            { EncodeStanding.Failed, [EncodeJobStatus.Failed] },
            { EncodeStanding.Failed, [EncodeJobStatus.Failed, EncodeJobStatus.Cancelled] },
            { EncodeStanding.Queued, [EncodeJobStatus.Failed, EncodeJobStatus.Queued] },
            { EncodeStanding.Running, [EncodeJobStatus.Queued, EncodeJobStatus.Running] },
            { EncodeStanding.Completed, [EncodeJobStatus.Completed, EncodeJobStatus.Running] },
            {
                EncodeStanding.Completed,
                [
                    EncodeJobStatus.Completed,
                    EncodeJobStatus.Completed,
                    EncodeJobStatus.Running,
                    EncodeJobStatus.Failed,
                    EncodeJobStatus.Cancelled,
                ]
            },
        };

    [Theory]
    [MemberData(nameof(WhatARecordingStandsAtForTheJobsBehindIt))]
    public void ARecordingStandsAtOnePlaceHoweverManyJobsTheLedgerHoldsForIt(
        EncodeStanding expected,
        EncodeJobStatus[] held)
        => Assert.Equal(expected, EncodeStandings.Over(held));

    [Fact(DisplayName = "BR-ES-002: an artefact already made is what a recording stands at, whatever is running beside it")]
    public void AnArtefactAlreadyMadeOutweighsWorkStillGoingOn()
    {
        Assert.Equal(
            EncodeStanding.Completed,
            EncodeStandings.Over([EncodeJobStatus.Running, EncodeJobStatus.Completed]));

        Assert.Equal(
            EncodeStanding.Running,
            EncodeStandings.Over([EncodeJobStatus.Running, EncodeJobStatus.Cancelled, EncodeJobStatus.Failed]));
    }

    [Fact(DisplayName = "BR-ES-002: work in the queue speaks over a failure that is already history")]
    public void WorkInTheQueueSpeaksOverAFailureThatIsAlreadyHistory()
        => Assert.Equal(
            EncodeStanding.Queued,
            EncodeStandings.Over([EncodeJobStatus.Failed, EncodeJobStatus.Failed, EncodeJobStatus.Queued]));

    [Fact]
    public void AStatusTheLedgerCouldNotHoldIsRefusedRatherThanReadAsUnencoded()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EncodeStandings.Over([(EncodeJobStatus)9]));

    [Fact(DisplayName = "BR-ES-002: a board answers for a recording it was never told about")]
    public void ABoardAnswersForARecordingItWasNeverToldAbout()
    {
        EncodeStandingBoard board = EncodeStandingBoard.Of([(Asked, EncodeJobStatus.Running)]);

        Assert.Equal(EncodeStanding.Running, board.For(Asked));
        Assert.Equal(EncodeStanding.NotEncoded, board.For(Another));
        Assert.Equal(EncodeStanding.NotEncoded, EncodeStandingBoard.Empty.For(Asked));
    }

    [Fact]
    public void ABoardFoldsEveryRowItWasHandedForTheSameRecording()
    {
        EncodeStandingBoard board = EncodeStandingBoard.Of(
        [
            (Asked, EncodeJobStatus.Failed),
            (Asked, EncodeJobStatus.Completed),
            (Another, EncodeJobStatus.Cancelled),
            (Another, EncodeJobStatus.Queued),
        ]);

        Assert.Equal(EncodeStanding.Completed, board.For(Asked));
        Assert.Equal(EncodeStanding.Queued, board.For(Another));
    }
}
