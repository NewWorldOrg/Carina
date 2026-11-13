using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Tests.Unit.Recordings;

using static Carina.Domain.Tests.Unit.Reservations.ReservationFactory;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class RecordedInsteadTests
{
    [Fact(DisplayName = "BR-RD-012: a reservation lost to competition names the claimed reservations whose windows met its own, earliest first")]
    public void ALostReservationNamesTheClaimsThatMetItsWindow()
    {
        Reservation lost = Planned(programme: Programme(4001, Now.AddHours(2)));
        Reservation later = Held(4002, Now.AddHours(2).AddMinutes(30));
        Reservation earlier = Held(4003, Now.AddHours(1).AddMinutes(30));
        Reservation apart = Held(4004, Now.AddHours(3));
        Reservation touching = Held(4005, Now.AddHours(1));

        Assert.Equal(
            [earlier.Id.Value, later.Id.Value],
            RecordedInstead.Of(lost, [apart, later, touching, earlier]));
    }

    [Fact(DisplayName = "BR-RD-012: the claims looked for reach as far as the longest margin either side of every reservation lost")]
    public void TheClaimsAreLookedForAcrossEveryLostWindow()
    {
        Reservation first = Planned(programme: Programme(4001, Now.AddHours(2)));
        Reservation last = Planned(programme: Programme(4002, Now.AddHours(5)));

        ReservationWindow window = RecordedInstead.LookedForAcross([last, first]);

        Assert.Equal(first.EffectiveStartAt - Margin.Longest, window.From);
        Assert.Equal(last.EffectiveEndAt + Margin.Longest, window.To);
    }

    [Fact(DisplayName = "BR-RD-012: a start the tuners refused names every recording in flight that was made of a reservation")]
    public void ARefusedStartNamesEveryRecordingInFlightOfAReservation()
    {
        var first = ReservationId.New();
        var second = ReservationId.New();
        Recording one = RecordingFactory.Started(reservationId: first);
        Recording unclaimed = RecordingFactory.Unclaimed();
        Recording two = RecordingFactory.Started(reservationId: second);

        Assert.Equal([first.Value, second.Value], RecordedInstead.AtTheRefusal([one, unclaimed, two]));
    }

    private static Reservation Held(int eventId, DateTime startsAt)
        => Planned(programme: Programme(eventId, startsAt));
}
