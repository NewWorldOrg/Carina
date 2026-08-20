using System.Net;

using Carina.Api.Authentication;
using Carina.Api.Events;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class AddressAdmissionTests(TestingWebApplicationFactory factory)
    : IClassFixture<TestingWebApplicationFactory>
{
    private const string ArrivingAddress = "10.42.0.9";

    public static TheoryData<string> EverySurfaceBehindTheDenial =>
    [
        "/api/tuners",
        "/api/services",
        "/api/programs",
        "/api/recordings/1/stream.ts",
        AppEventStream.Path,
    ];

    [Theory]
    [MemberData(nameof(EverySurfaceBehindTheDenial))]
    public async Task ACallerFromANamedNetworkIsRefusedLikeAnyOtherCarryingNoSession(string path)
    {
        using HttpResponseMessage response = await AskAsync(path, named: "10.0.0.0/8");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [MemberData(nameof(EverySurfaceBehindTheDenial))]
    public async Task ACallerIsRefusedWhereNoNetworkIsNamedAtAll(string path)
    {
        using HttpResponseMessage response = await AskAsync(path, named: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ASettingNamingEveryNetworkThereIsAdmitsNoneOfThem()
    {
        using HttpResponseMessage response = await AskAsync("/api/tuners", named: "0.0.0.0/0, ::/0");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public void TheRunningApplicationReadsTheSettingRatherThanLeavingItUnwired()
    {
        WebApplicationFactory<Program> wired = factory.WithWebHostBuilder(builder =>
            builder.UseSetting(AnonymousNetworks.Key, "10.0.0.0/8"));

        Assert.Equal("10.0.0.0/8", wired.Services.GetRequiredService<AnonymousNetworks>().ToString());
    }

    [Fact]
    public async Task TheEnumeratedListIsTheOnlyThingThatAdmitsHoweverTheNetworksAreNamed()
    {
        using HttpResponseMessage response = await AskAsync("/api/health", named: "10.0.0.0/8");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> AskAsync(string path, string? named)
    {
        WebApplicationFactory<Program> wired = factory.WithTestScheme().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(AnonymousNetworks.Key, named ?? string.Empty);
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IStartupFilter>(new ArrivingFrom(IPAddress.Parse(ArrivingAddress))));
        });

        using HttpClient client = wired.CreateClient();

        return await client.GetAsync(new Uri(path, UriKind.Relative), HttpCompletionOption.ResponseHeadersRead);
    }
}
