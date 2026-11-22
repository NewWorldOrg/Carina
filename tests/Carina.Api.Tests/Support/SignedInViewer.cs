using System.Security.Claims;

using Carina.Domain.Auth;

namespace Carina.Api.Tests.Support;

internal static class SignedInViewer
{
    public const string Name = "viewer";

    public static Subject Subject { get; } = new(Name);

    public static ClaimsPrincipal Principal()
        => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Name)], TestAuthenticationHandler.SchemeName));
}
