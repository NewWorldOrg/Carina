using System.Net;
using System.Net.Sockets;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Which addresses a broadcaster's server may stand at: anything reachable across the internet, and nothing on
/// this machine, on the networks around it, or in a range kept for something else. An IPv6 address that carries an
/// IPv4 one is judged by the IPv4 one it carries.
/// </summary>
public static class RelayAddresses
{
    private static readonly AddressRange[] NotPublicV4 =
    [
        new([0, 0, 0, 0], 8),
        new([10, 0, 0, 0], 8),
        new([100, 64, 0, 0], 10),
        new([127, 0, 0, 0], 8),
        new([169, 254, 0, 0], 16),
        new([172, 16, 0, 0], 12),
        new([192, 0, 0, 0], 24),
        new([192, 168, 0, 0], 16),
        new([198, 18, 0, 0], 15),
        new([224, 0, 0, 0], 4),
        new([240, 0, 0, 0], 4),
    ];

    private static readonly AddressRange[] NotPublicV6 =
    [
        new(new byte[16], 96),
        new([0x01, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 64),
        new([0x20, 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 32),
        new([0x20, 0x01, 0x0D, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 32),
        new([0xFC, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 7),
        new([0xFE, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 10),
        new([0xFE, 0xC0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 10),
        new([0xFF, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 8),
    ];

    private static readonly AddressRange Nat64 = new([0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 96);

    private static readonly AddressRange SixToFour = new([0x20, 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], 16);

    public static IPAddress Unmapped(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
        {
            return IsPublic(address.MapToIPv4());
        }

        byte[] bytes = address.GetAddressBytes();

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => !NotPublicV4.Any(range => range.Holds(bytes)),
            AddressFamily.InterNetworkV6 => IsPublicV6(bytes),
            _ => false,
        };
    }

    private static bool IsPublicV6(byte[] bytes)
    {
        if (Nat64.Holds(bytes))
        {
            return IsPublic(new IPAddress(bytes[12..]));
        }

        if (SixToFour.Holds(bytes))
        {
            return IsPublic(new IPAddress(bytes[2..6]));
        }

        return !NotPublicV6.Any(range => range.Holds(bytes));
    }

    private sealed record AddressRange(byte[] Prefix, int Bits)
    {
        public bool Holds(byte[] address)
        {
            if (address.Length != Prefix.Length)
            {
                return false;
            }

            int whole = Bits / 8;
            int rest = Bits % 8;

            if (!address.AsSpan(0, whole).SequenceEqual(Prefix.AsSpan(0, whole)))
            {
                return false;
            }

            int mask = (0xFF << (8 - rest)) & 0xFF;

            return rest == 0 || (address[whole] & mask) == (Prefix[whole] & mask);
        }
    }
}
