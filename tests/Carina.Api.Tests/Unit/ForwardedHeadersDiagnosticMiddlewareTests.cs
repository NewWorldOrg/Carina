using System.Net;

using Carina.Api.Authentication;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.Unit;

public sealed class ForwardedHeadersDiagnosticMiddlewareTests
{
    private const string Forwarded = "X-Forwarded-Proto";

    private const string Original = "X-Original-Proto";

    private readonly RecordingLogger logger = new();

    [Fact]
    public async Task AForwardedRequestThatWasIgnoredIsReportedWithTheAddressToTrust()
    {
        DefaultHttpContext context = From("10.9.0.7");
        context.Request.Headers[Forwarded] = "https";

        await Middleware().InvokeAsync(context);

        string said = Assert.Single(logger.Warnings);

        Assert.Contains("10.9.0.7", said, StringComparison.Ordinal);
        Assert.Contains(TrustedProxies.ProxiesKey, said, StringComparison.Ordinal);
        Assert.Contains(TrustedProxies.NetworksKey, said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReportIsMadeOnceRatherThanOnEveryRequestBehindAnUntrustedProxy()
    {
        ForwardedHeadersDiagnosticMiddleware middleware = Middleware();

        for (int attempt = 0; attempt < 3; attempt++)
        {
            DefaultHttpContext context = From("10.9.0.7");
            context.Request.Headers[Forwarded] = "https";

            await middleware.InvokeAsync(context);
        }

        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task AForwardedRequestThatWasTakenConfirmsTheHeadersAreWorking()
    {
        DefaultHttpContext context = From("10.9.0.7");
        context.Request.Headers[Original] = "http";
        context.Request.Scheme = "https";

        await Middleware().InvokeAsync(context);

        Assert.Empty(logger.Warnings);

        string said = Assert.Single(logger.Confirmations);

        Assert.Contains("10.9.0.7", said, StringComparison.Ordinal);
        Assert.Contains("https", said, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARequestThatWasNeverForwardedSaysNothingEitherWay()
    {
        await Middleware().InvokeAsync(From("10.9.0.7"));

        Assert.Empty(logger.Warnings);
        Assert.Empty(logger.Confirmations);
    }

    [Fact]
    public async Task TheDiagnosisOnlyWatchesAndLetsTheRequestThrough()
    {
        DefaultHttpContext context = From("10.9.0.7");
        context.Request.Headers[Forwarded] = "https";
        bool reached = false;

        await Middleware(_ =>
            {
                reached = true;

                return Task.CompletedTask;
            })
            .InvokeAsync(context);

        Assert.True(reached);
    }

    private static DefaultHttpContext From(string address)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(address);

        return context;
    }

    private ForwardedHeadersDiagnosticMiddleware Middleware(RequestDelegate? next = null)
        => new(
            next ?? (_ => Task.CompletedTask),
            Options.Create(new ForwardedHeadersOptions()),
            logger);

    private sealed class RecordingLogger : ILogger<ForwardedHeadersDiagnosticMiddleware>
    {
        public List<string> Warnings { get; } = [];

        public List<string> Confirmations { get; } = [];

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

            List<string> said = logLevel is LogLevel.Warning ? Warnings : Confirmations;

            said.Add(formatter(state, exception));
        }
    }
}
