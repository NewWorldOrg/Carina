using System.Net;

namespace Carina.Infrastructure.DataBroadcast;

/// <summary>
/// Which addresses a data broadcast document may be relayed to, read from the address alone: http or https, on
/// port 80 or 443, with no user named in it, and where the host is written as an address, a public one. A host
/// written as a name is judged again by the addresses it resolves to when the connection is opened.
/// </summary>
public static class RelayDestination
{
    public const int LongestAddress = 4096;

    public const int HttpPort = 80;

    public const int HttpsPort = 443;

    public static Uri? Admit(string? address)
    {
        if (string.IsNullOrEmpty(address)
            || address.Length > LongestAddress
            || !Uri.TryCreate(address, UriKind.Absolute, out Uri? read))
        {
            return null;
        }

        return Admits(read) ? read : null;
    }

    public static bool Admits(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return address.IsAbsoluteUri
               && address.AbsoluteUri.Length <= LongestAddress
               && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps)
               && address.Port is HttpPort or HttpsPort
               && address.UserInfo.Length == 0
               && address.IdnHost.Length > 0
               && WrittenAddressIsPublic(address);
    }

    private static bool WrittenAddressIsPublic(Uri address)
        => !IPAddress.TryParse(address.DnsSafeHost, out IPAddress? written) || RelayAddresses.IsPublic(written);
}
