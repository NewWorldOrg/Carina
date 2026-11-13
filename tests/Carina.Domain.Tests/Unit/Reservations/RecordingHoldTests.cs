using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Tests.Recordings;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class RecordingHoldTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    [Fact(DisplayName = "BR-RS-001: a reservation being recorded holds its tuner until the latest end any of its recordings is promised")]
    public void AReservationHoldsItsTunerUntilTheLatestPromisedEnd()
    {
        var reservation = ReservationId.New();
        Recording earlier = Recorded(reservation, Now.AddMinutes(30), new TunerDeviceId("pt3-0"));
        Recording later = Recorded(reservation, Now.AddMinutes(90), new TunerDeviceId("pt3-1"));

        RecordingHold hold = Assert.Single(RecordingHold.Of([later, earlier])).Value;

        Assert.Equal(Now.AddMinutes(90), hold.Until);
        Assert.Equal(new TunerDeviceId("pt3-1"), hold.Tuner);
    }

    [Fact(DisplayName = "BR-RS-001: a later recording that names no tuner keeps the tuner the earlier one named")]
    public void ALaterRecordingThatNamesNoTunerKeepsTheEarlierTuner()
    {
        var reservation = ReservationId.New();
        Recording earlier = Recorded(reservation, Now.AddMinutes(30), new TunerDeviceId("pt3-0"));
        Recording later = Recorded(reservation, Now.AddMinutes(90), null);

        RecordingHold hold = RecordingHold.Of([earlier, later])[reservation];

        Assert.Equal(Now.AddMinutes(90), hold.Until);
        Assert.Equal(new TunerDeviceId("pt3-0"), hold.Tuner);
    }

    [Fact(DisplayName = "BR-RS-001: a recording made of no reservation holds nothing on behalf of one")]
    public void ARecordingOfNoReservationHoldsNothing()
        => Assert.Empty(RecordingHold.Of([RecordingFactory.Unclaimed()]));

    private static Recording Recorded(ReservationId reservation, DateTime until, TunerDeviceId? tuner)
    {
        var id = RecordingId.New();

        return Recording.Begin(
            id,
            reservation,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Now.AddMinutes(-5),
            until,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Now,
            tuner);
    }
}
