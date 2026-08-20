using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class CookieAppendingHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "CookieProbe";

    public const string CookieName = "carina-probe";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        Response.Cookies.Append(
            CookieName,
            "issued",
            new CookieOptions
            {
                SameSite = SameSiteMode.None,
                HttpOnly = false,
                Path = "/",
            });

        return Task.FromResult(AuthenticateResult.NoResult());
    }
}

[Collection(FeatureTestCollection.Name)]
public sealed class StateChangingRequestTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private static readonly Uri Restart = new("/api/driver/restart", UriKind.Relative);

    private static readonly Uri Status = new("/api/driver/status", UriKind.Relative);

    [Fact]
    public async Task ARequestFromThisOriginCarryingJsonReachesTheEndpoint()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsync(Restart, Json());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task ARequestNamingNoOriginIsRefused()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);

        using HttpResponseMessage response = await client.PostAsync(Restart, Json());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(await SucceededAsync(response));
    }

    [Theory]
    [InlineData("https://elsewhere.example")]
    [InlineData("http://localhost.elsewhere.example")]
    [InlineData("null")]
    public async Task ARequestNamingAnotherOriginIsRefused(string origin)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, origin);

        using HttpResponseMessage response = await client.PostAsync(Restart, Json());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AFormPostIsRefusedEvenWhereTheEndpointReadsNoBody()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        using var form = new StringContent("anything=1", Encoding.UTF8, "application/x-www-form-urlencoded");

        using HttpResponseMessage response = await client.PostAsync(Restart, form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.False(await SucceededAsync(response));
    }

    [Fact]
    public async Task ARequestCarryingNoContentTypeAtAllIsRefused()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsync(Restart, content: null);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Theory]
    [InlineData("text/plain")]
    [InlineData("multipart/form-data")]
    public async Task ABodyAnotherSiteCanPostWithoutBeingLetThroughFirstIsRefused(string type)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        using var content = new StringContent("anything=1", Encoding.UTF8, type);

        using HttpResponseMessage response = await client.PostAsync(Restart, content);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.False(await SucceededAsync(response));
    }

    [Theory]
    [InlineData("DELETE", "/api/auth/sessions/anything")]
    [InlineData("PUT", "/api/auth/oidc-config")]
    public async Task ADestructiveRequestNamingAnotherOriginIsRefusedWhateverItsMethod(string method, string path)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://elsewhere.example");

        using HttpResponseMessage response = await SendingAsync(client, method, path, Json());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("DELETE", "/api/auth/sessions/anything")]
    [InlineData("PUT", "/api/auth/oidc-config")]
    public async Task ADestructiveRequestCarryingAFormIsRefusedWhateverItsMethod(string method, string path)
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        using var form = new StringContent("anything=1", Encoding.UTF8, "application/x-www-form-urlencoded");

        using HttpResponseMessage response = await SendingAsync(client, method, path, form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task AReadFromAnotherOriginIsLeftAlone()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();
        client.DefaultRequestHeaders.Remove(HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(HeaderNames.Origin, "https://elsewhere.example");

        using HttpResponseMessage response = await client.GetAsync(Status);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task AMethodTheEndpointDoesNotAnswerIsStillToldSo()
    {
        using HttpClient client = factory.CreateAuthenticatedClient();

        using HttpResponseMessage response = await client.PostAsync(Status, content: null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }

    [Fact]
    public async Task ACookieLeavesWithSameSiteLaxAndOutOfReachOfScripts()
    {
        WebApplicationFactory<Program> probed = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services
                    .AddAuthentication(CookieAppendingHandler.SchemeName)
                    .AddScheme<AuthenticationSchemeOptions, CookieAppendingHandler>(
                        CookieAppendingHandler.SchemeName,
                        _ => { })));
        using HttpClient client = probed.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/health", UriKind.Relative));
        string cookie = Assert.Single(response.Headers.GetValues(HeaderNames.SetCookie));

        Assert.StartsWith(CookieAppendingHandler.CookieName, cookie, StringComparison.Ordinal);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("samesite=none", cookie, StringComparison.OrdinalIgnoreCase);
    }

    private static StringContent Json() => new("{}", Encoding.UTF8, "application/json");

    private static async Task<HttpResponseMessage> SendingAsync(
        HttpClient client,
        string method,
        string path,
        HttpContent content)
    {
        using var asking = new HttpRequestMessage(new HttpMethod(method), new Uri(path, UriKind.Relative))
        {
            Content = content,
        };

        return await client.SendAsync(asking);
    }

    private static async Task<bool> SucceededAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        return body.RootElement.GetProperty("status").GetBoolean();
    }
}
