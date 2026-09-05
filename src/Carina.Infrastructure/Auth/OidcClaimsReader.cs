using System.Text.Json;

using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public static class OidcClaimsReader
{
    public const string OverflowedClaims = "_claim_names";

    public const string HostedDomainClaim = "hd";

    public const string EmailClaim = "email";

    public const string NameClaim = "name";

    public static OidcClaims? Read(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;

            if (root.ValueKind is not JsonValueKind.Object
                || Text(root, "iss") is not { Length: > 0 } issuer
                || Text(root, "sub") is not { Length: > 0 } subject
                || Seconds(root, "exp") is not { } expiresAt
                || Audiences(root) is not { Count: > 0 } audiences)
            {
                return null;
            }

            return new OidcClaims
            {
                Issuer = issuer,
                Audiences = audiences,
                Subject = subject,
                ExpiresAt = expiresAt,
                Nonce = Text(root, "nonce"),
                Groups = Strings(root, OidcClaims.GroupsClaim),
                GroupsOverflowed = Overflowed(root),
                HostedDomain = Text(root, HostedDomainClaim),
                Email = Text(root, EmailClaim),
                Name = Text(root, NameClaim),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Overflowed(JsonElement root)
        => root.TryGetProperty(OverflowedClaims, out JsonElement named)
           && named.ValueKind is JsonValueKind.Object
           && named.TryGetProperty(OidcClaims.GroupsClaim, out _);

    private static IReadOnlyList<string> Audiences(JsonElement root)
        => root.TryGetProperty("aud", out JsonElement found) && found.ValueKind is JsonValueKind.String
            ? [found.GetString()!]
            : Strings(root, "aud");

    private static IReadOnlyList<string> Strings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement found))
        {
            return [];
        }

        if (found.ValueKind is JsonValueKind.String)
        {
            return [found.GetString()!];
        }

        if (found.ValueKind is not JsonValueKind.Array)
        {
            return [];
        }

        return
        [
            .. found
                .EnumerateArray()
                .Where(entry => entry.ValueKind is JsonValueKind.String)
                .Select(entry => entry.GetString()!),
        ];
    }

    private static DateTime? Seconds(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement found)
           && found.ValueKind is JsonValueKind.Number
           && found.TryGetInt64(out long seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement found) && found.ValueKind is JsonValueKind.String
            ? found.GetString()
            : null;
}
