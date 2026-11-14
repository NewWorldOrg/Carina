using System.Net;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;

using static Carina.Api.Tests.Feature.Services.CatalogFeature;

namespace Carina.Api.Tests.Feature.Services;

[Trait("Category", "Feature")]
public sealed class DeleteCandidateChannelActionTests
{
    [Fact]
    public async Task ACandidateNamedByTheUuidThatIsAllZeroesIsRefusedAsARequestAndNothingIsRemoved()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "Two ways in", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync(
            $"{OneService}/candidate-channels/{Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(CandidateChannelIdText.Description, body.GetProperty("message").GetString());
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
    public async Task DeletingTheSelectedCandidateAsksForTheAllocationToBeSettledAgain()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        CandidateChannel chosen = feature.Candidates.Candidates[0];
        await feature.Candidates.SelectAsync(
            chosen.Id,
            SelectionSource.Manual,
            null,
            TunerHoldingDriverClient.At,
            CancellationToken.None);

        (HttpStatusCode status, JsonElement _) = await feature.DeleteAsync(
            $"{OneService}/candidate-channels/{chosen.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([RecalculationTrigger.SelectedChannelChanged], feature.Notices.Nudged);
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
        Assert.Empty(feature.Notices.Nudged);
    }
}
