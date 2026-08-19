using System.Buffers.Text;
using System.Text;
using System.Text.Json;

namespace Carina.Infrastructure.Auth;

public sealed class SignedToken
{
    private SignedToken(string algorithm, string? keyId, byte[] signed, byte[] signature, byte[] payload)
    {
        Algorithm = algorithm;
        KeyId = keyId;
        Signed = signed;
        Signature = signature;
        Payload = payload;
    }

    public string Algorithm { get; }

    public string? KeyId { get; }

    public byte[] Signed { get; }

    public byte[] Signature { get; }

    public byte[] Payload { get; }

    public static SignedToken? Read(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        string[] parts = token.Split('.');

        if (parts.Length != 3 || parts.Any(part => part.Length == 0))
        {
            return null;
        }

        try
        {
            using JsonDocument header = JsonDocument.Parse(Base64Url.DecodeFromChars(parts[0]));

            if (Text(header.RootElement, "alg") is not { } algorithm)
            {
                return null;
            }

            return new SignedToken(
                algorithm,
                Text(header.RootElement, "kid"),
                Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
                Base64Url.DecodeFromChars(parts[2]),
                Base64Url.DecodeFromChars(parts[1]));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement found) && found.ValueKind is JsonValueKind.String
            ? found.GetString()
            : null;
}
