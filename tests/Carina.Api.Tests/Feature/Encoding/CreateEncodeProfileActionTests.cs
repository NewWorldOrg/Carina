using System.Net;
using System.Text.Json;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class CreateEncodeProfileActionTests
{
    [Fact(DisplayName = "a profile is defined out of enumerated values and numbers, and comes back on the list")]
    public async Task AProfileIsDefinedAndComesBackOnTheList()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PostAsync("/api/encoding/profiles", new
        {
            label = "Viewing",
            codec = "h264",
            resolution = "asSource",
            deinterlace = "everyFrame",
            rateFactor = 22,
            quantiser = 24,
        });
        (_, JsonElement listed) = await feature.GetAsync("/api/encoding/profiles");
        JsonElement item = Assert.Single(listed.GetProperty("data").GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal("Viewing", body.GetProperty("data").GetProperty("label").GetString());
        Assert.Equal("h264", item.GetProperty("codec").GetString());
        Assert.Equal("everyFrame", item.GetProperty("deinterlace").GetString());
        Assert.Equal(22, item.GetProperty("rateFactor").GetInt32());
        Assert.Equal(body.GetProperty("data").GetProperty("id").GetGuid(), item.GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("codec", "av1")]
    [InlineData("codec", "9")]
    [InlineData("rateFactor", "52")]
    [InlineData("quantiser", "-1")]
    [InlineData("label", "\"\"")]
    public async Task AProfileOutsideTheEnumeratedValuesIsRefusedNamingTheField(string field, string value)
    {
        await using var feature = new EncodingFeature();
        string json = $$$"""{"label":"Viewing","codec":"h264","resolution":"hd","deinterlace":"leave","rateFactor":22,"quantiser":24,"{{{field}}}":{{{(value.StartsWith('"') ? value : value == "av1" ? "\"av1\"" : value)}}}}""";
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await feature.Client.PostAsync(new Uri("/api/encoding/profiles", UriKind.Relative), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(feature.Profiles.Profiles);
    }


    private static IEnumerable<JsonElement> Numbers(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(property => Numbers(property.Value)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(Numbers),
        JsonValueKind.Number => [element],
        _ => [],
    };
}
