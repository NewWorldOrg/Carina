using System.Net;
using System.Text.Json;

namespace Carina.Api.Tests.FeatureTest;

public sealed class UnreadableInputRefusalTests
{
    [Theory]
    [InlineData("/api/recordings?standing=sideways", "sideways")]
    [InlineData("/api/recordings?outcome=nearly", "nearly")]
    [InlineData("/api/recordings?drops=maybe", "maybe")]
    [InlineData("/api/recordings?sort=alphabetically", "alphabetically")]
    [InlineData("/api/recordings?perPage=abc", "abc")]
    [InlineData("/api/recordings?from=whenever", "whenever")]
    [InlineData("/api/reservations?sort=sideways", "sideways")]
    [InlineData("/api/live/channels?fields=everything", "everything")]
    [InlineData("/api/quality/channels?sort=sideways", "sideways")]
    [InlineData("/api/encoding/jobs?status=nearly", "nearly")]
    public async Task AValueAnEndpointCannotReadIsRefusedInTheEnvelopeWithoutRepeatingWhatWasSent(
        string path,
        string sent)
    {
        await using var feature = new RecordingFeature();

        string body = await feature.GetTextAsync(path);
        (HttpStatusCode status, JsonElement answer) = await feature.GetAsync(path);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.False(answer.GetProperty("status").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(answer.GetProperty("message").GetString()));
        Assert.DoesNotContain(sent, body, StringComparison.OrdinalIgnoreCase);
    }
}
