namespace Carina.Domain.Auth;

public sealed class OidcEndpoints
{
    private OidcEndpoints(string issuer, Uri authorization, Uri token, Uri jwks)
    {
        Issuer = issuer;
        Authorization = authorization;
        Token = token;
        Jwks = jwks;
    }

    public string Issuer { get; }

    public Uri Authorization { get; }

    public Uri Token { get; }

    public Uri Jwks { get; }

    public static OidcEndpoints Of(string issuer, string authorization, string token, string jwks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);

        return new OidcEndpoints(
            issuer.Trim(),
            Secure(authorization, nameof(authorization)),
            Secure(token, nameof(token)),
            Secure(jwks, nameof(jwks)));
    }

    private static Uri Secure(string address, string name)
    {
        ArgumentNullException.ThrowIfNull(address, name);

        if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException(
                "An identity provider is reached over https, and anything else would put the handshake on the wire in the clear.",
                name);
        }

        return parsed;
    }
}
