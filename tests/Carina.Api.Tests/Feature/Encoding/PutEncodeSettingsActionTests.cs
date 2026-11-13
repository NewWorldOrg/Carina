using System.Net;
using System.Text.Json;

using Carina.Domain.Encodings;

using Puppis.Contracts;

namespace Carina.Api.Tests.Feature.Encoding;

[Trait("Category", "Feature")]
public sealed class PutEncodeSettingsActionTests
{
    [Fact(DisplayName = "settling the auto-run writes one row, answers it as settled, and signals")]
    public async Task SettlingTheAutoRunWritesOneRowAndSignals()
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            "/api/encoding/settings",
            new { automatically = false, mostCores = 4 });
        (_, JsonElement read) = await feature.GetAsync("/api/encoding/settings");
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.False(data.GetProperty("automatically").GetBoolean());
        Assert.Equal(4, data.GetProperty("mostCores").GetInt32());
        Assert.True(data.GetProperty("stored").GetBoolean());
        Assert.Equal(EncodingFeature.Noon, data.GetProperty("updatedAt").GetDateTime());
        Assert.Equal(4, read.GetProperty("data").GetProperty("mostCores").GetInt32());
        Assert.Equal(1, feature.AutoRun.Saves);
        Assert.Contains(AppEventName.EncodeJobs, feature.Events.Signalled);
    }

    [Fact(DisplayName = "turning the auto-run off stops making jobs, and leaves the one already running alone")]
    public async Task TurningTheAutoRunOffLeavesTheJobAlreadyRunningAlone()
    {
        await using var feature = new EncodingFeature();
        EncodeProfile profile = feature.Defined();
        EncodeDestination destination = feature.Placed(profile);
        EncodeJob running = feature.Queued(feature.Recorded(), profile, destination);
        running.Start(EncodingFeature.Noon.AddMinutes(-20));
        EncodeJob waiting = feature.Queued(feature.Recorded(), profile, destination);

        (HttpStatusCode status, _) = await feature.PutAsync(
            "/api/encoding/settings",
            new { automatically = false, mostCores = 2 });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(EncodeJobStatus.Running, running.Status);
        Assert.Equal(EncodeJobStatus.Queued, waiting.Status);
        Assert.Empty(feature.Jobs.Moves);
        Assert.Empty(feature.Strays.Stopped);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(7)]
    public async Task ACapOutsideWhatThisMachineHasIsRefusedNamingTheField(int mostCores)
    {
        await using var feature = new EncodingFeature();

        (HttpStatusCode status, JsonElement body) = await feature.PutAsync(
            "/api/encoding/settings",
            new { automatically = true, mostCores });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("mostCores", body.GetProperty("message").GetString()!, StringComparison.Ordinal);
        Assert.Equal(0, feature.AutoRun.Saves);
    }

    [Theory]
    [InlineData("""{"mostCores":2}""", "automatically")]
    [InlineData("""{"automatically":true}""", "mostCores")]
    [InlineData("{}", "automatically")]
    public async Task SettlingHalfOfItIsRefusedNamingWhatIsMissing(string json, string named)
    {
        await using var feature = new EncodingFeature();
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await feature.Client.PutAsync(
            new Uri("/api/encoding/settings", UriKind.Relative),
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(named, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(0, feature.AutoRun.Saves);
    }
}
