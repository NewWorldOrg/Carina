using System.Net;
using System.Text.Json;

using Carina.Domain.Channels;

using Puppis.Contracts;

using static Carina.Api.Tests.Feature.Services.CatalogFeature;

namespace Carina.Api.Tests.Feature.Services;

[Trait("Category", "Feature")]
public sealed class AddCandidateChannelActionTests
{
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
}
