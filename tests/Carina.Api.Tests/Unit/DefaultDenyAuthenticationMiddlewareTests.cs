using System.Security.Claims;

using Carina.Api.Authentication;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.Unit;

public sealed class DefaultDenyAuthenticationMiddlewareTests
{
    private const string Subject = "tester";

    private static readonly string[] EdgeIdentityHeaders =
    [
        "X-Forwarded-User",
        "X-Forwarded-Email",
        "X-Forwarded-Preferred-Username",
        "X-Forwarded-Groups",
        "X-Auth-Request-User",
        "X-Auth-Request-Email",
    ];

    [Fact]
    public async Task ARequestWithoutCredentialsIsRefusedAndReachesNothingBehindIt()
    {
        DefaultHttpContext context = Asking("GET", "/api/tuners");

        bool reached = await RunAsync(context);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AHeaderNamingAUserIsNotACredential()
    {
        HttpContext context = Spoofed(Asking("GET", "/api/tuners"));

        bool reached = await RunAsync(context);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task AHeaderNamingAUserDoesNotChangeWhoTheRequestIs()
    {
        HttpContext context = Spoofed(Authenticated(Asking("GET", "/api/tuners")));
        string? seen = null;

        bool reached = await RunAsync(context, admitted => seen = admitted.User.Identity?.Name);

        Assert.True(reached);
        Assert.Equal(Subject, seen);
    }

    [Fact]
    public async Task AnEnumeratedSurfaceIsReachedWithoutCredentials()
    {
        DefaultHttpContext context = Asking("GET", "/api/health");

        bool reached = await RunAsync(context);

        Assert.True(reached);
    }

    [Fact]
    public async Task AScreenRequestIsSentToTheLoginScreenCarryingWhereItWasGoing()
    {
        DefaultHttpContext context = Asking("GET", "/programs", accept: "text/html,application/xhtml+xml");
        context.Request.QueryString = new QueryString("?type=terrestrial");

        bool reached = await RunAsync(context);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status302Found, context.Response.StatusCode);
        Assert.Equal(
            "/login?next=%2Fprograms%3Ftype%3Dterrestrial",
            context.Response.Headers.Location.ToString());
    }

    [Theory]
    [InlineData("/api/tuners", "text/html")]
    [InlineData("/api/events", "text/event-stream")]
    [InlineData("/recordings/1.ts", "*/*")]
    [InlineData("/recordings/1.ts", "video/mp2t")]
    public async Task ARequestThatIsNotAScreenIsRefusedRatherThanRedirected(string path, string accept)
    {
        DefaultHttpContext context = Asking("GET", path, accept);

        bool reached = await RunAsync(context);

        Assert.False(reached);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(context.Response.Headers.ContainsKey("Location"));
    }

    [Fact]
    public async Task NothingIsRefusedWhileNoSchemeCanSatisfyTheRefusal()
    {
        DefaultHttpContext context = Asking("GET", "/api/tuners");

        bool reached = await RunAsync(context, schemes: WithoutASchemeRegistered());

        Assert.True(reached);
    }

    private static DefaultHttpContext Asking(string method, string path, string accept = "application/json")
    {
        var context = new DefaultHttpContext();

        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers.Accept = accept;

        return context;
    }

    private static HttpContext Spoofed(HttpContext context)
    {
        foreach (string header in EdgeIdentityHeaders)
        {
            context.Request.Headers[header] = "someone-else";
        }

        return context;
    }

    private static HttpContext Authenticated(HttpContext context)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, Subject)], "Test");

        context.User = new ClaimsPrincipal(identity);

        return context;
    }

    private static async Task<bool> RunAsync(
        HttpContext context,
        Action<HttpContext>? behind = null,
        IAuthenticationSchemeProvider? schemes = null)
    {
        bool reached = false;
        var middleware = new DefaultDenyAuthenticationMiddleware(
            admitted =>
            {
                reached = true;
                behind?.Invoke(admitted);

                return Task.CompletedTask;
            },
            new StubEnvironment(Environments.Production));

        await middleware.InvokeAsync(context, schemes ?? WithASchemeRegistered());

        return reached;
    }

    private static IAuthenticationSchemeProvider WithASchemeRegistered()
    {
        AuthenticationSchemeProvider schemes = WithoutASchemeRegistered();

        schemes.AddScheme(new AuthenticationScheme(
            "Test",
            "Test",
            typeof(StubAuthenticationHandler)));

        return schemes;
    }

    private static AuthenticationSchemeProvider WithoutASchemeRegistered()
        => new(Options.Create(new AuthenticationOptions()));
}
