using System.Collections.Frozen;
using System.Net;

using Carina.Infrastructure.Net;

namespace Carina.Infrastructure.DataBroadcast;

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

        return addresses.Contains(PublicAddresses.Unmapped(address));
    }

    private static string Compared(string host)
        => WrittenAddress(host)?.ToString() ?? host.TrimEnd('.').ToLowerInvariant();

    private static IPAddress? WrittenAddress(string host)
        => IPAddress.TryParse(host.Trim('[', ']'), out IPAddress? written) ? PublicAddresses.Unmapped(written) : null;
}
