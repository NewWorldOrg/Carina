using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SpareTimeTests
{
    private static readonly DateTime Noon = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Occupancy Idle = new(true, false, false, null);

    [Fact(DisplayName = "with learning on and nothing recorded, watched or reserved, it is spare time")]
    public void WithNothingGoingOnItIsSpareTime()
        => Assert.Equal(SpareTimeVerdict.Spare, SpareTime.Judge(Idle, Noon));

    [Fact(DisplayName = "with learning off it is never spare time, whatever else is going on")]
    public void WithLearningOffItIsNeverSpareTime()
    {
        Assert.Equal(SpareTimeVerdict.LearningOff, SpareTime.Judge(Idle with { Learning = false }, Noon));
        Assert.Equal(SpareTimeVerdict.LearningOff, SpareTime.Judge(new Occupancy(false, true, true, Noon), Noon));
    }

    [Fact(DisplayName = "while anything is being recorded it is not spare time")]
    public void WhileRecordingItIsNotSpareTime()
        => Assert.Equal(SpareTimeVerdict.Recording, SpareTime.Judge(Idle with { Recording = true }, Noon));

    [Fact(DisplayName = "while anybody is watching it is not spare time")]
    public void WhileWatchingItIsNotSpareTime()
        => Assert.Equal(SpareTimeVerdict.Watching, SpareTime.Judge(Idle with { Watching = true }, Noon));

    [Theory(DisplayName = "a reservation starting within thirty minutes, or one that should have started already, leaves no spare time")]
    [InlineData(-5.0)]
    [InlineData(0.0)]
    [InlineData(29.0)]
    [InlineData(30.0)]
    public void AReservationWithinThirtyMinutesLeavesNoSpareTime(double minutes)
        => Assert.Equal(
            SpareTimeVerdict.ReservationSoon,
            SpareTime.Judge(Idle with { NextReservationStartsAt = Noon.AddMinutes(minutes) }, Noon));

    [Fact(DisplayName = "a reservation starting later than thirty minutes away leaves spare time")]
    public void AReservationLaterThanThirtyMinutesLeavesSpareTime()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), SpareTime.NoReservationWithin);
        Assert.Equal(
            SpareTimeVerdict.Spare,
            SpareTime.Judge(Idle with { NextReservationStartsAt = Noon.AddMinutes(30).AddSeconds(1) }, Noon));
    }

    [Fact(DisplayName = "the moment judged is a UTC instant")]
    public void TheMomentJudgedIsAUtcInstant()
        => Assert.Throws<ArgumentException>(() => SpareTime.Judge(Idle, DateTime.SpecifyKind(Noon, DateTimeKind.Local)));
}
