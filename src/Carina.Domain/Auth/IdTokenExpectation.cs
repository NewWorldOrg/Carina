using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed record IdTokenExpectation(string Issuer, string ClientId, string Nonce)
{
    public OidcRefusal Refuses(OidcClaims claims, DateTime now, TimeSpan clockSkew)
    {
        ArgumentNullException.ThrowIfNull(claims);
        UtcTimes.Required(now, nameof(now));
        ArgumentOutOfRangeException.ThrowIfLessThan(clockSkew, TimeSpan.Zero);

        if (!string.Equals(claims.Issuer, Issuer, StringComparison.Ordinal))
        {
            return OidcRefusal.TheIssuerIsNotTheOneConfigured;
        }

        if (!claims.Audiences.Contains(ClientId, StringComparer.Ordinal))
        {
            return OidcRefusal.TheTokenWasIssuedForSomebodyElse;
        }

        if (now > claims.ExpiresAt + clockSkew)
        {
            return OidcRefusal.TheIdTokenExpired;
        }

        if (!Unguessable.Same(Nonce, claims.Nonce))
        {
            return OidcRefusal.TheNonceIsNotTheOneIssued;
        }

        return OidcRefusal.None;
    }
}
