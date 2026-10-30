using System.Net;

using Carina.Driver.CardLending;

namespace Carina.Driver.Tests;

public sealed class LendingDoorTests
{
    private static readonly IPAddress Borrower = IPAddress.Parse("192.168.77.8");

    private static readonly IPAddress Neighbour = IPAddress.Parse("192.168.77.9");

    [Fact]
    public void OneAddressHoldsOnlySoManyConnectionsThatHaveNotBorrowed()
    {
        LendingDoor door = new(unborrowed: 8, perAddress: 3);

        for (int connection = 0; connection < 3; connection++)
        {
            Assert.True(door.TryAdmit($"c{connection}", Borrower, out _));
        }

        Assert.False(door.TryAdmit("c3", Borrower, out _));
        Assert.True(door.TryAdmit("n0", Neighbour, out _));
    }

    [Fact]
    public void AFullDoorTurnsAwayItsOldestWaiterRatherThanTheNewcomer()
    {
        LendingDoor door = new(unborrowed: 4, perAddress: 3);

        for (int connection = 0; connection < 4; connection++)
        {
            Assert.True(door.TryAdmit($"c{connection}", IPAddress.Parse($"192.168.77.{10 + connection}"), out string? none));
            Assert.Null(none);
        }

        Assert.True(door.TryAdmit("c4", IPAddress.Parse("192.168.77.20"), out string? evicted));
        Assert.Equal("c0", evicted);

        Assert.True(door.TryAdmit("c5", IPAddress.Parse("192.168.77.21"), out evicted));
        Assert.Equal("c1", evicted);
    }

    [Fact]
    public void AConnectionThatBorrowedIsNeverTurnedAway()
    {
        LendingDoor door = new(unborrowed: 1, perAddress: 1);
        door.TryAdmit("c0", Borrower, out _);
        door.Borrowed("c0");
        door.TryAdmit("c1", Neighbour, out _);

        Assert.True(door.TryAdmit("c2", IPAddress.Parse("192.168.77.30"), out string? evicted));
        Assert.Equal("c1", evicted);
        Assert.True(door.HasBorrowed("c0"));
    }

    [Fact]
    public void AConnectionThatBorrowedNoLongerTakesASeatAtTheDoor()
    {
        LendingDoor door = new(unborrowed: 1, perAddress: 1);
        door.TryAdmit("c0", Borrower, out _);

        door.Borrowed("c0");

        Assert.True(door.TryAdmit("c1", Borrower, out string? evicted));
        Assert.Null(evicted);
        Assert.True(door.HasBorrowed("c0"));
        Assert.False(door.HasBorrowed("c1"));
    }

    [Fact]
    public void AConnectionThatLeftGivesItsSeatBack()
    {
        LendingDoor door = new(unborrowed: 1, perAddress: 1);
        door.TryAdmit("c0", Borrower, out _);

        door.Left("c0");

        Assert.True(door.TryAdmit("c1", Borrower, out string? evicted));
        Assert.Null(evicted);
    }

    [Fact]
    public void AnIpv4AddressSeenThroughIpv6CountsAsTheSameAddress()
    {
        LendingDoor door = new(unborrowed: 8, perAddress: 1);
        door.TryAdmit("c0", Borrower, out _);

        Assert.False(door.TryAdmit("c1", Borrower.MapToIPv6(), out _));
    }

    [Fact]
    public void Ipv6AddressesOfOneSubnetCountAsOneAddress()
    {
        LendingDoor door = new(unborrowed: 8, perAddress: 1);
        door.TryAdmit("c0", IPAddress.Parse("fd12:3456:789a:1::10"), out _);

        Assert.False(door.TryAdmit("c1", IPAddress.Parse("fd12:3456:789a:1:a:b:c:d"), out _));
        Assert.True(door.TryAdmit("c2", IPAddress.Parse("fd12:3456:789a:2::10"), out _));
    }

    [Fact]
    public void TheTimeToBorrowIsShort()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), CardLendingServer.UnborrowedLife);
    }
}
