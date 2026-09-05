using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

using Carina.Api.Logos;
using Carina.Domain.Channels;
using Carina.TestSupport;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class LogoDeliveryTests
{
    private const int SomeNetworkId = 32741;
    private const int SomeServiceId = 1024;
    private const int AnotherServiceId = 1025;
    private const int SomeLogoId = 261;

    private static readonly DateTime At = new(2026, 9, 5, 4, 30, 0, DateTimeKind.Utc);

    private static readonly byte[] APicture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02];

    [Fact]
    public async Task TheLogoOfAStationComesBackAsAPng()
    {
        await using var feature = new LogoFeature();
        feature.Naming(SomeServiceId, SomeLogoId);
        feature.Collected(SomeLogoId);

        using HttpResponseMessage answer = await feature.GetAsync(SomeServiceId);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(LogoDelivery.MediaType, answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(APicture, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TwoServicesOfOneStationAreBothAnsweredWithTheOneLogoTheyShare()
    {
        await using var feature = new LogoFeature();
        feature.Naming(SomeServiceId, SomeLogoId);
        feature.Naming(AnotherServiceId, SomeLogoId);
        feature.Collected(SomeLogoId);

        using HttpResponseMessage first = await feature.GetAsync(SomeServiceId);
        using HttpResponseMessage second = await feature.GetAsync(AnotherServiceId);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(first.Headers.ETag?.Tag, second.Headers.ETag?.Tag);
    }

    [Fact]
    public async Task ASecondAskForTheSameLogoIsAnsweredWithNothingToSendAgain()
    {
        await using var feature = new LogoFeature();
        feature.Naming(SomeServiceId, SomeLogoId);
        feature.Collected(SomeLogoId);
        using HttpResponseMessage first = await feature.GetAsync(SomeServiceId);

        using HttpResponseMessage second = await feature.GetAsync(SomeServiceId, first.Headers.ETag?.Tag);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
        Assert.Empty(await second.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheLogoSaysHowLongItMayBeKeptAndWhenItWasCollected()
    {
        await using var feature = new LogoFeature();
        feature.Naming(SomeServiceId, SomeLogoId);
        feature.Collected(SomeLogoId);

        using HttpResponseMessage answer = await feature.GetAsync(SomeServiceId);

        Assert.True(answer.Headers.CacheControl?.Private);
        Assert.Equal(TimeSpan.FromDays(1), answer.Headers.CacheControl?.MaxAge);
        Assert.Equal(
            At.ToString("R", CultureInfo.InvariantCulture),
            answer.Content.Headers.LastModified?.UtcDateTime.ToString("R", CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task AStationThatBroadcastsNoLogoIsAnsweredWithNothingRatherThanABrokenPicture()
    {
        await using var feature = new LogoFeature();
        feature.Naming(SomeServiceId, null);

        using HttpResponseMessage answer = await feature.GetAsync(SomeServiceId);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AStationWhoseLogoHasNotBeenCollectedYetIsAnsweredWithNothing()
    {
        await using var feature = new LogoFeature();
        feature.Naming(SomeServiceId, SomeLogoId);

        using HttpResponseMessage answer = await feature.GetAsync(SomeServiceId);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact]
    public async Task AServiceNobodyKnowsIsAnsweredWithNothing()
    {
        await using var feature = new LogoFeature();

        using HttpResponseMessage answer = await feature.GetAsync(SomeServiceId);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact]
    public async Task AnIdentifierNoBroadcastCouldCarryIsRefusedBeforeAnythingIsLookedUp()
    {
        await using var feature = new LogoFeature();

        using HttpResponseMessage answer = await feature.Client.GetAsync(
            new Uri($"/api/services/{NetworkId.MaxValue + 1}-{SomeServiceId}/logo", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Fact]
    public async Task AClientCarryingNoCredentialsIsRefusedRatherThanSentToASignInScreen()
    {
        await using var feature = new LogoFeature();
        feature.Naming(SomeServiceId, SomeLogoId);
        feature.Collected(SomeLogoId);

        using HttpResponseMessage answer = await feature.Stranger.GetAsync(
            new Uri(LogoDelivery.Of(new NetworkId(SomeNetworkId), new ServiceId(SomeServiceId)), UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    private sealed class LogoFeature : IAsyncDisposable
    {
        private readonly TestingWebApplicationFactory factory = new();

        public LogoFeature()
        {
            WebApplicationFactory<Program> configured = factory
                .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IHostedService>();
                    services.AddSingleton<IStationLogoRepository>(Logos);
                }));

            Client = configured.CreateAuthenticatedClient();
            Stranger = configured.WithTestScheme().CreateClient();
        }

        public HttpClient Client { get; }

        public HttpClient Stranger { get; }

        public HeldLogos Logos { get; } = new();

        public void Naming(int serviceId, int? logoId)
            => Logos.Services.Add(BroadcastService.Rehydrate(
                new NetworkId(SomeNetworkId),
                new ServiceId(serviceId),
                "Fixture Service",
                ServiceCategory.Television,
                At,
                At,
                logoId: logoId is { } named ? new LogoId(named) : null,
                logoDeclaration: logoId is null
                    ? StationLogoDeclaration.NoPictureIsBroadcast
                    : StationLogoDeclaration.InTheCommonDataTable));

        public void Collected(int logoId)
            => Logos.Logos.Add(StationLogo.Collect(
                new NetworkId(SomeNetworkId),
                new LogoId(logoId),
                0x05,
                3,
                64,
                36,
                APicture,
                At));

        public Task<HttpResponseMessage> GetAsync(int serviceId, string? knownTag = null)
        {
            var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(
                    LogoDelivery.Of(new NetworkId(SomeNetworkId), new ServiceId(serviceId)),
                    UriKind.Relative));

            if (knownTag is not null)
            {
                request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(knownTag));
            }

            using (request)
            {
                return Client.SendAsync(request);
            }
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            Stranger.Dispose();
            await factory.DisposeAsync();
        }
    }
}
