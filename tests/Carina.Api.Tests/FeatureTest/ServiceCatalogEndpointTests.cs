using System.Net;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class ServiceCatalogEndpointTests
{
    private const int Terrestrial = 53;
    private const int OtherTerrestrial = 55;
    private const int ThirdTerrestrial = 57;
    private const int SatelliteSlot = 5;
    private const int SatelliteStream = 50001;

    private const string OneService = "/api/services/1-101";

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

    [Fact]
    public async Task AServiceNobodyEverFoundIsAnsweredAsNotFound()
    {
        await using var feature = new CatalogFeature();

        (HttpStatusCode status, JsonElement _) = await feature.GetAsync("/api/services/1-999");

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task SwitchingTheSelectedCandidateChangesWhichChannelTheServiceTunesFrom()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        CandidateChannel second = feature.Candidates.Candidates[1];

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = second.Id.Value });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            OtherTerrestrial,
            body.GetProperty("data").GetProperty("selectedChannel").GetProperty("physicalChannel").GetInt32());
    }

    [Fact]
    public async Task OnlyOneCandidateIsEverSelectedAndTheDatabaseIsWhatHoldsThat()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Three ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial),
            TuningParameters.Terrestrial(ThirdTerrestrial));

        foreach (CandidateChannel candidate in feature.Candidates.Candidates.ToArray())
        {
            await feature.PutAsync(
                $"{OneService}/selected-channel",
                new { candidateChannelId = candidate.Id.Value });
        }

        Assert.Single(feature.Candidates.Candidates, candidate => candidate.IsSelected);
        Assert.Equal(
            ThirdTerrestrial,
            feature.Candidates.Candidates.Single(candidate => candidate.IsSelected).Tuning.PhysicalChannel);
    }

    [Fact]
    public async Task ClearingTheSelectionLeavesTheServiceWithNowhereToTuneRatherThanARepointing()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = feature.Candidates.Candidates[0].Id.Value });

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            body.GetProperty("data").GetProperty("selectedChannel").ValueKind);
        Assert.DoesNotContain(feature.Candidates.Candidates, candidate => candidate.IsSelected);
    }

    [Fact]
    public async Task SelectingACandidateThatBelongsToAnotherServiceIsRefused()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));
        CandidateChannel elsewhere = feature.Seed(102, "Second", TuningParameters.Terrestrial(OtherTerrestrial));

        (HttpStatusCode status, JsonElement _) = await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = elsewhere.Id.Value });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.DoesNotContain(feature.Candidates.Candidates, candidate => candidate.IsSelected);
    }

    [Fact]
    public async Task SelectingACandidateNobodyHoldsIsAnsweredAsNotFound()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement _) = await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task AChannelAddedByHandArrivesNeedingRevalidation()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"{OneService}/candidate-channels",
            new { tuning = new { system = "isdbT", physicalChannel = OtherTerrestrial } });

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(2, body.GetProperty("data").GetProperty("candidateCount").GetInt32());
        Assert.Contains(
            feature.Candidates.Candidates,
            candidate => candidate.Tuning.PhysicalChannel == OtherTerrestrial && candidate.NeedsRevalidation);
    }

    [Fact]
    public async Task AChannelOutsideTheRangeTheStandardAllowsIsRefusedWithTheRangeItBroke()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"{OneService}/candidate-channels",
            new { tuning = new { system = "isdbT", physicalChannel = 12 } });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("13", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Single(feature.Candidates.Candidates);
    }

    [Fact]
    public async Task ASatelliteSlotTheDemodulatorCannotReachIsRefused()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement _) = await feature.PostAsync(
            $"{OneService}/candidate-channels",
            new { tuning = new { system = "isdbSBs", physicalChannel = 7, transportStreamId = SatelliteStream } });

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task AChannelNoTunerInServiceCanReceiveIsRefusedWithWhatItClashedWith()
    {
        await using var feature = new CatalogFeature();
        feature.Driver.Tuners = [new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle)];
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync(
            $"{OneService}/candidate-channels",
            new
            {
                tuning = new
                {
                    system = "isdbSBs",
                    physicalChannel = SatelliteSlot,
                    transportStreamId = SatelliteStream,
                },
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
        Assert.Contains("satellite", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Single(feature.Candidates.Candidates);
    }

    [Fact]
    public async Task ADisabledTunerNoLongerCountsAsSomethingThatCanReceive()
    {
        await using var feature = new CatalogFeature();
        feature.Driver.Tuners =
        [
            new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle),
            new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Disabled),
        ];
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement _) = await feature.PostAsync(
            $"{OneService}/candidate-channels",
            new
            {
                tuning = new
                {
                    system = "isdbSBs",
                    physicalChannel = SatelliteSlot,
                    transportStreamId = SatelliteStream,
                },
            });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, status);
    }

    [Fact]
    public async Task AChannelIsNotSavedWhileTheTunersItNeedsCannotBeAskedAbout()
    {
        await using var feature = new CatalogFeature();
        feature.Driver.Unreachable = "the driver socket is not there";
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement _) = await feature.PostAsync(
            $"{OneService}/candidate-channels",
            new { tuning = new { system = "isdbT", physicalChannel = OtherTerrestrial } });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Single(feature.Candidates.Candidates);
    }

    [Fact]
    public async Task AChannelTheServiceAlreadyCarriesIsNotAddedTwice()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement _) = await feature.PostAsync(
            $"{OneService}/candidate-channels",
            new { tuning = new { system = "isdbT", physicalChannel = Terrestrial } });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Single(feature.Candidates.Candidates);
    }

    [Fact]
    public async Task DeletingACandidateLeavesTheServiceAndItsOtherChannelsInPlace()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        CandidateChannel doomed = feature.Candidates.Candidates[0];

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(
            $"{OneService}/candidate-channels/{doomed.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("candidateCount").GetInt32());
        Assert.Single(feature.Services.Services);
    }

    [Fact]
    public async Task DeletingTheSelectedCandidateLeavesNothingSelectedRatherThanARepointing()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        CandidateChannel chosen = feature.Candidates.Candidates[0];
        await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = chosen.Id.Value });

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(
            $"{OneService}/candidate-channels/{chosen.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            System.Text.Json.JsonValueKind.Null,
            body.GetProperty("data").GetProperty("selectedChannel").ValueKind);
    }

    [Fact]
    public async Task DeletingACandidateOfAnotherServiceIsRefused()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));
        CandidateChannel elsewhere = feature.Seed(102, "Second", TuningParameters.Terrestrial(OtherTerrestrial));

        (HttpStatusCode status, JsonElement _) = await feature.DeleteAsync(
            $"{OneService}/candidate-channels/{elsewhere.Id.Value}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(2, feature.Candidates.Candidates.Count);
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

        await feature.GetAsync("/api/services");
        await feature.GetAsync(OneService);

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
    public async Task DeletingAChannelDefinitionLeavesTheProgrammesCollectedThroughItAlone()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));
        feature.Collect(101, 40_001, "Kept");
        feature.Collect(101, 40_002, "Kept too");

        CandidateChannel doomed = feature.Candidates.Candidates[0];

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(
            $"{OneService}/candidate-channels/{doomed.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(1, body.GetProperty("data").GetProperty("candidateCount").GetInt32());
        Assert.Equal(
            [40_001, 40_002],
            feature.Programmes.Programmes.Select(programme => programme.EventId.Value));
    }

    [Fact]
    public async Task LeavingAServiceWithNowhereToTuneStillLeavesItsProgrammesAlone()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "One way in", TuningParameters.Terrestrial(Terrestrial));
        feature.Collect(101, 40_001, "Kept");

        CandidateChannel chosen = feature.Candidates.Candidates[0];
        await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = chosen.Id.Value });

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(
            $"{OneService}/candidate-channels/{chosen.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            JsonValueKind.Null,
            body.GetProperty("data").GetProperty("selectedChannel").ValueKind);
        Assert.Single(feature.Programmes.Programmes);
    }

    [Fact]
    public async Task EveryCatalogSurfaceIsBehindTheSameDenialAsTheRestOnceASchemeIsRegistered()
    {
        using var app = new TestingWebApplicationFactory();
        using HttpClient client = app.WithTestScheme().CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync(new Uri("/api/services", UriKind.Relative))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync(new Uri(OneService, UriKind.Relative))).StatusCode);
    }
}
