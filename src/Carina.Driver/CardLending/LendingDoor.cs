using System.Net;
using System.Net.Sockets;

namespace Carina.Driver.CardLending;

/// <summary>
/// Who may wait at the lending port without having borrowed: a few connections in all and fewer from any one address,
/// an IPv6 subnet of /64 counting as one address, apart from the connections that hold a lease. A newcomer to a full
/// address or a full door turns away the connection there that has waited longest, so nothing without the key can keep
/// a borrower out; a connection that has answered with the key is never turned away.
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
    /// Seats the connection; <paramref name="evicted"/> names the connection that had to give up its seat for it, which
    /// the caller closes. It says no only when every connection it could turn away has answered with the key.
    /// </summary>
    public bool TryAdmit(string connection, IPAddress address, DateTimeOffset now, out string? evicted)
    {
        ArgumentNullException.ThrowIfNull(address);

        evicted = null;
        string area = AreaOf(address);

        lock (gate)
        {
            List<KeyValuePair<string, Seat>> waiting = [.. seats.Where(seat => !seat.Value.Borrowed)];
            List<KeyValuePair<string, Seat>> sameArea = [.. waiting.Where(seat => seat.Value.Area == area)];

            if (sameArea.Count >= perAddress && !TryTurnAway(sameArea, out evicted))
            {
                return false;
            }

            if (evicted is null && waiting.Count >= unborrowed && !TryTurnAway(waiting, out evicted))
            {
                return false;
            }

            seats[connection] = new Seat(area, ++arrivals, now);

            return true;
        }
    }

    /// <summary>
    /// Marks the connection as one that has answered a round trip with the key, so that it is never turned away.
    /// </summary>
    public void Proven(string connection)
    {
        lock (gate)
        {
            if (seats.TryGetValue(connection, out Seat? seat))
            {
                seat.Proven = true;
            }
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

    /// <summary>
    /// The connections that have waited longer than <paramref name="life"/> without borrowing.
    /// </summary>
    public IReadOnlyList<string> Overdue(DateTimeOffset now, TimeSpan life)
    {
        lock (gate)
        {
            return [.. seats.Where(seat => !seat.Value.Borrowed && now - seat.Value.Since > life).Select(seat => seat.Key)];
        }
    }

    public void Left(string connection)
    {
        lock (gate)
        {
            seats.Remove(connection);
        }
    }

    private bool TryTurnAway(List<KeyValuePair<string, Seat>> candidates, out string? evicted)
    {
        evicted = null;
        KeyValuePair<string, Seat>[] unproven = [.. candidates.Where(seat => !seat.Value.Proven)];
        if (unproven.Length is 0)
        {
            return false;
        }

        evicted = unproven.MinBy(seat => seat.Value.Arrival).Key;
        seats.Remove(evicted);

        return true;
    }

    private static string AreaOf(IPAddress address)
    {
        IPAddress plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        return plain.AddressFamily is AddressFamily.InterNetworkV6
            ? $"{Convert.ToHexString(plain.GetAddressBytes(), 0, SubnetBytes)}/64"
            : plain.ToString();
    }

    private sealed class Seat(string area, long arrival, DateTimeOffset since)
    {
        public string Area { get; } = area;

        public long Arrival { get; } = arrival;

        public DateTimeOffset Since { get; } = since;

        public bool Proven { get; set; }

        public bool Borrowed { get; set; }
    }
}
