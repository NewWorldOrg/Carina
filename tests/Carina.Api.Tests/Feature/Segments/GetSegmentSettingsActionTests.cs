using System.Net;
using System.Text.Json;

namespace Carina.Api.Tests.Feature.Segments;

[Trait("Category", "Feature")]
public sealed class GetSegmentSettingsActionTests
{
    [Fact(DisplayName = "with no row held the threshold reads at 0.70, and nobody has changed it")]
    public async Task WithNoRowHeldTheThresholdReadsAtItsDefault()
    {
        await using var feature = new SegmentSettingsFeature();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync();
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["skipThreshold", "skipThresholdChangedAt"], data.EnumerateObject().Select(field => field.Name).ToArray());
        Assert.Equal(0.70, data.GetProperty("skipThreshold").GetDouble());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("skipThresholdChangedAt").ValueKind);
        Assert.Equal(0, feature.Settings.Saves);
    }

    [Fact(DisplayName = "a caller who has not signed in cannot read the settings")]
    public async Task ACallerWhoHasNotSignedInCannotRead()
    {
        await using var feature = new SegmentSettingsFeature();
        using HttpClient anonymous = feature.Anonymous();

        using HttpResponseMessage read = await anonymous.GetAsync(new Uri(SegmentSettingsFeature.Path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(0, feature.Settings.Reads);
        Assert.Equal(0, feature.Settings.Saves);
    }
}
