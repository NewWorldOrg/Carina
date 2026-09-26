using System.Security.Claims;

using Carina.Api.Authentication;
using Carina.Domain.Auth;

namespace Carina.Api.Tests.Unit;

public sealed class SessionClaimsTests
{
    private static readonly DateTime At = new(2026, 9, 26, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ThePrincipalNamesItsSessionByTheHashOfTheCookieAndNeverByTheCookie()
    {
        SessionId carried = SessionId.Issue();
        AuthSession session = AuthSession.Start(
            carried,
            new Subject("carina"),
            "carina",
            AuthMethod.Local,
            "a device",
            At);

        ClaimsPrincipal principal = SessionClaims.Principal(session, SessionAuthenticationHandler.SchemeName);

        Assert.DoesNotContain(
            principal.Claims,
            claim => claim.Value.Contains(carried.Value, StringComparison.Ordinal));
        Assert.Equal(SessionHandle.Of(carried).Value, principal.FindFirstValue(SessionClaims.Session));
    }
}
