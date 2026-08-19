namespace Carina.Domain.Auth;

public sealed record OidcCodeExchange(
    string ClientId,
    ClientSecret ClientSecret,
    string Code,
    string RedirectUri,
    string CodeVerifier);

public interface IOidcGateway
{
    Task<OidcEndpoints?> ReachAsync(string discoveryUrl, CancellationToken cancellationToken);

    Task<string?> ExchangeAsync(
        OidcEndpoints endpoints,
        OidcCodeExchange exchange,
        CancellationToken cancellationToken);

    Task<OidcClaims?> ReadAsync(
        OidcEndpoints endpoints,
        string idToken,
        CancellationToken cancellationToken);
}
