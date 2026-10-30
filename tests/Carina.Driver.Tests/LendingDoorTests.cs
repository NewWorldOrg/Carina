using System.Net;

using Carina.Driver.CardLending;

namespace Carina.Driver.Tests;

public sealed class LendingDoorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 0, 0, 0, TimeSpan.Zero);

    private static readonly TimeSpan Life = TimeSpan.FromSeconds(2);

    private static readonly IPAddress Borrower = IPAddress.Parse("192.168.77.8");

    private static readonly IPAddress Neighbour = IPAddress.Parse("192.168.77.9");

    private static bool Admit(LendingDoor door, string connection, IPAddress address, out string? evicted) =>
        door.TryAdmit(connection, address, Now, out evicted);

    [Fact]
    public void ANewcomerFromAFullAddressTurnsAwayThatAddresssLongestWaiter()
    {
        LendingDoor door = new(unborrowed: 8, perAddress: 3);

        for (int connection = 0; connection < 3; connection++)
        {
            Assert.True(Admit(door, $"c{connection}", Borrower, out _));
        }

        Assert.True(Admit(door, "c3", Borrower, out string? evicted));
        Assert.Equal("c0", evicted);
        Assert.True(Admit(door, "n0", Neighbour, out evicted));
        Assert.Null(evicted);
    }

    [Fact]
    public void ANewcomerFromAFullSubnetTurnsAwayThatSubnetsLongestWaiter()
    {
        LendingDoor door = new(unborrowed: 8, perAddress: 3);
        Admit(door, "elsewhere", IPAddress.Parse("fd12:3456:789a:2::10"), out _);

        for (int connection = 0; connection < 3; connection++)
        {
            Admit(door, $"c{connection}", IPAddress.Parse($"fe80::{connection + 1}"), out _);
        }

        Assert.True(Admit(door, "c3", IPAddress.Parse("fe80::abcd:1"), out string? evicted));
        Assert.Equal("c0", evicted);
    }

    [Fact]
    public void AFullDoorTurnsAwayItsOldestWaiterRatherThanTheNewcomer()
    {
        LendingDoor door = new(unborrowed: 4, perAddress: 3);

        for (int connection = 0; connection < 4; connection++)
        {
            Assert.True(Admit(door, $"c{connection}", IPAddress.Parse($"192.168.77.{10 + connection}"), out string? none));
            Assert.Null(none);
        }

        Assert.True(Admit(door, "c4", IPAddress.Parse("192.168.77.20"), out string? evicted));
        Assert.Equal("c0", evicted);

        Assert.True(Admit(door, "c5", IPAddress.Parse("192.168.77.21"), out evicted));
        Assert.Equal("c1", evicted);
    }

    [Fact]
    public void AConnectionThatAnsweredWithTheKeyIsNeverTurnedAwayFromAFullDoor()
    {
        LendingDoor door = new(unborrowed: 2, perAddress: 3);
        Admit(door, "borrower", Borrower, out _);
        door.Proven("borrower");
        Admit(door, "c1", IPAddress.Parse("192.168.77.11"), out _);

        Assert.True(Admit(door, "c2", IPAddress.Parse("192.168.77.12"), out string? evicted));
        Assert.Equal("c1", evicted);
        Assert.True(Admit(door, "c3", IPAddress.Parse("192.168.77.13"), out evicted));
        Assert.Equal("c2", evicted);
    }

    [Fact]
    public void AConnectionThatAnsweredWithTheKeyIsNeverTurnedAwayFromAFullSubnet()
    {
        LendingDoor door = new(unborrowed: 8, perAddress: 2);
        Admit(door, "borrower", IPAddress.Parse("fe80::1"), out _);
        door.Proven("borrower");
        Admit(door, "c1", IPAddress.Parse("fe80::2"), out _);

        Assert.True(Admit(door, "c2", IPAddress.Parse("fe80::3"), out string? evicted));
        Assert.Equal("c1", evicted);
    }

    [Fact]
    public void ANewcomerWhenEveryoneWaitingHasTheKeyIsTheOneTurnedAway()
    {
        LendingDoor door = new(unborrowed: 1, perAddress: 1);
        Admit(door, "borrower", Borrower, out _);
        door.Proven("borrower");

        Assert.False(Admit(door, "c1", Neighbour, out string? evicted));
        Assert.Null(evicted);
    }

    [Fact]
    public void AConnectionThatBorrowedIsNeverTurnedAway()
    {
        LendingDoor door = new(unborrowed: 1, perAddress: 1);
        Admit(door, "c0", Borrower, out _);
        door.Borrowed("c0");
        Admit(door, "c1", Neighbour, out _);

        Assert.True(Admit(door, "c2", IPAddress.Parse("192.168.77.30"), out string? evicted));
        Assert.Equal("c1", evicted);
        Assert.True(door.HasBorrowed("c0"));
    }

    [Fact]
    public void AConnectionThatBorrowedNoLongerTakesASeatAtTheDoor()
    {
        LendingDoor door = new(unborrowed: 1, perAddress: 1);
        Admit(door, "c0", Borrower, out _);

        door.Borrowed("c0");

        Assert.True(Admit(door, "c1", Borrower, out string? evicted));
        Assert.Null(evicted);
        Assert.True(door.HasBorrowed("c0"));
        Assert.False(door.HasBorrowed("c1"));
    }

    [Fact]
    public void AConnectionThatLeftGivesItsSeatBack()
    {
        LendingDoor door = new(unborrowed: 1, perAddress: 1);
        Admit(door, "c0", Borrower, out _);

        door.Left("c0");

        Assert.True(Admit(door, "c1", Neighbour, out string? evicted));
        Assert.Null(evicted);
    }

    [Fact]
    public void AnIpv4AddressSeenThroughIpv6CountsAsTheSameAddress()
    {
        LendingDoor door = new(unborrowed: 8, perAddress: 1);
        Admit(door, "c0", Borrower, out _);

        Assert.True(Admit(door, "c1", Borrower.MapToIPv6(), out string? evicted));
        Assert.Equal("c0", evicted);
    }

    [Fact]
    public void Ipv6AddressesOfOneSubnetCountAsOneAddress()
    {
        LendingDoor door = new(unborrowed: 8, perAddress: 1);
        Admit(door, "c0", IPAddress.Parse("fd12:3456:789a:1::10"), out _);

        Assert.True(Admit(door, "c1", IPAddress.Parse("fd12:3456:789a:1:a:b:c:d"), out string? evicted));
        Assert.Equal("c0", evicted);
        Assert.True(Admit(door, "c2", IPAddress.Parse("fd12:3456:789a:2::10"), out evicted));
        Assert.Null(evicted);
    }

    [Fact]
    public void OnlyConnectionsThatHaveWaitedTooLongWithoutBorrowingAreOverdue()
    {
        LendingDoor door = new(unborrowed: 8, perAddress: 8);
        door.TryAdmit("waiting", Borrower, Now, out _);
        door.TryAdmit("borrowed", Borrower, Now, out _);
        door.Borrowed("borrowed");
        door.TryAdmit("late", Borrower, Now + TimeSpan.FromSeconds(1), out _);

        Assert.Empty(door.Overdue(Now + Life, Life));
        Assert.Equal(["waiting"], door.Overdue(Now + Life + TimeSpan.FromMilliseconds(1), Life));
    }

    [Fact]
    public void TheTimeToBorrowIsShort()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), CardLendingServer.UnborrowedLife);
    }
}
