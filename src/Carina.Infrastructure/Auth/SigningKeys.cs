using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;

namespace Carina.Infrastructure.Auth;

public sealed record SigningKey(string KeyType, string? KeyId, string? Algorithm, JsonElement Material);

public static class SigningKeys
{
    public const string Rsa = "RSA";

    public const string EllipticCurve = "EC";

    public static IReadOnlyList<SigningKey> Read(JsonElement document)
    {
        if (!document.TryGetProperty("keys", out JsonElement keys)
            || keys.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        var held = new List<SigningKey>();

        foreach (JsonElement key in keys.EnumerateArray())
        {
            if (Text(key, "kty") is { } type)
            {
                held.Add(new SigningKey(type, Text(key, "kid"), Text(key, "alg"), key.Clone()));
            }
        }

        return held;
    }

    public static bool Verifies(IReadOnlyList<SigningKey> keys, SignedToken token)
    {
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(token);

        if (HashFor(token.Algorithm) is not { } hash)
        {
            return false;
        }

        return keys
            .Where(key => token.KeyId is null || string.Equals(key.KeyId, token.KeyId, StringComparison.Ordinal))
            .Where(key => key.Algorithm is null || string.Equals(key.Algorithm, token.Algorithm, StringComparison.Ordinal))
            .Any(key => Verifies(key, token, hash));
    }

    private static bool Verifies(SigningKey key, SignedToken token, HashAlgorithmName hash)
        => token.Algorithm[0] switch
        {
            'R' when key.KeyType == Rsa => VerifiesWithRsa(key, token, hash),
            'E' when key.KeyType == EllipticCurve => VerifiesWithCurve(key, token, hash),
            _ => false,
        };

    private static bool VerifiesWithRsa(SigningKey key, SignedToken token, HashAlgorithmName hash)
    {
        if (Bytes(key.Material, "n") is not { } modulus || Bytes(key.Material, "e") is not { } exponent)
        {
            return false;
        }

        using RSA rsa = RSA.Create();

        try
        {
            rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = exponent });

            return rsa.VerifyData(token.Signed, token.Signature, hash, RSASignaturePadding.Pkcs1);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool VerifiesWithCurve(SigningKey key, SignedToken token, HashAlgorithmName hash)
    {
        if (Bytes(key.Material, "x") is not { } x
            || Bytes(key.Material, "y") is not { } y
            || CurveFor(Text(key.Material, "crv")) is not { } curve)
        {
            return false;
        }

        try
        {
            using ECDsa algorithm = ECDsa.Create(new ECParameters
            {
                Curve = curve,
                Q = new ECPoint { X = x, Y = y },
            });

            return algorithm.VerifyData(token.Signed, token.Signature, hash);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static HashAlgorithmName? HashFor(string algorithm)
        => algorithm switch
        {
            "RS256" or "ES256" => HashAlgorithmName.SHA256,
            "RS384" or "ES384" => HashAlgorithmName.SHA384,
            "RS512" or "ES512" => HashAlgorithmName.SHA512,
            _ => null,
        };

    private static ECCurve? CurveFor(string? name)
        => name switch
        {
            "P-256" => ECCurve.NamedCurves.nistP256,
            "P-384" => ECCurve.NamedCurves.nistP384,
            "P-521" => ECCurve.NamedCurves.nistP521,
            _ => null,
        };

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement found) && found.ValueKind is JsonValueKind.String
            ? found.GetString()
            : null;

    private static byte[]? Bytes(JsonElement element, string name)
    {
        if (Text(element, name) is not { } encoded)
        {
            return null;
        }

        try
        {
            return Base64Url.DecodeFromChars(encoded);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
