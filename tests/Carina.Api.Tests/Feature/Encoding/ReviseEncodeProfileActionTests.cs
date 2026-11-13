using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class ReviseEncodeProfileActionTests
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

    [Fact(DisplayName = "a profile is changed in every field at once and the list answers with what it now says")]
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

    [Fact(DisplayName = "what a change leaves out is refused, so a change cannot reach a shape a definition could not")]
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

    [Theory(DisplayName = "a change and a definition are refused in the same words")]
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

    [Fact(DisplayName = "a profile a waiting job names does not move")]
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

    [Fact(DisplayName = "a retired profile is not changed, because what was encoded with it must keep reading true")]
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
}
