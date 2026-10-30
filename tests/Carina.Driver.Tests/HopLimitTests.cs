using System.Net;
using System.Net.Sockets;

using Carina.Driver.CardLending;

namespace Carina.Driver.Tests;

public sealed class HopLimitTests
{
    [Fact]
    public void AnIpv4SocketCarriesTheLimitAsItsTimeToLive()
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        HopLimit.Apply(socket, 3);

        Assert.Equal(3, socket.Ttl);
        Assert.Equal(3, HopLimit.Of(socket));
    }

    [Fact]
    public void AnIpv6SocketCarriesTheLimitForBothFamiliesItMaySpeak()
    {
        using Socket socket = new(AddressFamily.InterNetworkV6, SocketType.Stream, ProtocolType.Tcp)
        {
            DualMode = true,
        };

        HopLimit.Apply(socket, 2);

        Assert.Equal(2, HopLimit.Of(socket));
        Assert.Equal(2, (int)socket.GetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive)!);
    }

    [Fact]
    public void AConnectionAcceptedOnALimitedListenerKeepsTheLimit()
    {
        using Socket listener = CardLendingServer.ListenSocket(new IPEndPoint(IPAddress.Loopback, 0), 2);
        listener.Listen();
        using Socket caller = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        caller.Connect(listener.LocalEndPoint!);

        using Socket accepted = listener.Accept();

        Assert.Equal(2, HopLimit.Of(accepted));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(HopLimit.Most + 1)]
    public void ALimitOutsideTheHouseholdRangeIsRefused(int hops)
    {
        using Socket socket = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        Assert.Throws<ArgumentOutOfRangeException>(() => HopLimit.Apply(socket, hops));
    }
}
