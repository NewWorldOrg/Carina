using System.Net;

namespace Carina.Api.Authentication;

public sealed class TrustedProxyNetworks
{
    public const string SettingKey = "CARINA_TRUSTED_PROXY_NETWORKS";

    public const string SettingRequirement =
        "CARINA_TRUSTED_PROXY_NETWORKS must name, as comma separated CIDR networks, the reverse "
        + "proxy the app is reachable through. A network covering the whole address space is not "
        + "a trust boundary and is refused.";

    private readonly IReadOnlyList<IPNetwork> networks;

    private TrustedProxyNetworks(IReadOnlyList<IPNetwork> networks) => this.networks = networks;

    public static bool TryParse(string? setting, out TrustedProxyNetworks parsed)
    {
        parsed = new TrustedProxyNetworks([]);

        var entries = (setting ?? string.Empty).Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (entries.Length == 0)
        {
            return false;
        }

        var networks = new List<IPNetwork>(entries.Length);

        foreach (var entry in entries)
        {
            if (!IPNetwork.TryParse(entry, out var network) || network.PrefixLength == 0)
            {
                return false;
            }

            networks.Add(network);
        }

        parsed = new TrustedProxyNetworks(networks);

        return true;
    }

    public bool Admits(IPAddress? peer)
    {
        if (peer is null)
        {
            return false;
        }

        var address = peer.IsIPv4MappedToIPv6 ? peer.MapToIPv4() : peer;

        return networks.Any(network => network.Contains(address));
    }
}
