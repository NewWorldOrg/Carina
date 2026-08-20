using System.Net.Http.Json;
using System.Text;

using Carina.Domain.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class AuthProbe : IAsyncDisposable
{
    public const string Password = "a password long enough";

    private readonly TestingWebApplicationFactory factory = new();

    private AuthProbe(bool secure)
    {
        Wired = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAuthSessionRepository>(Sessions);
            services.AddSingleton<ILocalAccountRepository>(Accounts);
            services.AddSingleton<IPasswordHasher>(Hasher);
        }));

        Client = Wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri(secure ? "https://localhost" : "http://localhost"),
            HandleCookies = true,
        });

        Client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        Client.DefaultRequestHeaders.Add(
            HeaderNames.Origin,
            Client.BaseAddress!.GetLeftPart(UriPartial.Authority));
    }

    public static StringContent Json() => new("{}", Encoding.UTF8, "application/json");

    public WebApplicationFactory<Program> Wired { get; }

    public HttpClient Client { get; }

    public HeldAuthSessions Sessions { get; } = new();

    public HeldLocalAccount Accounts { get; } = new();

    public QuickPasswordHasher Hasher { get; } = new();

    public static AuthProbe OverHttp() => new(secure: false);

    public static AuthProbe OverHttps() => new(secure: true);

    public AuthProbe WithAnAccount()
    {
        Accounts.Account = LocalAccount.Bootstrap(
            FirstCredentials.Username,
            Hasher.Hash(Password, PasswordHashPolicy.Default),
            DateTime.UtcNow);

        return this;
    }

    public AuthSession Sitting(string device)
    {
        AuthSession session = AuthSession.Start(
            SessionId.Issue(),
            new Subject(FirstCredentials.Username),
            AuthMethod.Local,
            device,
            DateTime.UtcNow);

        Sessions.Sessions.Add(session);

        return session;
    }

    public Task<HttpResponseMessage> LogInAsync(string username, string password)
        => Client.PostAsJsonAsync(
            new Uri("/api/auth/login", UriKind.Relative),
            new { username, password });

    public HttpClient Relaying(string carried)
    {
        HttpClient client = Wired.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("http://localhost"),
            AllowAutoRedirect = false,
        });

        client.DefaultRequestHeaders.Add(HeaderNames.Cookie, carried);

        return client;
    }

    public async Task<HttpClient> RelayingAsync()
    {
        WithAnAccount();

        using HttpResponseMessage response = await LogInAsync(FirstCredentials.Username, Password);

        response.EnsureSuccessStatusCode();

        string handed = response.Headers.GetValues(HeaderNames.SetCookie).Single();

        return Relaying(handed[..handed.IndexOf(';', StringComparison.Ordinal)]);
    }

    public async Task<AuthSession> SignedInAsync()
    {
        WithAnAccount();

        using HttpResponseMessage response = await LogInAsync(FirstCredentials.Username, Password);

        response.EnsureSuccessStatusCode();

        return Sessions.Sessions[^1];
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }
}
