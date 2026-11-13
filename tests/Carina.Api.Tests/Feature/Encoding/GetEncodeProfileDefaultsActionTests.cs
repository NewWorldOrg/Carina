using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Machines;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class GetEncodeProfileDefaultsActionTests
{
    [Fact(DisplayName = "a new profile starts at H.264 on a machine that writes H.265 nowhere, and at the source's size, every field undone, rate factor 20 and quantiser 26")]
    public async Task ANewProfileStartsAtH264WhereH265IsWrittenNowhere()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/profiles/defaults");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["codec", "resolution", "deinterlace", "rateFactor", "quantiser"], data.EnumerateObject().Select(field => field.Name));
        Assert.Equal("h264", data.GetProperty("codec").GetString());
        Assert.Equal("asSource", data.GetProperty("resolution").GetString());
        Assert.Equal("everyField", data.GetProperty("deinterlace").GetString());
        Assert.Equal(EncodeProfileDefaults.RateFactor, data.GetProperty("rateFactor").GetInt32());
        Assert.Equal(EncodeProfileDefaults.Quantiser, data.GetProperty("quantiser").GetInt32());
    }

    [Fact(DisplayName = "a new profile starts at H.265 on a machine that writes it, with the same rates")]
    public async Task ANewProfileStartsAtH265WhereItIsWritten()
    {
        await using var feature = new EncodingFeature(can: MachineCapabilities.Of(
            CardStanding.Usable,
            [Faculty.EncodeH264OnTheProcessor, Faculty.EncodeH264OnTheCard, Faculty.EncodeH265OnTheCard],
            string.Empty));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync("/api/encoding/profiles/defaults");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("h265", data.GetProperty("codec").GetString());
        Assert.Equal(EncodeProfileDefaults.RateFactor, data.GetProperty("rateFactor").GetInt32());
        Assert.Equal(EncodeProfileDefaults.Quantiser, data.GetProperty("quantiser").GetInt32());
    }

    [Fact(DisplayName = "what a new profile starts at is read without defining anything")]
    public async Task WhatANewProfileStartsAtIsReadWithoutDefiningAnything()
    {
        await using var feature = new EncodingFeature();

        await feature.GetAsync("/api/encoding/profiles/defaults");

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
