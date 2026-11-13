using System.Net;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Feature.Videos;

[Trait("Category", "Feature")]
public sealed class IssueVideoTicketActionTests
{
    [Fact]
    public async Task AskingForATicketWithAFormRatherThanJsonIsRefused()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AskForATicketAsync(
            recording.Id,
            "application/x-www-form-urlencoded");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, answer.StatusCode);
    }

    [Fact]
    public async Task NobodyWithoutASessionIsGivenATicket()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using var content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        using HttpResponseMessage answer = await feature.Player.PostAsync(
            new Uri($"/api/videos/{recording.Id.Wire}/ticket", UriKind.Relative),
            content);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
    }

    [Fact]
    public async Task NoTicketIsIssuedForARecordingThatIsStillBeingWritten()
    {
        await using var feature = new TicketedFeature();

        using HttpResponseMessage answer = await feature.AskForATicketAsync(feature.StillWriting().Id);

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
    }

    [Fact]
    public async Task NoTicketIsIssuedForARecordingNobodyHas()
    {
        await using var feature = new TicketedFeature();

        using HttpResponseMessage answer = await feature.AskForATicketAsync(RecordingId.New());

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }
}
