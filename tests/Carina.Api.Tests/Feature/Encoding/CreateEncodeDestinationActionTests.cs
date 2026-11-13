using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class CreateEncodeDestinationActionTests
{
    [Fact(DisplayName = "a destination names a root out of the declared set that this process holds, and comes back on the list")]
    public async Task ADestinationNamingAHeldRootIsDefined()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/destinations", new
        {
            label = "Shelf",
            outputRoot = "encodes",
            defaultProfileId = profile.Id.Value,
        });
        (_, JsonElement listed) = await feature.GetAsync("/api/encoding/destinations");

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("encodes", body.GetProperty("data").GetProperty("outputRoot").GetString());
        Assert.Equal(profile.Id.Value, body.GetProperty("data").GetProperty("defaultProfileId").GetGuid());
        Assert.Single(listed.GetProperty("data").GetProperty("items").EnumerateArray());
    }

    [Fact(DisplayName = "a destination naming the root the recordings are read from is refused at saving")]
    public async Task ADestinationNamingTheRootTheRecordingsAreReadFromIsRefused()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/destinations", new
        {
            label = "Wrong shelf",
            outputRoot = "primary",
            defaultProfileId = profile.Id.Value,
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("holds for writing", body.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(feature.Destinations.Destinations);
    }

    [Fact(DisplayName = "a destination naming a root nobody declares is refused")]
    public async Task ADestinationNamingARootNobodyDeclaresIsRefused()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/destinations", new
        {
            label = "Nowhere",
            outputRoot = "elsewhere",
            defaultProfileId = profile.Id.Value,
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("outputRoot", body.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "while the driver cannot say what it declares, no destination is saved")]
    public async Task WhileTheDriverCannotSayWhatItDeclaresNoDestinationIsSaved()
    {
        await using var feature = new EncodingFeature();
        feature.Driver.Unreachable = "no socket at that path";
        EncodeProfile profile = feature.Defined();

        (HttpStatusCode status, _) = await feature.PostAsync("/api/encoding/destinations", new
        {
            label = "Shelf",
            outputRoot = "encodes",
            defaultProfileId = profile.Id.Value,
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Empty(feature.Destinations.Destinations);
    }


    private static IEnumerable<JsonElement> Numbers(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Numbers(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Numbers),
        JsonValueKind.Number => [element],
        _ => [],
    };
}
