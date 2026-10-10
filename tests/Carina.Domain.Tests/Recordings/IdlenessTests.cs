using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class IdlenessTests
{
    private static readonly DateTime Noon = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Busyness Quiet = new(false, false, null);

    [Fact(DisplayName = "BR-BS-001: the machine is idle when nothing is recorded or watched and no reservation is near")]
    public void TheMachineIsIdleWhenNothingStandsInTheWay()
        => Assert.Equal(IdleVerdict.Idle, Idleness.Judge(Quiet, Noon));

    [Fact(DisplayName = "BR-BS-001: a recording being written stands in the way before anybody watching does")]
    public void ARecordingBeingWrittenStandsInTheWayFirst()
    {
        Assert.Equal(IdleVerdict.Recording, Idleness.Judge(new Busyness(true, true, Noon), Noon));
        Assert.Equal(IdleVerdict.Watching, Idleness.Judge(Quiet with { Watching = true }, Noon));
    }

    [Theory(DisplayName = "BR-BS-001: a reservation that starts within thirty minutes, its margin included, stands in the way")]
    [InlineData(-10, IdleVerdict.ReservationSoon)]
    [InlineData(0, IdleVerdict.ReservationSoon)]
    [InlineData(30, IdleVerdict.ReservationSoon)]
    [InlineData(31, IdleVerdict.Idle)]
    public void AReservationWithinThirtyMinutesStandsInTheWay(int minutes, IdleVerdict verdict)
        => Assert.Equal(verdict, Idleness.Judge(Quiet with { NextReservationStartsAt = Noon.AddMinutes(minutes) }, Noon));

    [Fact]
    public void AMomentThatIsNotUtcIsRefused()
        => Assert.Throws<ArgumentException>(() => Idleness.Judge(Quiet, DateTime.SpecifyKind(Noon, DateTimeKind.Local)));
}
