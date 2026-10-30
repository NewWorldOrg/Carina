using System.Net;

namespace Carina.Driver.CardLending;

/// <summary>
/// Who may wait at the lending port without having borrowed: a few connections in all and fewer from any one address,
/// apart from the connections that hold a lease, so that something without the key cannot take the seats a borrower
/// needs.
/// </summary>
public sealed class LendingDoor(int unborrowed = LendingDoor.MostUnborrowed, int perAddress = LendingDoor.PerAddress)
{
    public const int MostUnborrowed = 8;

    public const int PerAddress = 3;

    private readonly Lock gate = new();

    private readonly Dictionary<string, Seat> seats = [];

    public bool TryAdmit(string connection, IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        IPAddress plain = address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        lock (gate)
        {
            IEnumerable<Seat> waiting = seats.Values.Where(seat => !seat.Borrowed);
            if (waiting.Count() >= unborrowed || waiting.Count(seat => seat.Address.Equals(plain)) >= perAddress)
            {
                return false;
            }

            seats[connection] = new Seat(plain);

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

    private sealed class Seat(IPAddress address)
    {
        public IPAddress Address { get; } = address;

        public bool Borrowed { get; set; }
    }
}
