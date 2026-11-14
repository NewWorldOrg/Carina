using System.Net;
using System.Text.Json;

using Carina.Api.Common;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;

using static Carina.Api.Tests.Feature.Services.CatalogFeature;

namespace Carina.Api.Tests.Feature.Services;

[Trait("Category", "Feature")]
public sealed class PutSelectedChannelActionTests
{
    [Fact]
    public async Task ASelectedChannelThatChangedAsksForTheAllocationToBeSettledAgain()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(
            101,
            "Two ways in",
            TuningParameters.Terrestrial(Terrestrial),
            TuningParameters.Terrestrial(OtherTerrestrial));

        CandidateChannel second = feature.Candidates.Candidates[1];

        (HttpStatusCode status, JsonElement _) = await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = second.Id.Value });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal([RecalculationTrigger.SelectedChannelChanged], feature.Notices.Nudged);
    }

    [Fact]
    public async Task ASelectedChannelNamedByTheUuidThatIsAllZeroesIsRefusedAsARequestAndNothingMoves()
    {
        await using var feature = new CatalogFeature();
        feature.Seed(101, "Two ways in", TuningParameters.Terrestrial(Terrestrial));

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            $"{OneService}/selected-channel",
            new { candidateChannelId = Guid.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(CandidateChannelIdText.Description, body.GetProperty("message").GetString());
        Assert.Empty(feature.Notices.Nudged);
    }

    [Fact]
    public async Task ASelectedChannelOnAServiceNobodyEverFoundAsksForNothing()
    {
        await using var feature = new CatalogFeature();

        (HttpStatusCode status, JsonElement _) = await feature.PutAsync(
            "/api/services/1-999/selected-channel",
            new { candidateChannelId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Empty(feature.Notices.Nudged);
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
}
