using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class RemoveEncodeProfileActionTests
{
    [Fact(DisplayName = "a profile a running job names is not taken away either")]
    public async Task AProfileARunningJobNamesIsNotTakenAway()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob running = feature.Queued(feature.Recorded(), profile, destination);
        running.Start(EncodingFeature.Noon.AddMinutes(-20));

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/encoding/profiles/{profile.Id.Value}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("running with", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Single(feature.Profiles.Profiles);
    }

    [Fact(DisplayName = "a profile no job names is gone from the ledger when it is removed")]
    public async Task AProfileNoJobNamesIsGoneWhenItIsRemoved()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile kept = feature.Defined();
        feature.Placed(kept);
        EncodeProfile unused = feature.Defined("Nobody asked for this");

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/encoding/profiles/{unused.Id.Value}");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("deleted", body.GetProperty("data").GetProperty("removal").GetString());
        Assert.Null(body.GetProperty("data").GetProperty("retiredAt").GetString());
        Assert.DoesNotContain(unused, feature.Profiles.Profiles);
    }

    [Fact(DisplayName = "a profile an artefact was made with is retired rather than removed, and still answers by id")]
    public async Task AProfileAnArtefactWasMadeWithIsRetiredRatherThanRemoved()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile standing = feature.Defined();
        EncodeDestination destination = feature.Placed(standing);
        EncodeProfile older = feature.Defined("What last month was encoded with");
        Recording recording = feature.Recorded();
        feature.Completed(recording, older, destination);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/encoding/profiles/{older.Id.Value}");
        (_, JsonElement listed) = await feature.GetAsync("/api/encoding/profiles");
        JsonElement item = listed.GetProperty("data").GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("id").GetGuid() == older.Id.Value);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("retired", body.GetProperty("data").GetProperty("removal").GetString());
        Assert.Equal(EncodingFeature.Noon, item.GetProperty("retiredAt").GetDateTime());
        Assert.Contains(older, feature.Profiles.Profiles);
    }

    [Fact(DisplayName = "the profile a destination encodes with unless another is asked for is not removed")]
    public async Task TheProfileADestinationEncodesWithIsNotRemoved()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);

        (HttpStatusCode status, JsonElement body) = await feature.DeleteAsync($"/api/encoding/profiles/{profile.Id.Value}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains(destination.Id.Wire, body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Single(feature.Profiles.Profiles);
    }

    [Fact(DisplayName = "a definition nobody defined answers that it is not there")]
    public async Task ADefinitionNobodyDefinedIsNotThere()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode removed, _) = await feature.DeleteAsync($"/api/encoding/profiles/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, removed);
    }

    [Fact(DisplayName = "an id of nothing but zeroes names no definition")]
    public async Task AnIdOfNothingButZeroesNamesNoDefinition()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode removed, _) = await feature.DeleteAsync($"/api/encoding/profiles/{Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, removed);
    }

    [Fact(DisplayName = "no job is deleted, and what the two definition surfaces reach is the definition and nothing the ledger holds")]
    public async Task NoJobIsDeletedAndTheDefinitionSurfacesReachNothingTheLedgerHolds()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeProfile spare = feature.Defined("Nobody asked for this");
        EncodeJob job = feature.Queued(feature.Recorded(), profile, destination);

        using HttpResponseMessage jobs = await feature.Client.DeleteAsync(new Uri($"/api/encoding/jobs/{job.Id.Value}", UriKind.Relative));
        (HttpStatusCode removed, _) = await feature.DeleteAsync($"/api/encoding/profiles/{spare.Id.Value}");

        Assert.Equal(HttpStatusCode.NotFound, jobs.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removed);
        Assert.Single(feature.Jobs.Jobs);
        Assert.Equal([profile], feature.Profiles.Profiles);
        Assert.Single(feature.Destinations.Destinations);
    }

    private static IEnumerable<JsonElement> Numbers(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Numbers(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Numbers),
        JsonValueKind.Number => [element],
        _ => [],
    };
}
