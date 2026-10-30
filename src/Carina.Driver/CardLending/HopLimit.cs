using System.Net.Sockets;

namespace Carina.Driver.CardLending;

/// <summary>
/// How many routers a packet of card lending may cross before it is dropped: the IPv4 time to live and the IPv6 hop
/// limit, kept small so that what is sent does not reach past the household.
/// </summary>
public static class HopLimit
{
    public const int Least = 1;

    public const int Most = 8;

    public static void Apply(Socket socket, int hops)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentOutOfRangeException.ThrowIfLessThan(hops, Least);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hops, Most);

        if (socket.AddressFamily is AddressFamily.InterNetworkV6)
        {
            socket.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IpTimeToLive, hops);

            if (socket.DualMode)
            {
                socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, hops);
            }

            return;
        }

        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, hops);
    }

    public static int Of(Socket socket)
    {
        ArgumentNullException.ThrowIfNull(socket);

        SocketOptionLevel level = socket.AddressFamily is AddressFamily.InterNetworkV6
            ? SocketOptionLevel.IPv6
            : SocketOptionLevel.IP;

        return (int)socket.GetSocketOption(level, SocketOptionName.IpTimeToLive)!;
    }
}
