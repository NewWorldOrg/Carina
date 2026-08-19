using System.Net;
using System.Text.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ForwardedIdentityTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string Spoofed = "someone-else@elsewhere.example";

    private static readonly Uri Status = new("/api/driver/status", UriKind.Relative);

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
    public async Task AHeaderNamingAUserIsNotACredential()
    {
        using HttpClient client = Spoofing(factory.WithTestScheme().CreateClient());

        using HttpResponseMessage response = await client.GetAsync(Status);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AHeaderNamingAUserChangesNothingAboutAnAnsweredRequest()
    {
        using HttpClient plain = factory.CreateAuthenticatedClient();
        using HttpClient spoofing = Spoofing(factory.CreateAuthenticatedClient());

        using HttpResponseMessage answered = await plain.GetAsync(Status);
        using HttpResponseMessage spoofed = await spoofing.GetAsync(Status);
        string body = await spoofed.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, spoofed.StatusCode);
        Assert.Equal(await ConnectionAsync(answered), ConnectionOf(body));
        Assert.DoesNotContain(Spoofed, body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AHeaderNamingAUserDoesNotOpenTheDocumentOutsideDevelopment()
    {
        using var deployed = new TestingWebApplicationFactory();
        using HttpClient client = Spoofing(deployed
            .WithWebHostBuilder(builder => builder.UseEnvironment(Environments.Production))
            .WithTestScheme()
            .CreateClient());

        using HttpResponseMessage response = await client.GetAsync(new Uri(ServedOpenApi.Route, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static HttpClient Spoofing(HttpClient client)
    {
        foreach (string header in EdgeIdentityHeaders)
        {
            client.DefaultRequestHeaders.Add(header, Spoofed);
        }

        return client;
    }

    private static async Task<string?> ConnectionAsync(HttpResponseMessage response)
        => ConnectionOf(await response.Content.ReadAsStringAsync());

    private static string? ConnectionOf(string body)
    {
        using var document = JsonDocument.Parse(body);

        return document.RootElement.GetProperty("data").GetProperty("connection").GetString();
    }
}
