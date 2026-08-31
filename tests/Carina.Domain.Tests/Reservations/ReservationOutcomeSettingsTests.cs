using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationOutcomeSettingsTests
{
    [Fact]
    public void HowLongARecordingMayBeLateIsSetApartFromHowLongARuleLeavesAReservationAlone()
    {
        var rules = new RuleApplicationSettings { Grace = TimeSpan.FromMinutes(42) };
        var outcomes = new ReservationOutcomeSettings { Grace = TimeSpan.FromMinutes(7) };

        Assert.Equal(TimeSpan.FromMinutes(42), rules.Grace);
        Assert.Equal(TimeSpan.FromMinutes(7), outcomes.Grace);
        Assert.Equal(ReservationOutcomeSettings.DefaultGrace, new ReservationOutcomeSettings().Grace);
    }

    [Fact]
    public void TheDefaultLeavesRoomForTheLongestWayARecordingHasToItsFirstByte()
        => Assert.True(ReservationOutcomeSettings.DefaultGrace > TimeSpan.FromMinutes(1));
}
