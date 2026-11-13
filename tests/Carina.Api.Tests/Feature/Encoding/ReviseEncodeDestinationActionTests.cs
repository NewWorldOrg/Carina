using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class ReviseEncodeDestinationActionTests
{
    [Fact(DisplayName = "a destination a waiting job names does not move")]
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

    [Fact(DisplayName = "a destination cannot name a retired profile as the one it encodes with")]
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

    [Fact(DisplayName = "the one destination left is put right by changing it, since it cannot be removed")]
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

    [Fact(DisplayName = "a destination that names a root nobody declares is refused when it is changed as well")]
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

    [Fact(DisplayName = "while the driver cannot say what it declares, no destination is changed")]
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
}
