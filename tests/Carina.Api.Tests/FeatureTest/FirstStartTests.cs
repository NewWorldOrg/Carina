using System.Net;
using System.Net.Http.Json;

using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.FeatureTest;

internal sealed record SaidLine(
    string Category,
    LogLevel Level,
    string Text,
    IReadOnlyList<KeyValuePair<string, object?>> Values)
{
    public string Only(string name)
        => Values.FirstOrDefault(value => value.Key == name).Value?.ToString() ?? string.Empty;
}

internal sealed class RecordedStartup : ILoggerProvider
{
    private readonly List<SaidLine> said = [];

    public IReadOnlyList<SaidLine> By<T>(LogLevel level)
    {
        lock (said)
        {
            return [.. said.Where(line =>
                string.Equals(line.Category, typeof(T).FullName, StringComparison.Ordinal)
                && line.Level == level)];
        }
    }

    public ILogger CreateLogger(string categoryName) => new Line(said, categoryName);

    public void Dispose()
    {
    }

    private sealed class Line(List<SaidLine> said, string category) : ILogger
    {
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

            var line = new SaidLine(
                category,
                logLevel,
                formatter(state, exception),
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? []);

            lock (said)
            {
                said.Add(line);
            }
        }
    }
}

internal sealed class RefusingLocalAccounts : ILocalAccountRepository
{
    public Task<LocalAccount?> FindAsync(CancellationToken cancellationToken)
        => Task.FromException<LocalAccount?>(new InvalidOperationException("The store is out of reach."));

    public Task SaveAsync(LocalAccount account, CancellationToken cancellationToken)
        => Task.FromException(new InvalidOperationException("The store is out of reach."));
}

[Collection(FeatureTestCollection.Name)]
public sealed class FirstStartTests
{
    private static readonly Uri Login = new("/api/auth/login", UriKind.Relative);

    private static readonly Uri Tuners = new("/api/tuners", UriKind.Relative);

    [Fact]
    public async Task AnAppStartingWithNoAccountMakesOneAndTheWordItWroteDownOpensIt()
    {
        var log = new RecordedStartup();
        var accounts = new HeldLocalAccount();

        await using var factory = new TestingWebApplicationFactory();
        using HttpClient client = Started(factory, log, accounts);

        await Eventually.Happens(
            () => log.By<LocalAccountBootstrap>(LogLevel.Warning).Count > 0,
            "the app writes down the credentials it made for itself");

        SaidLine written = Assert.Single(log.By<LocalAccountBootstrap>(LogLevel.Warning));

        using HttpResponseMessage signedIn = await client.PostAsJsonAsync(
            Login,
            new { username = written.Only("Username"), password = written.Only("Password") });

        Assert.Equal(FirstCredentials.Username, written.Only("Username"));
        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
        Assert.Equal(1, accounts.Saves);
    }

    [Fact]
    public async Task AnAppThatCouldNotMakeThatAccountRefusesCallersRatherThanLettingThemPast()
    {
        var log = new RecordedStartup();

        await using var factory = new TestingWebApplicationFactory();
        using HttpClient client = Started(factory, log, new RefusingLocalAccounts());

        await Eventually.Happens(
            () => log.By<LocalAccountBootstrap>(LogLevel.Error).Count > 0,
            "the app says nobody can sign in");

        using HttpResponseMessage refused = await client.GetAsync(Tuners);
        using HttpResponseMessage attempted = await client.PostAsJsonAsync(
            Login,
            new { username = FirstCredentials.Username, password = "anything at all" });

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, attempted.StatusCode);
        Assert.DoesNotContain(
            attempted.Headers,
            header => header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnAppTrustingNoProxySaysSoAtStartupRatherThanAtTheFirstFailedSignIn()
    {
        var log = new RecordedStartup();

        await using var factory = new TestingWebApplicationFactory();
        WebApplicationFactory<Program> wired = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(TrustedProxies.ProxiesKey, string.Empty);
            builder.UseSetting(TrustedProxies.NetworksKey, string.Empty);
            builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(log));
        });

        using HttpClient client = wired.CreateClient();

        await Eventually.Happens(
            () => log.By<TrustedProxyDiagnosis>(LogLevel.Warning).Count > 0,
            "the app says it trusts nothing to label a forwarded request");

        SaidLine warned = Assert.Single(log.By<TrustedProxyDiagnosis>(LogLevel.Warning));

        Assert.Contains(TrustedProxies.ProxiesKey, warned.Text, StringComparison.Ordinal);
        Assert.Contains(TrustedProxies.NetworksKey, warned.Text, StringComparison.Ordinal);
    }

    private static HttpClient Started(
        TestingWebApplicationFactory factory,
        RecordedStartup log,
        ILocalAccountRepository accounts)
        => factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(log);
                services.AddSingleton(accounts);
                services.AddSingleton<IAuthSessionRepository>(new HeldAuthSessions());
                services.AddSingleton<IPasswordHasher>(new QuickPasswordHasher());
            }))
            .CreateClient();
}
