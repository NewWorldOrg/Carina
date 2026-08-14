using System.Net;

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

        var (status, body) = await feature.GetAsync("/api/services");
        var listed = body.GetProperty("data");

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

        var (_, body) = await feature.GetAsync("/api/services");

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

        var (status, body) = await feature.GetAsync(OneService);
        var data = body.GetProperty("data");

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

        var (status, _) = await feature.GetAsync("/api/services/1-999");

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

        var second = feature.Candidates.Candidates[1];

        var (status, body) = await feature.PutAsync(
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

        foreach (var candidate in feature.Candidates.Candidates.ToArray())
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

        var (status, body) = await feature.PutAsync(
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
        var elsewhere = feature.Seed(102, "Second", TuningParameters.Terrestrial(OtherTerrestrial));

        var (status, _) = await feature.PutAsync(
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

        var (status, _) = await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task AChannelAddedByHandArrivesNeedingRevalidation()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "First", TuningParameters.Terrestrial(Terrestrial));

        var (status, body) = await feature.PostAsync(
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

        var (status, body) = await feature.PostAsync(
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

        var (status, _) = await feature.PostAsync(
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

        var (status, body) = await feature.PostAsync(
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

        var (status, _) = await feature.PostAsync(
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

        var (status, _) = await feature.PostAsync(
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

        var (status, _) = await feature.PostAsync(
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

        var doomed = feature.Candidates.Candidates[0];

        var (status, body) = await feature.DeleteAsync(
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

        var chosen = feature.Candidates.Candidates[0];
        await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = chosen.Id.Value });

        var (status, body) = await feature.DeleteAsync(
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
        var elsewhere = feature.Seed(102, "Second", TuningParameters.Terrestrial(OtherTerrestrial));

        var (status, _) = await feature.DeleteAsync(
            $"{OneService}/candidate-channels/{elsewhere.Id.Value}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal(2, feature.Candidates.Candidates.Count);
    }

    [Fact]
    public async Task EveryCatalogSurfaceIsBehindTheSameDenialAsTheRest()
    {
        using var anonymous = new TestingWebApplicationFactory();
        using var client = anonymous.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync(new Uri("/api/services", UriKind.Relative))).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.GetAsync(new Uri(OneService, UriKind.Relative))).StatusCode);
    }
}
