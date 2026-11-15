using System.Net;
using System.Text.Json;

using Carina.Api.Tests.Support;
using Carina.Domain.Channels;

using Puppis.Contracts;

using static Carina.Api.Tests.Feature.Services.CatalogFeature;

namespace Carina.Api.Tests.Feature.Services;

[Trait("Category", "Feature")]
public sealed class ListServicesActionTests
{
    [Fact]
    public async Task TheListNamesEveryServiceWithItsIdentifiersAndCategory()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));
        feature.Seed(102, "Second", TuningParameters.Terrestrial(OtherTerrestrial));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/services");
        JsonElement listed = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, listed.GetArrayLength());
        Assert.Equal(1, listed[0].GetProperty("networkId").GetInt32());
        Assert.Equal(101, listed[0].GetProperty("serviceId").GetInt32());
        Assert.Equal("television", listed[0].GetProperty("category").GetString());
    }

    [Fact]
    public async Task TwoServicesSharingANameAreStillToldApartByTheirIdentifiers()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "Same name", TuningParameters.Terrestrial(Terrestrial));
        feature.Seed(102, "Same name", TuningParameters.Terrestrial(OtherTerrestrial));

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync("/api/services");

        Assert.Equal(
            [101, 102],
            body.GetProperty("data").EnumerateArray()
                .Select(service => service.GetProperty("serviceId").GetInt32()));
    }

    [Fact]
    public async Task ASatelliteServiceIsSaidToBeOutOfReceptionOnceItsOnlyTunerIsTurnedOffAndIsKept()
    {
        await using var feature = new CatalogFeature();
        feature.Driver.Tuners =
        [
            new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle),
            new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Disabled),
        ];
        feature.Seed(101, "Ground", TuningParameters.Terrestrial(Terrestrial));
        feature.Seed(102, "Sky", TuningParameters.Bs(SatelliteSlot, new TransportStreamId(SatelliteStream)));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/services");
        JsonElement listed = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["receivable", "noTunerInService"], Receptions(listed));
        Assert.Equal(2, feature.Services.Services.Count);
        Assert.Equal(2, feature.Candidates.Candidates.Count);
    }

    [Fact]
    public async Task TurningTheSatelliteTunerBackOnMakesTheSameServiceReceivableAgain()
    {
        await using var feature = new CatalogFeature();
        feature.Driver.Tuners =
        [
            new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Disabled),
        ];
        feature.Seed(102, "Sky", TuningParameters.Bs(SatelliteSlot, new TransportStreamId(SatelliteStream)));

        Assert.Equal(["noTunerInService"], Receptions((await feature.GetAsync("/api/services")).Body.GetProperty("data")));

        feature.Driver.Tuners = [new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Idle)];

        Assert.Equal(["receivable"], Receptions((await feature.GetAsync("/api/services")).Body.GetProperty("data")));
    }

    [Fact]
    public async Task TunersThatCannotBeAskedAboutLeaveReceptionUnknownRatherThanOut()
    {
        await using var feature = new CatalogFeature();
        feature.Driver.Unreachable = "no socket";
        feature.Seed(101, "Ground", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/services");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["unknown"], Receptions(body.GetProperty("data")));
    }

    [Fact]
    public async Task TheListNamesTheChannelThatMeasuredBetterThanTheSelectedOne()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));
        feature.Measure(0, 12_000);
        feature.Measure(1, 29_000);

        await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = feature.Candidates.Candidates[0].Id.Value });

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/services");
        JsonElement listed = body.GetProperty("data")[0];

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            Terrestrial,
            listed.GetProperty("selectedChannel").GetProperty("physicalChannel").GetInt32());
        Assert.Equal(
            OtherTerrestrial,
            listed.GetProperty("betterChannel").GetProperty("physicalChannel").GetInt32());
    }

    [Fact]
    public async Task TheListNamesNoBetterChannelWhenTheMeasurementsAlreadyFavourTheSelectedOne()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));
        feature.Measure(0, 29_000);
        feature.Measure(1, 12_000);

        await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = feature.Candidates.Candidates[0].Id.Value });

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync("/api/services");

        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            body.GetProperty("data")[0].GetProperty("betterChannel").ValueKind);
    }

    [Fact]
    public async Task AChannelChosenByHandIsSaidToBeOutrankedJustAsOneAScanChoseWouldBe()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));
        feature.Measure(0, 12_000);
        feature.Measure(1, 29_000);

        await feature.Candidates.SelectAsync(
            feature.Candidates.Candidates[0].Id,
            SelectionSource.Manual,
            null,
            TunerHoldingDriverClient.At,
            CancellationToken.None);

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync("/api/services");
        JsonElement listed = body.GetProperty("data")[0];

        Assert.Equal(
            "manual",
            listed.GetProperty("candidates")[0].GetProperty("selection").GetProperty("source").GetString());
        Assert.Equal(
            OtherTerrestrial,
            listed.GetProperty("betterChannel").GetProperty("physicalChannel").GetInt32());
    }

    [Fact]
    public async Task AServiceWithNowhereToTuneIsNotSaidToHaveABetterChannel()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));
        feature.Measure(0, 12_000);
        feature.Measure(1, 29_000);

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync("/api/services");

        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            body.GetProperty("data")[0].GetProperty("betterChannel").ValueKind);
    }

    [Fact]
    public async Task SayingTheSelectionIsOutrankedDoesNotMoveTheSelection()
    {
        await using var feature = new CatalogFeature();
        await feature.SelectTheWeakerOfTwoChannelsAsync();

        await feature.GetAsync("/api/services");

        CandidateChannel held = Assert.Single(
            feature.Candidates.Candidates,
            candidate => candidate.IsSelected);

        Assert.Equal(Terrestrial, held.Tuning.PhysicalChannel);
        Assert.Equal(SelectionSource.Manual, held.SelectionSource);
    }

    [Fact]
    public async Task ACandidateThatKeptFailingSaysSoOnTheListWithoutOpeningIt()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));
        feature.Measure(0, 21_000);
        feature.Refuse(1, RotationBackoff.Default.FailureCeiling);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/services");
        JsonElement candidates = body.GetProperty("data")[0].GetProperty("candidates");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("active", candidates[0].GetProperty("rotationState").GetString());
        Assert.Equal("needsAttention", candidates[1].GetProperty("rotationState").GetString());
        Assert.Equal(
            RotationBackoff.Default.FailureCeiling,
            candidates[1].GetProperty("consecutiveFailures").GetInt32());
        Assert.NotEqual(
            JsonValueKind.Null,
            candidates[1].GetProperty("needsAttentionSince").ValueKind);
    }

    [Fact]
    public async Task ACandidateStillBackingOffSaysWhenItIsDueAgainRatherThanThatItIsGone()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "One way in", TuningParameters.Terrestrial(Terrestrial));
        feature.Refuse(0, 1);

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync("/api/services");
        JsonElement candidate = body.GetProperty("data")[0].GetProperty("candidates")[0];

        Assert.Equal("backingOff", candidate.GetProperty("rotationState").GetString());
        Assert.Equal(
            new DateTimeOffset(TunerHoldingDriverClient.At.Add(RotationBackoff.Default.FirstDelay)),
            candidate.GetProperty("nextAttemptAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task EveryCatalogSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        using var app = new TestingWebApplicationFactory();
        using HttpClient client = app.WithTestScheme().CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync(new Uri("/api/services", UriKind.Relative))).StatusCode);
    }

    [Fact]
    public async Task AServiceWhoseLogoHasBeenCollectedSaysSoAndWhereToAskForIt()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));
        feature.Logos.Services[0].NamesTheLogo(new LogoId(261));
        feature.Logos.Logos.Add(Logo(261));

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync("/api/services");
        JsonElement listed = body.GetProperty("data")[0];

        Assert.Equal("inTheCommonDataTable", listed.GetProperty("logoDeclaration").GetString());
        Assert.Equal("/api/services/1-101/logo", listed.GetProperty("logo").GetProperty("url").GetString());
    }

    [Fact]
    public async Task TwoServicesOfOneStationBothPointAtTheirOwnAddressForTheLogoTheyShare()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));
        feature.Seed(102, "Second", TuningParameters.Terrestrial(Terrestrial));
        feature.Logos.Services[0].NamesTheLogo(new LogoId(261));
        feature.Logos.Services[1].NamesTheLogo(new LogoId(261));
        feature.Logos.Logos.Add(Logo(261));

        (HttpStatusCode _, JsonElement body) = await feature.GetAsync("/api/services");
        JsonElement listed = body.GetProperty("data");

        Assert.Equal("/api/services/1-101/logo", listed[0].GetProperty("logo").GetProperty("url").GetString());
        Assert.Equal("/api/services/1-102/logo", listed[1].GetProperty("logo").GetProperty("url").GetString());
    }
}
