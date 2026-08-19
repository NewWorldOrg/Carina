using Carina.Api.Authentication;

using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.Unit;

public sealed class AnonymousSurfaceTests
{
    [Theory]
    [InlineData("GET", "/api/health")]
    [InlineData("get", "/api/health")]
    [InlineData("GET", "/api/health/")]
    [InlineData("GET", "/API/Health")]
    [InlineData("GET", "/login")]
    [InlineData("GET", "/logged-out")]
    [InlineData("POST", "/api/auth/login")]
    [InlineData("GET", "/api/auth/sign-in-options")]
    [InlineData("GET", "/api/auth/oidc/start")]
    [InlineData("GET", "/api/auth/oidc/callback")]
    [InlineData("GET", "/favicon.ico")]
    [InlineData("GET", "/manifest.webmanifest")]
    [InlineData("GET", "/_next/static/chunks/main-0f3a.js")]
    public void TheEnumeratedSurfacesAreAdmitted(string method, string path)
    {
        Assert.True(AnonymousSurfaces.Everywhere.Admit(method, path));
    }

    [Theory]
    [InlineData("POST", "/api/health")]
    [InlineData("GET", "/api/health/detail")]
    [InlineData("GET", "/api/tuners")]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("POST", "/login")]
    [InlineData("GET", "/_next/staticbait")]
    [InlineData("GET", "/thumbnails/1.jpg")]
    [InlineData("GET", "/logos/1.png")]
    [InlineData("GET", "/openapi/v1.json")]
    [InlineData("GET", "/")]
    public void EverythingElseIsLeftToTheDefaultDenial(string method, string path)
    {
        Assert.False(AnonymousSurfaces.Everywhere.Admit(method, path));
    }

    [Fact]
    public void TheListIsTheWholeOfWhatMayBeReachedWithoutCredentials()
    {
        Assert.Equal(
            [
                "GET /_next/static/",
                "GET /api/auth/oidc/callback",
                "GET /api/auth/oidc/start",
                "GET /api/auth/sign-in-options",
                "GET /api/health",
                "GET /favicon.ico",
                "GET /logged-out",
                "GET /login",
                "GET /manifest.webmanifest",
                "POST /api/auth/login",
            ],
            AnonymousSurfaces.Everywhere
                .Select(surface => $"{surface.Method} {surface.Path}")
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void TheServedDocumentIsReachedWithoutCredentialsOnlyWhileDeveloping()
    {
        Assert.True(AnonymousSurfaces.WhileDeveloping.Admit("GET", "/openapi/v1.json"));
        Assert.False(AnonymousSurfaces.Everywhere.Admit("GET", "/openapi/v1.json"));
        Assert.Equal(AnonymousSurfaces.Everywhere.Count + 1, AnonymousSurfaces.WhileDeveloping.Count);
    }

    [Fact]
    public void TheEnvironmentDecidesWhichListIsInForce()
    {
        Assert.Same(
            AnonymousSurfaces.WhileDeveloping,
            AnonymousSurfaces.For(new StubEnvironment(Environments.Development)));
        Assert.Same(
            AnonymousSurfaces.Everywhere,
            AnonymousSurfaces.For(new StubEnvironment(Environments.Production)));
    }

    [Fact]
    public void ASurfaceBelowADirectoryNamesTheDirectory()
    {
        Assert.Throws<ArgumentException>(() => AnonymousSurface.Below("GET", "/_next/static"));
    }
}
