using System.Net;
using System.Net.Http.Headers;

using Microsoft.AspNetCore.Mvc.Testing;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ScreenRequestTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    [Theory]
    [InlineData("/programs", "/login?next=%2Fprograms")]
    [InlineData("/programs?type=terrestrial", "/login?next=%2Fprograms%3Ftype%3Dterrestrial")]
    [InlineData("/settings/tuners", "/login?next=%2Fsettings%2Ftuners")]
    public async Task AScreenAskedForWithoutCredentialsIsSentToTheLoginScreen(string path, string sentTo)
    {
        using HttpClient client = Browser();

        using HttpResponseMessage response = await client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal(sentTo, response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task TheLoginScreenIsNotSentToItself()
    {
        using HttpClient client = Browser();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/login", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ACallForDataIsRefusedRatherThanSentToAScreenEvenWhenItSaysItTakesHtml()
    {
        using HttpClient client = Browser();

        using HttpResponseMessage response = await client.GetAsync(new Uri("/api/programs", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task APlayerIsRefusedRatherThanHandedALoginScreenItCannotRead()
    {
        using HttpClient client = Browser();
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using HttpResponseMessage response = await client.GetAsync(new Uri("/recordings/1.ts", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    private HttpClient Browser()
    {
        HttpClient client = factory
            .WithTestScheme()
            .CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        return client;
    }
}
