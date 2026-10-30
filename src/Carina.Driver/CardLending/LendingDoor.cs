using System.Net;
using System.Net.Sockets;

namespace Carina.Driver.CardLending;

/// <summary>
/// Who may wait at the lending port without having borrowed: a few connections in all and fewer from any one address,
/// an IPv6 subnet of /64 counting as one address, apart from the connections that hold a lease. A newcomer to a full
/// door turns away the connection that has waited longest, so nothing without the key can keep a borrower out.
/// </summary>
public sealed class LendingDoor(int unborrowed = LendingDoor.MostUnborrowed, int perAddress = LendingDoor.PerAddress)
{
    public const int MostUnborrowed = 8;

    public const int PerAddress = 3;

    private const int SubnetBytes = 8;

    private readonly Lock gate = new();

    private readonly Dictionary<string, Seat> seats = [];

    private long arrivals;

    /// <summary>
    /// Seats the connection, or says no when its address already has as many waiting as it may; <paramref name="evicted"/>
    /// names the connection that had to give up its seat for it, which the caller closes.
    /// </summary>
    public bool TryAdmit(string connection, IPAddress address, out string? evicted)
    {
        ArgumentNullException.ThrowIfNull(address);

        evicted = null;
        string area = AreaOf(address);

        lock (gate)
        {
            List<KeyValuePair<string, Seat>> waiting = [.. seats.Where(seat => !seat.Value.Borrowed)];
            if (waiting.Count(seat => seat.Value.Area == area) >= perAddress)
            {
                return false;
            }

            if (waiting.Count >= unborrowed)
            {
                evicted = waiting.MinBy(seat => seat.Value.Arrival).Key;
                seats.Remove(evicted);
            }

            seats[connection] = new Seat(area, ++arrivals);

            return true;
        }
    }

    public void Borrowed(string connection)
    {
        lock (gate)
        {
            if (seats.TryGetValue(connection, out Seat? seat))
            {
                seat.Borrowed = true;
            }
        }
    }

    public bool HasBorrowed(string connection)
    {
        lock (gate)
        {
            return seats.TryGetValue(connection, out Seat? seat) && seat.Borrowed;
        }
    }

    public void Left(string connection)
    {
        lock (gate)
        {
            seats.Remove(connection);
        }
    }

    private static string AreaOf(IPAddress address)
    {
        IPAddress plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        return plain.AddressFamily is AddressFamily.InterNetworkV6
            ? $"{Convert.ToHexString(plain.GetAddressBytes(), 0, SubnetBytes)}/64"
            : plain.ToString();
    }

    private sealed class Seat(string area, long arrival)
    {
        public string Area { get; } = area;

        public long Arrival { get; } = arrival;

        public bool Borrowed { get; set; }
    }
}
