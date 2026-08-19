using Microsoft.Extensions.Hosting;

namespace Carina.Api.Authentication;

public static class AnonymousSurfaces
{
    public static IReadOnlyList<AnonymousSurface> Everywhere { get; } =
    [
        AnonymousSurface.Exactly(HttpMethods.Get, "/api/health"),
        AnonymousSurface.Exactly(HttpMethods.Get, LoginRedirect.Path),
        AnonymousSurface.Exactly(HttpMethods.Get, LoginRedirect.LoggedOut),
        AnonymousSurface.Exactly(HttpMethods.Post, "/api/auth/login"),
        AnonymousSurface.Exactly(HttpMethods.Get, OidcHandshake.StartPath),
        AnonymousSurface.Exactly(HttpMethods.Get, OidcHandshake.CallbackPath),
        AnonymousSurface.Below(HttpMethods.Get, "/_next/static/"),
        AnonymousSurface.Exactly(HttpMethods.Get, "/favicon.ico"),
        AnonymousSurface.Exactly(HttpMethods.Get, "/manifest.webmanifest"),
    ];

    public static IReadOnlyList<AnonymousSurface> WhileDeveloping { get; } =
    [
        .. Everywhere,
        AnonymousSurface.Exactly(HttpMethods.Get, "/openapi/v1.json"),
    ];

    public static IReadOnlyList<AnonymousSurface> For(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);

        return environment.IsDevelopment() ? WhileDeveloping : Everywhere;
    }

    public static bool Admit(
        this IReadOnlyList<AnonymousSurface> surfaces,
        string method,
        string path)
    {
        ArgumentNullException.ThrowIfNull(surfaces);

        return surfaces.Any(surface => surface.Admits(method, path));
    }
}
