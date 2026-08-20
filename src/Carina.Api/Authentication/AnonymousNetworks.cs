using System.Net;

namespace Carina.Api.Authentication;

public sealed class AnonymousNetworks
{
    public const string Key = "CARINA_ANONYMOUS_NETWORKS";

    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    private AnonymousNetworks(IReadOnlyList<IPNetwork> networks) => Networks = networks;

    public IReadOnlyList<IPNetwork> Networks { get; }

    public bool NamesNothing => Networks.Count == 0;

    public static AnonymousNetworks Named(string? setting) => new([.. Entries(setting).Select(Network)]);

    public override string ToString()
        => NamesNothing
            ? "nothing"
            : string.Join(", ", Networks.Select(network => network.ToString()));

    private static IEnumerable<string> Entries(string? setting)
        => setting is null
            ? []
            : setting.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static IPNetwork Network(string entry)
        => IPNetwork.TryParse(entry, out IPNetwork parsed)
            ? parsed
            : throw new ArgumentException(
                $"{Key} names networks as address/prefix, and '{entry}' is not one.",
                Key);
}
