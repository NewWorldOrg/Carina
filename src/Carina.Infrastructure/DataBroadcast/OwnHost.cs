using System.Collections.Frozen;
using System.Net;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// The hosts a browser reaches this installation by, which the relay connects to by no scheme and on no port. A
/// name is compared as a resolver is asked for it, in ASCII, in lower case and without a trailing dot, so an address
/// that writes it in another case, with the dot or in Unicode is the same host. A host written as an address is also
/// refused wherever a name resolves to it; a host written as a name is not judged by its addresses, which it may
/// share with other servers.
/// </summary>
public sealed class OwnHost
{
    private readonly FrozenSet<string> names;

    private readonly FrozenSet<IPAddress> addresses;

    private OwnHost(FrozenSet<string> names, FrozenSet<IPAddress> addresses)
    {
        this.names = names;
        this.addresses = addresses;
    }

    public static OwnHost Unnamed { get; } = new(FrozenSet<string>.Empty, FrozenSet<IPAddress>.Empty);

    public static OwnHost Of(IEnumerable<Uri> origins)
    {
        ArgumentNullException.ThrowIfNull(origins);

        string[] hosts = [.. origins.Select(origin => origin.IdnHost)];

        return new OwnHost(
            hosts.Select(Compared).ToFrozenSet(StringComparer.Ordinal),
            hosts.Select(WrittenAddress).OfType<IPAddress>().ToFrozenSet());
    }

    public bool IsNamedBy(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.IsAbsoluteUri && IsNamedBy(address.IdnHost);
    }

    public bool IsNamedBy(string host)
    {
        ArgumentNullException.ThrowIfNull(host);

        return names.Contains(Compared(host));
    }

    public bool IsAt(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return addresses.Contains(RelayAddresses.Unmapped(address));
    }

    private static string Compared(string host)
        => WrittenAddress(host)?.ToString() ?? host.TrimEnd('.').ToLowerInvariant();

    private static IPAddress? WrittenAddress(string host)
        => IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? written) ? RelayAddresses.Unmapped(written) : null;
}
