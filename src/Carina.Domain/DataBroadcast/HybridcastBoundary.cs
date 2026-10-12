using System.Net;

namespace Carina.Domain.DataBroadcast;

/// <summary>
/// The addresses a Hybridcast application may be relayed to. A managed address takes in its own host and every
/// host under it, on its own scheme and port, and where it names a path, that path and what is under it. A pair
/// that names no address would take in every address; the relay narrows that to the host of the entry alone, so an
/// application whose AIT leaves its boundary open reaches nothing beyond the server it was entered from. A WebSocket
/// address is judged as the http or https address it upgrades from.
/// </summary>
public sealed class HybridcastBoundary
{
    private readonly IReadOnlyList<Uri> managed;

    private readonly string? openToHost;

    private HybridcastBoundary(IReadOnlyList<Uri> managed, string? openToHost)
    {
        this.managed = managed;
        this.openToHost = openToHost;
    }

    public static HybridcastBoundary Of(Uri entry, IReadOnlyList<HybridcastPermission> pairs)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(pairs);

        bool open = pairs.Count == 0 || pairs.Any(pair => pair.ManagedUrls.Count == 0);
        List<Uri> managed = [.. pairs.SelectMany(pair => pair.ManagedUrls).Select(Managed).OfType<Uri>()];

        return new HybridcastBoundary(managed, open ? entry.IdnHost : null);
    }

    public bool Admits(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (!address.IsAbsoluteUri || WebScheme(address.Scheme) is not { } scheme)
        {
            return false;
        }

        string host = address.IdnHost;

        return string.Equals(host, openToHost, StringComparison.OrdinalIgnoreCase)
               || managed.Any(by => by.Scheme == scheme && by.Port == address.Port && IsWithin(host, by.IdnHost) && IsUnder(address.AbsolutePath, by.AbsolutePath));
    }

    private static Uri? Managed(string written)
        => Uri.TryCreate(written, UriKind.Absolute, out Uri? read) && (read.Scheme == Uri.UriSchemeHttp || read.Scheme == Uri.UriSchemeHttps)
            ? read
            : null;

    private static string? WebScheme(string scheme) => scheme switch
    {
        "http" or "ws" => Uri.UriSchemeHttp,
        "https" or "wss" => Uri.UriSchemeHttps,
        _ => null,
    };

    private static bool IsWithin(string host, string domain)
        => string.Equals(host, domain, StringComparison.OrdinalIgnoreCase)
           || (!IPAddress.TryParse(host.Trim('[', ']'), out _) && host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));

    private static bool IsUnder(string path, string directory)
        => directory == "/"
           || path == directory
           || path.StartsWith(directory.EndsWith('/') ? directory : directory + "/", StringComparison.Ordinal);
}
