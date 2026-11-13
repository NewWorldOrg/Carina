using System.Net;

using Carina.Api.Playback;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Feature.Thumbnails;

[Trait("Category", "Feature")]
public sealed class ThumbnailDeliveryTests
{
    [Fact]
    public async Task ThePictureDrawnOfARecordingComesBackAsAJpeg()
    {
        await using var feature = new DrawnFeature();
        Recording recording = feature.Illustrated(ThumbnailState.Ready);

        using HttpResponseMessage answer = await feature.GetAsync(recording);
        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(ThumbnailDelivery.MediaType, answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(DrawnFeature.APicture, body);
        Assert.Equal("ready", Header(answer, ThumbnailDelivery.State));
    }

    [Theory]
    [InlineData(ThumbnailState.Pending)]
    [InlineData(ThumbnailState.Skipped)]
    [InlineData(ThumbnailState.Failed)]
    public async Task ARecordingWithNoPictureSaysWhichOfThoseItIsRatherThanHandingBackNothing(ThumbnailState state)
    {
        await using var feature = new DrawnFeature();
        Recording recording = feature.Illustrated(state, onDisk: false);

        using HttpResponseMessage answer = await feature.GetAsync(recording);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Equal(
            System.Text.Json.JsonNamingPolicy.CamelCase.ConvertName(state.ToString()),
            Header(answer, ThumbnailDelivery.State));
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task APictureTheLedgerSaysIsThereButIsNotSaysItIsOutOfReach()
    {
        await using var feature = new DrawnFeature();
        Recording recording = feature.Illustrated(ThumbnailState.Ready, onDisk: false);

        using HttpResponseMessage answer = await feature.GetAsync(recording);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
    }

    [Fact]
    public async Task WithNowhereToKeepPicturesNoRecordingHasOne()
    {
        await using var feature = new DrawnFeature(anywhereToPutThem: false);
        Recording recording = feature.Illustrated(ThumbnailState.Ready, onDisk: false);

        using HttpResponseMessage answer = await feature.GetAsync(recording);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
    }

    [Fact]
    public async Task ARecordingNobodyHasIsNotFound()
    {
        await using var feature = new DrawnFeature();

        using HttpResponseMessage answer = await feature.Client.GetAsync(
            new Uri($"/api/videos/{RecordingId.New().Wire}/thumbnail", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact]
    public async Task SomethingThatIsNotARecordingIdIsRefusedBeforeAnythingIsLookedFor()
    {
        await using var feature = new DrawnFeature();

        using HttpResponseMessage answer = await feature.Client.GetAsync(
            new Uri("/api/videos/not-an-id/thumbnail", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Fact]
    public async Task AClientCarryingNoCredentialsIsRefusedRatherThanSentToASignInScreen()
    {
        await using var feature = new DrawnFeature();
        Recording recording = feature.Illustrated(ThumbnailState.Ready);

        using HttpResponseMessage answer = await feature.Stranger.GetAsync(
            new Uri($"/api/videos/{recording.Id.Wire}/thumbnail", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task NothingHandedBackSaysWhereOnThisMachineThePictureIs()
    {
        await using var feature = new DrawnFeature();
        Recording recording = feature.Illustrated(ThumbnailState.Ready, onDisk: false);

        using HttpResponseMessage answer = await feature.GetAsync(recording);

        Assert.DoesNotContain(
            answer.Headers.Concat(answer.Content.Headers),
            header => header.Value.Any(value => value.Contains(feature.PicturesAt, StringComparison.Ordinal)));
        Assert.DoesNotContain(
            feature.PicturesAt,
            await answer.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    private static string? Header(HttpResponseMessage answer, string named)
        => answer.Headers.TryGetValues(named, out IEnumerable<string>? values) ? values.Single() : null;
}
