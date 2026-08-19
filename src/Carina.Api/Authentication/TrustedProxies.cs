using System.Net;

namespace Carina.Api.Authentication;

public sealed class TrustedProxies
{
    public const string ProxiesKey = "CARINA_KNOWN_PROXIES";

    public const string NetworksKey = "CARINA_KNOWN_NETWORKS";

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    private TrustedProxies(IReadOnlyList<IPAddress> proxies, IReadOnlyList<IPNetwork> networks)
    {
        Proxies = proxies;
        Networks = networks;
    }

    public IReadOnlyList<IPAddress> Proxies { get; }

    public IReadOnlyList<IPNetwork> Networks { get; }

    public bool TrustsNothing => Proxies.Count == 0 && Networks.Count == 0;

    public static TrustedProxies Named(string? proxies, string? networks)
        => new(
            [.. Entries(proxies).Select(Address)],
            [.. Entries(networks).Select(Network)]);

    public override string ToString()
        => TrustsNothing
            ? "nothing"
            : string.Join(
                ", ",
                Proxies.Select(proxy => proxy.ToString()).Concat(Networks.Select(network => network.ToString())));

    private static IEnumerable<string> Entries(string? setting)
        => setting is null
            ? []
            : setting.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IPAddress Address(string entry)
        => IPAddress.TryParse(entry, out IPAddress? parsed)
            ? parsed
            : throw new ArgumentException(
                $"{ProxiesKey} names the addresses requests arrive from, and '{entry}' is not one. "
                + "Write addresses without a prefix length; a range belongs in "
                + $"{NetworksKey} as address/prefix.",
                ProxiesKey);

    private static IPNetwork Network(string entry)
        => IPNetwork.TryParse(entry, out IPNetwork parsed)
            ? parsed
            : throw new ArgumentException(
                $"{NetworksKey} names the networks requests arrive from as address/prefix, and '{entry}' is not one.",
                NetworksKey);
}
