using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class EncodingDefinitionChangeTests
{
    private static object AProfile(string label = "Viewing", int rateFactor = 20) => new
    {
        label,
        codec = "h264",
        resolution = "asSource",
        deinterlace = "everyFrame",
        rateFactor,
        quantiser = 24,
    };

    [Fact(DisplayName = "BR-EV-006: a profile is changed in every field at once and the list answers with what it now says")]
    public async Task AProfileIsChangedInEveryFieldAtOnce()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            $"/api/encoding/profiles/{profile.Id.Value}",
            AProfile("Viewing, finer", 18));
        (_, JsonElement listed) = await feature.GetAsync("/api/encoding/profiles");
        JsonElement item = Assert.Single(listed.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Viewing, finer", body.GetProperty("data").GetProperty("label").GetString());
        Assert.Equal(18, item.GetProperty("rateFactor").GetInt32());
        Assert.Equal(profile.Id.Value, item.GetProperty("id").GetGuid());
        Assert.Null(item.GetProperty("retiredAt").GetString());
    }

    [Fact(DisplayName = "BR-EV-006: what a change leaves out is refused, so a change cannot reach a shape a definition could not")]
    public async Task AChangeThatLeavesAFieldOutIsRefused()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();

        (HttpStatusCode status, _) = await feature.PatchAsync(
            $"/api/encoding/profiles/{profile.Id.Value}",
            new { label = "Viewing, finer" });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(22, feature.Profiles.Profiles.Single().SoftwareRateControl.RateFactor);
    }

    [Theory(DisplayName = "BR-EV-006: a change and a definition are refused in the same words")]
    [InlineData("rateFactor", "52")]
    [InlineData("quantiser", "-1")]
    [InlineData("label", "\"\"")]
    public async Task AChangeAndADefinitionAreRefusedInTheSameWords(string field, string value)
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        string json =
            $$$"""{"label":"Viewing","codec":"h264","resolution":"hd","deinterlace":"leave","rateFactor":22,"quantiser":24,"{{{field}}}":{{{value}}}}""";

        using var defining = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var changing = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage defined = await feature.Client.PostAsync(
            new Uri("/api/encoding/profiles", UriKind.Relative),
            defining);
        using HttpResponseMessage changed = await feature.Client.PatchAsync(
            new Uri($"/api/encoding/profiles/{profile.Id.Value}", UriKind.Relative),
            changing);

        Assert.Equal(HttpStatusCode.BadRequest, defined.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        Assert.Equal(await defined.Content.ReadAsStringAsync(), await changed.Content.ReadAsStringAsync());
    }

    [Fact(DisplayName = "BR-ES-003: a profile a waiting job names does not move")]
    public async Task AProfileAWaitingJobNamesDoesNotMove()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob waiting = feature.Queued(feature.Recorded(), profile, destination);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            $"/api/encoding/profiles/{profile.Id.Value}",
            AProfile("Viewing, finer", 18));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains(waiting.Id.Wire, body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(22, feature.Profiles.Profiles.Single().SoftwareRateControl.RateFactor);
    }

    [Fact(DisplayName = "BR-ES-003: a profile a running job names is not taken away either")]
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

    [Fact(DisplayName = "BR-ES-003: a destination a waiting job names does not move")]
    public async Task ADestinationAWaitingJobNamesDoesNotMove()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        feature.Queued(feature.Recorded(), profile, destination);

        (HttpStatusCode status, _) = await feature.PatchAsync(
            $"/api/encoding/destinations/{destination.Id.Value}",
            new { label = "Another shelf", outputRoot = "encodes", defaultProfileId = profile.Id.Value });

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("Shelf", feature.Destinations.Destinations.Single().Label.Value);
    }

    [Fact(DisplayName = "BR-ED2-015: a profile no job names is gone from the ledger when it is removed")]
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

    [Fact(DisplayName = "BR-ED2-015: a profile an artefact was made with is retired rather than removed, and still answers by id")]
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

    [Fact(DisplayName = "BR-ED2-015: a retired profile is not changed, because what was encoded with it must keep reading true")]
    public async Task ARetiredProfileIsNotChanged()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile standing = feature.Defined();
        EncodeDestination destination = feature.Placed(standing);
        EncodeProfile older = feature.Defined("What last month was encoded with");
        feature.Completed(feature.Recorded(), older, destination);
        await feature.DeleteAsync($"/api/encoding/profiles/{older.Id.Value}");

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            $"/api/encoding/profiles/{older.Id.Value}",
            AProfile("Back from the dead", 18));
        (HttpStatusCode again, _) = await feature.DeleteAsync($"/api/encoding/profiles/{older.Id.Value}");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Contains("retired", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, again);
    }

    [Fact(DisplayName = "BR-ED2-015: a retired destination takes no new job")]
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

    [Fact(DisplayName = "BR-ED2-016: the profile a destination encodes with unless another is asked for is not removed")]
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

    [Fact(DisplayName = "BR-ED2-016: a destination cannot name a retired profile as the one it encodes with")]
    public async Task ADestinationCannotNameARetiredProfile()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile standing = feature.Defined();
        EncodeDestination destination = feature.Placed(standing);
        EncodeProfile older = feature.Defined("What last month was encoded with");
        feature.Completed(feature.Recorded(), older, destination);
        await feature.DeleteAsync($"/api/encoding/profiles/{older.Id.Value}");

        (HttpStatusCode changed, JsonElement body) = await feature.PatchAsync(
            $"/api/encoding/destinations/{destination.Id.Value}",
            new { label = "Shelf", outputRoot = "encodes", defaultProfileId = older.Id.Value });
        (HttpStatusCode defined, _) = await feature.PostAsync("/api/encoding/destinations", new
        {
            label = "Another shelf",
            outputRoot = "encodes",
            defaultProfileId = older.Id.Value,
        });

        Assert.Equal(HttpStatusCode.BadRequest, changed);
        Assert.Contains("still offered", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.BadRequest, defined);
    }

    [Fact(DisplayName = "BR-ED2-016: the last destination left is not removed, because a machine with nowhere to write encodes nothing")]
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

    [Fact(DisplayName = "BR-ED2-016: the one destination left is put right by changing it, since it cannot be removed")]
    public async Task TheOneDestinationLeftIsPutRightByChangingIt()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination only = feature.Placed(profile);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            $"/api/encoding/destinations/{only.Id.Value}",
            new { label = "The shelf, spelled right", outputRoot = "encodes", defaultProfileId = profile.Id.Value });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("The shelf, spelled right", body.GetProperty("data").GetProperty("label").GetString());
        Assert.Equal("The shelf, spelled right", feature.Destinations.Destinations.Single().Label.Value);
    }

    [Fact(DisplayName = "BR-ED2-015: a destination no job names is gone from the ledger when it is removed")]
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

    [Fact(DisplayName = "BR-EV-006: a destination that names a root nobody declares is refused when it is changed as well")]
    public async Task ADestinationChangedToARootNobodyDeclaresIsRefused()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);

        (HttpStatusCode status, JsonElement body) = await feature.PatchAsync(
            $"/api/encoding/destinations/{destination.Id.Value}",
            new { label = "Nowhere", outputRoot = "elsewhere", defaultProfileId = profile.Id.Value });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("outputRoot", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal("encodes", feature.Destinations.Destinations.Single().OutputRoot.Value);
    }

    [Fact(DisplayName = "BR-EV-006: while the driver cannot say what it declares, no destination is changed")]
    public async Task WhileTheDriverCannotSayWhatItDeclaresNoDestinationIsChanged()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        feature.Driver.Unreachable = "no socket at that path";

        (HttpStatusCode status, _) = await feature.PatchAsync(
            $"/api/encoding/destinations/{destination.Id.Value}",
            new { label = "Another shelf", outputRoot = "encodes", defaultProfileId = profile.Id.Value });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("Shelf", feature.Destinations.Destinations.Single().Label.Value);
    }

    [Theory(DisplayName = "BR-EA2-001: a definition nobody defined answers that it is not there")]
    [InlineData("profiles")]
    [InlineData("destinations")]
    public async Task ADefinitionNobodyDefinedIsNotThere(string surface)
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode removed, _) = await feature.DeleteAsync($"/api/encoding/{surface}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, removed);
    }

    [Theory(DisplayName = "BR-EA2-001: an id of nothing but zeroes names no definition")]
    [InlineData("profiles")]
    [InlineData("destinations")]
    public async Task AnIdOfNothingButZeroesNamesNoDefinition(string surface)
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode removed, _) = await feature.DeleteAsync($"/api/encoding/{surface}/{Guid.Empty}");

        Assert.Equal(HttpStatusCode.BadRequest, removed);
    }
}
