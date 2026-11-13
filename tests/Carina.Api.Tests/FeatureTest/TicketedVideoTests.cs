using System.Net;
using System.Text.Json;

using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

public sealed class TicketedVideoTests
{
    [Fact]
    public async Task ATicketIsIssuedForOneRecordingAndSaysWhenItDies()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AskForATicketAsync(recording.Id);
        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(43, read.RootElement.GetProperty("data").GetProperty("inTheClear").GetString()!.Length);
        Assert.True(read.RootElement.GetProperty("data").TryGetProperty("lapsesAt", out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/play")]
    [InlineData("/thumbnail")]
    [InlineData("/scrub")]
    [InlineData("/captions")]
    [InlineData("/data-broadcast")]
    [InlineData("/data-broadcast/modules/64/7/0/1")]
    public async Task EverySurfaceUnderThisPrefixRefusesAStrangerWithTheSameStatus(string beneath)
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}{beneath}",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
    }
}
