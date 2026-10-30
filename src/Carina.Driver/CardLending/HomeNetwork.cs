using System.Net;
using System.Net.Sockets;

namespace Carina.Driver.CardLending;

/// <summary>
/// The addresses a household's own network hands out and the internet does not route: loopback, the private ranges of
/// RFC 1918, unique local addresses (RFC 4193) and link-local addresses. The shared range carriers put in front of
/// many households (RFC 6598) is not one of them.
/// </summary>
public static class HomeNetwork
{
    private static readonly (byte[] Prefix, int Bits)[] Ipv4Ranges =
    [
        ([127, 0, 0, 0], 8),
        ([10, 0, 0, 0], 8),
        ([172, 16, 0, 0], 12),
        ([192, 168, 0, 0], 16),
        ([169, 254, 0, 0], 16),
    ];

    private static readonly (byte[] Prefix, int Bits)[] Ipv6Ranges =
    [
        ([0xFC, 0x00], 7),
        ([0xFE, 0x80], 10),
    ];

    public static bool Holds(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        IPAddress plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        return plain.AddressFamily switch
        {
            AddressFamily.InterNetwork => InAny(plain.GetAddressBytes(), Ipv4Ranges),
            AddressFamily.InterNetworkV6 => IPAddress.IPv6Loopback.Equals(plain) || InAny(plain.GetAddressBytes(), Ipv6Ranges),
            _ => false,
        };
    }

    private static bool InAny(byte[] address, (byte[] Prefix, int Bits)[] ranges) =>
        ranges.Any(range => Within(address, range.Prefix, range.Bits));

    private static bool Within(byte[] address, byte[] prefix, int bits)
    {
        for (int bit = 0; bit < bits; bit++)
        {
            int mask = 0x80 >> (bit % 8);
            if ((address[bit / 8] & mask) != (prefix[bit / 8] & mask))
            {
                return false;
            }
        }

        return true;
    }
}
