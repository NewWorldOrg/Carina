using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingEndTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    [Fact(DisplayName = "BR-KS-001: a recording nobody asked to stop is over from the instant its window closes, and not before")]
    public void ARecordingIsOverFromTheInstantItsWindowCloses()
    {
        Recording recording = RecordingFactory.Started();

        Assert.False(recording.IsOver(recording.ExpectedWindowEnd.AddTicks(-1)));
        Assert.True(recording.IsOver(recording.ExpectedWindowEnd));
        Assert.True(recording.IsOver(recording.ExpectedWindowEnd.AddHours(1)));
    }

    [Fact(DisplayName = "BR-KD-006: a recording this side asked to stop is over at once, whatever its window says")]
    public void ARecordingAskedToStopIsOverAtOnce()
    {
        Recording recording = RecordingFactory.Started();

        recording.Abort(Now);

        Assert.True(recording.IsOver(Now));
        Assert.True(recording.IsOver(recording.ExpectedWindowStart));
    }

    [Fact(DisplayName = "BR-KD-009: a recording that follows its programme later is over when the later end comes")]
    public void ARecordingThatFollowsItsProgrammeIsOverWhenTheLaterEndComes()
    {
        Recording recording = RecordingFactory.Started();
        DateTime promised = recording.ExpectedWindowEnd;

        recording.Extend(promised.AddMinutes(10));

        Assert.False(recording.IsOver(promised));
        Assert.True(recording.IsOver(promised.AddMinutes(10)));
    }

    [Fact(DisplayName = "BR-KD-006: a recording is due to be stopped once its window has closed, until this side has asked")]
    public void ARecordingIsDueToBeStoppedOnceItsWindowClosesUntilAsked()
    {
        Recording recording = RecordingFactory.Started();
        DateTime closes = recording.ExpectedWindowEnd;

        Assert.False(recording.IsDueToStop(closes.AddTicks(-1)));
        Assert.True(recording.IsDueToStop(closes));

        recording.Abort(closes);

        Assert.False(recording.IsDueToStop(closes));
        Assert.False(recording.IsDueToStop(closes.AddHours(1)));
    }

    [Fact(DisplayName = "BR-KD-006: a recording stopped before its window closed is over and is not stopped a second time")]
    public void ARecordingStoppedEarlyIsOverAndNotStoppedAgain()
    {
        Recording recording = RecordingFactory.Started();

        recording.Abort(Now);

        Assert.True(recording.IsOver(Now));
        Assert.False(recording.IsDueToStop(recording.ExpectedWindowEnd));
    }

    [Fact(DisplayName = "BR-KD-009: a recording made for a reservation follows its programme until this side asks it to stop")]
    public void ARecordingMadeForAReservationFollowsItsProgrammeUntilAskedToStop()
    {
        Recording recording = RecordingFactory.Started(reservationId: ReservationId.New());

        Assert.True(recording.FollowsItsProgramme);
        Assert.True(recording.IsOver(recording.ExpectedWindowEnd));
        Assert.True(recording.FollowsItsProgramme);

        recording.Abort(Now);

        Assert.False(recording.FollowsItsProgramme);
    }

    [Fact(DisplayName = "BR-KD-009: a recording no reservation asked for has no margin to follow a programme with")]
    public void ARecordingNoReservationAskedForDoesNotFollowItsProgramme()
        => Assert.False(RecordingFactory.Started().FollowsItsProgramme);
}
