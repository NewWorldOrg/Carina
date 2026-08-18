using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ForgetArchivedServiceEndpointTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task LettingGoOfAServiceNeedsTheWordThatMeansIt()
    {
        await using var feature = new EpgFeature();

        feature.Archived.Programmes.Add(Kept(1049));

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/epg/archive/forget-service",
            new { networkId = 4, serviceId = 1049 });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Single(feature.Archived.Programmes);
    }

    [Fact]
    public async Task AConfirmedRequestLetsGoOfThatServiceOnly()
    {
        await using var feature = new EpgFeature();

        feature.Archived.Programmes.Add(Kept(1049));
        feature.Archived.Programmes.Add(Kept(1050));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            "/api/epg/archive/forget-service",
            new { networkId = 4, serviceId = 1049, confirm = "forget-this-service" });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("forgotten").GetInt32());
        Assert.Equal(1050, Assert.Single(feature.Archived.Programmes).ServiceId.Value);
    }

    [Fact]
    public async Task AServiceThatIsNotNamedIsRefused()
    {
        await using var feature = new EpgFeature();

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/epg/archive/forget-service",
            new { confirm = "forget-this-service" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AnIdOutsideWhatBroadcastingAllowsIsRefusedRatherThanThrown()
    {
        await using var feature = new EpgFeature();

        (HttpStatusCode status, _) = await feature.PostAsync(
            "/api/epg/archive/forget-service",
            new { networkId = 999_999, serviceId = 1049, confirm = "forget-this-service" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    private static ArchivedProgramme Kept(int service)
        => ArchivedProgramme.Rehydrate(
            new NetworkId(4),
            new ServiceId(service),
            new EventId(1),
            At,
            At.AddMinutes(30),
            "ニュース",
            string.Empty,
            false,
            [],
            [],
            At);
}
