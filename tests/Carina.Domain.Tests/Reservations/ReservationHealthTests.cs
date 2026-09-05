using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationHealthTests
{
    private static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ACountOfReservationsIsNeverNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReservationHealth(Noon, -1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReservationHealth(Noon, 0, -1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReservationHealth(Noon, 0, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReservationHealth(Noon, 0, 0, 0, -1));
    }

    [Fact]
    public void TheMomentTheCountsWereTakenAtIsAUtcInstant()
    {
        Assert.Throws<ArgumentException>(
            () => new ReservationHealth(new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Local), 0, 0, 0, 0));
    }

    [Fact]
    public void AClearBillOfHealthCountsNothingAndSaysWhen()
    {
        ReservationHealth clear = ReservationHealth.Clear(Noon);

        Assert.Equal(Noon, clear.AsOf);
        Assert.Equal(0, clear.Contended + clear.ReceptionUnavailable + clear.EpgDiverged + clear.EpgMissing);
    }
}
