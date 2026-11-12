using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Carina.TestSupport;

/// <summary>
/// A push service's reading of a <c>vapid</c> Authorization header (RFC 8292): the token's parts, and whether the
/// key the header names signed it.
/// </summary>
public sealed record VapidReading(JsonElement Header, JsonElement Claims, byte[] Key, bool Signed)
{
    private const string Scheme = "vapid ";

    public static VapidReading Of(string authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        if (!authorization.StartsWith(Scheme, StringComparison.Ordinal))
        {
            throw new FormatException("The header does not use the vapid scheme.");
        }

        Dictionary<string, string> parameters = authorization[Scheme.Length..]
            .Split(',', StringSplitOptions.TrimEntries)
            .Select(parameter => parameter.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

        return Of(parameters["t"], Base64Url.DecodeFromChars(parameters["k"]));
    }

    public static VapidReading Of(string token, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(key);

        string[] parts = token.Split('.');

        if (parts.Length is not 3 || key.Length is not 65 || key[0] is not 0x04)
        {
            throw new FormatException("The token is not three parts, or the key is not an uncompressed P-256 point.");
        }

        using ECDsa signer = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint { X = key[1..33], Y = key[33..65] },
        });

        bool signed = signer.VerifyData(
            Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
            Base64Url.DecodeFromChars(parts[2]),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return new VapidReading(Json(parts[0]), Json(parts[1]), key, signed);
    }

    private static JsonElement Json(string part)
    {
        using JsonDocument document = JsonDocument.Parse(Base64Url.DecodeFromChars(part));

        return document.RootElement.Clone();
    }
}
