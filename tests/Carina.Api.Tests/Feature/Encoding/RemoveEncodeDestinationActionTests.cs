using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class RemoveEncodeDestinationActionTests
{
    [Fact(DisplayName = "a retired destination takes no new job")]
    public async Task ARetiredDestinationTakesNoNewJob()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination leaving = feature.Placed(profile);
        feature.Placed(profile);
        feature.Completed(feature.Recorded(), profile, leaving);
        Recording next = feature.Recorded();

        (HttpStatusCode retired, JsonElement removal) = await feature.DeleteAsync($"/api/encoding/destinations/{leaving.Id.Value}");
        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/jobs", new
        {
            recordingId = next.Id.Wire,
            destinationId = leaving.Id.Value,
        });

        Assert.Equal(HttpStatusCode.OK, retired);
        Assert.Equal("retired", removal.GetProperty("data").GetProperty("removal").GetString());
        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("takes nothing new", body.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "the last destination left is not removed, because a machine with nowhere to write encodes nothing")]
    public async Task TheLastDestinationLeftIsNotRemoved()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination only = feature.Placed(profile);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/encoding/destinations/{only.Id.Value}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("only one left", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Single(feature.Destinations.Destinations);
    }

    [Fact(DisplayName = "a destination no job names is gone from the ledger when it is removed")]
    public async Task ADestinationNoJobNamesIsGoneWhenItIsRemoved()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        feature.Placed(profile);
        EncodeDestination spare = feature.Placed(profile);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/encoding/destinations/{spare.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("deleted", body.GetProperty("data").GetProperty("removal").GetString());
        Assert.DoesNotContain(spare, feature.Destinations.Destinations);
    }

    [Fact(DisplayName = "a definition nobody defined answers that it is not there")]
    public async Task ADefinitionNobodyDefinedIsNotThere()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode removed, _) = await feature.DeleteAsync($"/api/encoding/destinations/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, removed);
    }

    [Fact(DisplayName = "an id of nothing but zeroes names no definition")]
    public async Task AnIdOfNothingButZeroesNamesNoDefinition()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode removed, _) = await feature.DeleteAsync($"/api/encoding/destinations/{Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, removed);
    }
}
