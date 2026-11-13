using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class ListEncodeProfilesActionTests
{
    [Fact(DisplayName = "every profile defined is listed with what it encodes with")]
    public async Task EveryProfileDefinedIsListedWithWhatItEncodesWith()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile viewing = feature.Defined("Viewing");
        EncodeProfile shelf = feature.Defined("Shelf");

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/profiles");
        JsonElement[] items = [.. body.GetProperty("data").GetProperty("items").EnumerateArray()];
        JsonElement listed = Assert.Single(items, item => item.GetProperty("id").GetGuid() == viewing.Id.Value);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            new HashSet<Guid> { viewing.Id.Value, shelf.Id.Value },
            items.Select(item => item.GetProperty("id").GetGuid()).ToHashSet());
        Assert.Equal("Viewing", listed.GetProperty("label").GetString());
        Assert.Equal("h264", listed.GetProperty("codec").GetString());
        Assert.Equal("asSource", listed.GetProperty("resolution").GetString());
        Assert.Equal("everyFrame", listed.GetProperty("deinterlace").GetString());
        Assert.Equal(22, listed.GetProperty("rateFactor").GetInt32());
        Assert.Equal(24, listed.GetProperty("quantiser").GetInt32());
        Assert.Equal(JsonValueKind.Null, listed.GetProperty("retiredAt").ValueKind);
    }

    [Fact(DisplayName = "with no profile defined the list is empty")]
    public async Task WithNoProfileDefinedTheListIsEmpty()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/profiles");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(body.GetProperty("data").GetProperty("items").EnumerateArray());
    }
}
