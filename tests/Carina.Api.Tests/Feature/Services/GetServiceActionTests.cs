using System.Net;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Channels;

using Puppis.Contracts;

using static Carina.Api.Tests.Feature.Services.CatalogFeature;

namespace Carina.Api.Tests.Feature.Services;

[Trait("Category", "Feature")]
public sealed class GetServiceActionTests
{
    [Fact]
    public async Task TheDetailCarriesEveryCandidateChannelAndWhichOneIsSelected()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        await feature.Candidates.SelectAsync(
            feature.Candidates.Candidates[0].Id,
            SelectionSource.Manual,
            null,
            TunerHoldingDriverClient.At,
            CancellationToken.None);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(OneService);
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, data.GetProperty("candidateCount").GetInt32());
        Assert.Equal(
            Terrestrial,
            data.GetProperty("selectedChannel").GetProperty("physicalChannel").GetInt32());
        Assert.Equal(
            "manual",
            data.GetProperty("candidates")[0].GetProperty("selection").GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("/api/services/70000-1")]
    [InlineData("/api/services/1-70000")]
    [InlineData("/api/services/-1-101")]
    public async Task AServiceNamedOutsideTheRangeOfItsIdsIsRefusedAsARequestAndNotAFault(string path)
    {
        await using var feature = new CatalogFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(ServiceKeyText.Description, body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AServiceNobodyEverFoundIsAnsweredAsNotFound()
    {
        await using var feature = new CatalogFeature();

        (HttpStatusCode status, JsonElement _) = await feature.GetAsync("/api/services/1-999");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task TheDetailOfAServiceNoTunerReceivesSaysSoToo()
    {
        await using var feature = new CatalogFeature();
        feature.Driver.Tuners = [new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle)];
        feature.Seed(101, "Sky", TuningParameters.Bs(SatelliteSlot, new TransportStreamId(SatelliteStream)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(OneService);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("noTunerInService", body.GetProperty("data").GetProperty("reception").GetString());
    }

    [Fact]
    public async Task SayingTheSelectionIsOutrankedDoesNotMoveTheSelection()
    {
        await using var feature = new CatalogFeature();
        await feature.SelectTheWeakerOfTwoChannelsAsync();

        await feature.GetAsync(OneService);

        CandidateChannel held = Assert.Single(
            feature.Candidates.Candidates,
            candidate => candidate.IsSelected);

        Assert.Equal(Terrestrial, held.Tuning.PhysicalChannel);
        Assert.Equal(SelectionSource.Manual, held.SelectionSource);
    }

    [Fact]
    public async Task EveryCatalogSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        using var app = new TestingWebApplicationFactory();
        using HttpClient client = app.WithTestScheme().CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync(new Uri(OneService, UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task AServiceThatBroadcastsNoLogoSaysSoRatherThanOfferingAnAddressThatAnswersNothing()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));
        feature.Logos.Services[0].BroadcastsNoLogo();

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync(OneService);
        JsonElement found = body.GetProperty("data");

        Assert.Equal("noPictureIsBroadcast", found.GetProperty("logoDeclaration").GetString());
        Assert.Equal(JsonValueKind.Null, found.GetProperty("logo").ValueKind);
    }

    [Fact]
    public async Task AServiceWhoseLogoIsNamedButNotYetCollectedOffersNoAddressForIt()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));
        feature.Logos.Services[0].NamesTheLogo(new LogoId(261));

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync(OneService);
        JsonElement found = body.GetProperty("data");

        Assert.Equal("inTheCommonDataTable", found.GetProperty("logoDeclaration").GetString());
        Assert.Equal(JsonValueKind.Null, found.GetProperty("logo").ValueKind);
    }

    [Fact]
    public async Task AServiceNobodyHasReadTheDescriptionOfSaysThatRatherThanThatItHasNoLogo()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync(OneService);

        Assert.Equal("notYetRead", body.GetProperty("data").GetProperty("logoDeclaration").GetString());
    }
}
