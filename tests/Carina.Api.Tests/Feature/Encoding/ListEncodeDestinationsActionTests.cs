using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class ListEncodeDestinationsActionTests
{
    [Fact(DisplayName = "every destination defined is listed with the root it writes to and the profile it encodes with")]
    public async Task EveryDestinationDefinedIsListedWithItsRootAndItsProfile()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination shelf = feature.Placed(profile);
        EncodeDestination beside = feature.Placed(profile, EncodingFeature.Primary);

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/destinations");
        JsonElement[] items = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];
        JsonElement listed = Assert.Single(items, item => item.GetProperty("id").GetGuid() == shelf.Id.Value);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            new HashSet<Guid> { shelf.Id.Value, beside.Id.Value },
            items.Select(item => item.GetProperty("id").GetGuid()).ToHashSet());
        Assert.Equal("Shelf", listed.GetProperty("label").GetString());
        Assert.Equal("encodes", listed.GetProperty("outputRoot").GetString());
        Assert.Equal(profile.Id.Value, listed.GetProperty("defaultProfileId").GetGuid());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("retiredAt").ValueKind);
    }

    [Fact(DisplayName = "with no destination defined the list is empty")]
    public async Task WithNoDestinationDefinedTheListIsEmpty()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/destinations");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(body.GetProperty("data").GetProperty("items").EnumerateArray());
    }
}
