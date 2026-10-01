using System.Security.Claims;

using Carina.Api.Authentication;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;

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

    [Fact(DisplayName = "BR-AU-020: an API request refused for carrying no session cookie is written down")]
    public async Task BrAu020AnApiRequestRefusedForCarryingNoSessionCookieIsWrittenDown()
    {
        var heard = new RecordingLogger();

        await RunAsync(Asking("GET", "/api/tuners"), heard: heard);

        string said = Assert.Single(heard.Lines);

        Assert.Contains(nameof(SignInMoment.RefusedWithoutASessionCookie), said, StringComparison.Ordinal);
        Assert.Contains($"session cookie {SignInRecord.Absent}", said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: a refused request that did carry a cookie is left to whoever judged the cookie, so it is written once")]
    public async Task BrAu020ARefusedRequestThatDidCarryACookieIsLeftToWhoeverJudgedTheCookie()
    {
        var heard = new RecordingLogger();
        DefaultHttpContext context = Asking("GET", "/api/tuners");
        context.Request.Headers[HeaderNames.Cookie] = $"{SessionCookie.Name}=something-that-opened-nothing";

        bool reached = await RunAsync(context, heard: heard);

        Assert.False(reached);
        Assert.Empty(heard.Lines);
    }

    [Theory(DisplayName = "BR-AU-020: a screen sent to the login screen, a request outside the API and an open surface are not written down")]
    [InlineData("/programs", "text/html")]
    [InlineData("/recordings/1.ts", "video/mp2t")]
    [InlineData("/api/health", "application/json")]
    public async Task BrAu020WhatIsNotAnApiRefusalIsNotWrittenDown(string path, string accept)
    {
        var heard = new RecordingLogger();

        await RunAsync(Asking("GET", path, accept), heard: heard);

        Assert.Empty(heard.Lines);
    }

    [Fact(DisplayName = "BR-AU-020: a request that was admitted is not written down by the gate")]
    public async Task BrAu020ARequestThatWasAdmittedIsNotWrittenDownByTheGate()
    {
        var heard = new RecordingLogger();

        bool reached = await RunAsync(Authenticated(Asking("GET", "/api/tuners")), heard: heard);

        Assert.True(reached);
        Assert.Empty(heard.Lines);
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
        ILogger<SignInRecord>? heard = null)
    {
        bool reached = false;
        var middleware = new DefaultDenyAuthenticationMiddleware(
            admitted =>
            {
                reached = true;
                behind?.Invoke(admitted);

                return Task.CompletedTask;
            },
            new StubEnvironment(Environments.Production),
            new SignInRecord(heard ?? NullLogger<SignInRecord>.Instance, TimeProvider.System));

        await middleware.InvokeAsync(context);

        return reached;
    }

    private sealed class RecordingLogger : ILogger<SignInRecord>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Lines.Add(formatter(state, exception));
        }
    }
}
