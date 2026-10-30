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
            Assert.True(door.TryAdmit($"c{connection}", Borrower));
        }

        Assert.False(door.TryAdmit("c3", Borrower));
        Assert.True(door.TryAdmit("n0", Neighbour));
    }

    [Fact]
    public void ConnectionsThatHaveNotBorrowedHaveALimitOfTheirOwn()
    {
        LendingDoor door = new(unborrowed: 4, perAddress: 3);

        for (int connection = 0; connection < 4; connection++)
        {
            Assert.True(door.TryAdmit($"c{connection}", IPAddress.Parse($"192.168.77.{10 + connection}")));
        }

        Assert.False(door.TryAdmit("c4", IPAddress.Parse("192.168.77.20")));
    }

    [Fact]
    public void AConnectionThatBorrowedNoLongerTakesASeatAtTheDoor()
    {
        LendingDoor door = new(unborrowed: 1, perAddress: 1);
        door.TryAdmit("c0", Borrower);

        door.Borrowed("c0");

        Assert.True(door.TryAdmit("c1", Borrower));
        Assert.True(door.HasBorrowed("c0"));
        Assert.False(door.HasBorrowed("c1"));
    }

    [Fact]
    public void AConnectionThatLeftGivesItsSeatBack()
    {
        LendingDoor door = new(unborrowed: 1, perAddress: 1);
        door.TryAdmit("c0", Borrower);

        door.Left("c0");

        Assert.True(door.TryAdmit("c1", Borrower));
    }

    [Fact]
    public void AnIpv4AddressSeenThroughIpv6CountsAsTheSameAddress()
    {
        LendingDoor door = new(unborrowed: 8, perAddress: 1);
        door.TryAdmit("c0", Borrower);

        Assert.False(door.TryAdmit("c1", Borrower.MapToIPv6()));
    }
}
