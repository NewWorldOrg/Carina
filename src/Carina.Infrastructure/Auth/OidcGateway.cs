using System.Text.Json;

using Carina.Domain.Auth;

namespace Carina.Infrastructure.Auth;

public sealed class OidcGateway(HttpClient client) : IOidcGateway
{
    public const string GrantType = "authorization_code";

    public async Task<OidcEndpoints?> ReachAsync(string discoveryUrl, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(discoveryUrl);

        using JsonDocument? document = await FetchAsync(new Uri(discoveryUrl), cancellationToken);

        if (document is null || document.RootElement.ValueKind is not JsonValueKind.Object)
        {
            return null;
        }

        try
        {
            return OidcEndpoints.Of(
                Text(document.RootElement, "issuer") ?? string.Empty,
                Text(document.RootElement, "authorization_endpoint") ?? string.Empty,
                Text(document.RootElement, "token_endpoint") ?? string.Empty,
                Text(document.RootElement, "jwks_uri") ?? string.Empty);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public async Task<string?> ExchangeAsync(
        OidcEndpoints endpoints,
        OidcCodeExchange exchange,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(exchange);

        using var body = new FormUrlEncodedContent(
        [
            new KeyValuePair<string, string>("grant_type", GrantType),
            new KeyValuePair<string, string>("code", exchange.Code),
            new KeyValuePair<string, string>("redirect_uri", exchange.RedirectUri),
            new KeyValuePair<string, string>("client_id", exchange.ClientId),
            new KeyValuePair<string, string>("client_secret", exchange.ClientSecret.Value),
            new KeyValuePair<string, string>("code_verifier", exchange.CodeVerifier),
        ]);

        try
        {
            using HttpResponseMessage response = await client.PostAsync(endpoints.Token, body, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using JsonDocument document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);

            return document.RootElement.ValueKind is JsonValueKind.Object
                ? Text(document.RootElement, "id_token")
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    public async Task<OidcClaims?> ReadAsync(
        OidcEndpoints endpoints,
        string idToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        if (SignedToken.Read(idToken) is not { } token)
        {
            return null;
        }

        using JsonDocument? jwks = await FetchAsync(endpoints.Jwks, cancellationToken);

        if (jwks is null || !SigningKeys.Verifies(SigningKeys.Read(jwks.RootElement), token))
        {
            return null;
        }

        return OidcClaimsReader.Read(token.Payload);
    }

    private async Task<JsonDocument?> FetchAsync(Uri address, CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await client.GetAsync(address, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static string? Text(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement found) && found.ValueKind is JsonValueKind.String
            ? found.GetString()
            : null;
}
