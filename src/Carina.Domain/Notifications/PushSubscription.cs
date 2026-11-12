using System.Buffers.Text;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Carina.Domain.Notifications;

/// <summary>
/// What a browser hands over to be sent notices: the endpoint of its push service and the two keys messages to it
/// are encrypted with. All three are secrets, read in the clear through <see cref="EndpointAsWritten"/>,
/// <see cref="KeyAsWritten"/> and <see cref="SecretAsWritten"/> only, to seal them and to send to them.
/// </summary>
public sealed class PushSubscription
{
    public const int LongestEndpoint = 2048;

    public const int KeyLength = 65;

    public const int SecretLength = 16;

    private const int HttpsPort = 443;

    private const int CoordinateLength = 32;

    private const byte Uncompressed = 0x04;

    private const string AnyWindowsHost = ".notify.windows.com";

    private static readonly IReadOnlySet<string> Hosts = new HashSet<string>(StringComparer.Ordinal)
    {
        "fcm.googleapis.com",
        "updates.push.services.mozilla.com",
        "web.push.apple.com",
    };

    private static readonly BigInteger FieldPrime = BigInteger.Parse(
        "00ffffffff00000001000000000000000000000000ffffffffffffffffffffffff",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);

    private static readonly BigInteger CurveConstant = BigInteger.Parse(
        "005ac635d8aa3a93e7b3ebbd55769886bc651d06b0cc53b0f63bce3c3e27d2604b",
        System.Globalization.NumberStyles.HexNumber,
        System.Globalization.CultureInfo.InvariantCulture);

    private PushSubscription(string endpoint, string key, string secret, string audience)
    {
        EndpointAsWritten = endpoint;
        KeyAsWritten = key;
        SecretAsWritten = secret;
        Audience = audience;
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint)));
        Tail = Fingerprint[^NotificationDestination.UrlTailLength..];
    }

    public string EndpointAsWritten { get; }

    public string KeyAsWritten { get; }

    public string SecretAsWritten { get; }

    /// <summary>
    /// The SHA-256 of the endpoint as written, in lowercase hex: what tells one browser from another without
    /// telling where it is reached.
    /// </summary>
    public string Fingerprint { get; }

    public string Tail { get; }

    /// <summary>
    /// The origin of the push service, which a message to it names as the one it is meant for.
    /// </summary>
    public string Audience { get; }

    /// <summary>
    /// The subscription as written, or nothing where the endpoint is not an https address of a push service this
    /// ledger sends to, the key is not an uncompressed point of P-256, or the secret is not 16 bytes.
    /// </summary>
    public static PushSubscription? Read(string? endpoint, string? key, string? secret)
    {
        if (string.IsNullOrEmpty(endpoint)
            || endpoint.Length > LongestEndpoint
            || !endpoint.All(IsPlain)
            || !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? read)
            || !IsToAPushService(read))
        {
            return null;
        }

        return IsAPoint(Decoded(key, KeyLength)) && Decoded(secret, SecretLength) is not null
            ? new PushSubscription(endpoint, key!, secret!, $"{Uri.UriSchemeHttps}{Uri.SchemeDelimiter}{read.IdnHost}")
            : null;
    }

    public override string ToString() => "(push subscription)";

    private static bool IsPlain(char letter) => letter is > ' ' and <= '~' and not '\\';

    private static bool IsToAPushService(Uri read)
        => read.Scheme == Uri.UriSchemeHttps
           && read.Port == HttpsPort
           && read.UserInfo.Length is 0
           && read.Fragment.Length is 0
           && IsAPushService(read.IdnHost);

    private static bool IsAPushService(string host)
        => Hosts.Contains(host)
           || (host.Length > AnyWindowsHost.Length && host.EndsWith(AnyWindowsHost, StringComparison.Ordinal));

    private static byte[]? Decoded(string? written, int length)
    {
        if (string.IsNullOrEmpty(written) || !written.All(IsOfTheAlphabet) || !Base64Url.IsValid(written))
        {
            return null;
        }

        byte[] decoded = Base64Url.DecodeFromChars(written);

        return decoded.Length == length ? decoded : null;
    }

    private static bool IsOfTheAlphabet(char letter) => char.IsAsciiLetterOrDigit(letter) || letter is '-' or '_';

    private static bool IsAPoint(byte[]? key)
    {
        if (key is null || key[0] != Uncompressed)
        {
            return false;
        }

        BigInteger x = new(key.AsSpan(1, CoordinateLength), isUnsigned: true, isBigEndian: true);
        BigInteger y = new(key.AsSpan(1 + CoordinateLength, CoordinateLength), isUnsigned: true, isBigEndian: true);

        if (x >= FieldPrime || y >= FieldPrime)
        {
            return false;
        }

        BigInteger left = y * y % FieldPrime;
        BigInteger right = (((x * x * x) - (3 * x) + CurveConstant) % FieldPrime + FieldPrime) % FieldPrime;

        return left == right;
    }
}
