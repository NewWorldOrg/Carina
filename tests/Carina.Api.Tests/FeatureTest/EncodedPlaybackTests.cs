using System.Net;
using System.Text.Json;

using Carina.Api.Playback;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class EncodedPlaybackTests
{
    [Fact]
    public async Task AnArtefactOnTheDiskIsWhatComesBackAndNothingIsTranscodedWhilePlaying()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage plan = await feature.PlanAsync(recording);
        JsonElement read = (await PlayFeature.PlanOfAsync(plan)).GetProperty("data");
        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.False(read.GetProperty("transcodes").GetBoolean());
        Assert.True(read.GetProperty("canSeek").GetBoolean());
        Assert.Equal("video/mp4", read.GetProperty("mediaType").GetString());
        Assert.Equal(artefact.Length, read.GetProperty("bytes").GetInt64());

        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact]
    public async Task AnArtefactIsMovedAboutByAskingForARangeOfItRatherThanByStartingAgain()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording, bytes: 2_000);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, string.Empty, "bytes=1000-1099");

        Assert.Equal(HttpStatusCode.PartialContent, picture.StatusCode);
        Assert.Equal("byRange", Header(picture, PlaybackHeaders.Seeking));
        Assert.Equal("bytes", Assert.Single(picture.Headers.AcceptRanges));
        Assert.Equal(artefact[1_000..1_100], await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact]
    public async Task OfTwoArtefactsOfOneRecordingTheOneMadeLastIsPlayed()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, bytes: 700);
        byte[] later = feature.Encoded(recording, minutesLater: 90, bytes: 1_300);

        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(later, await picture.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AnArtefactTheLedgerNamesAndTheDiskHasNotIsTranscodedInsteadAndSaidToHaveBeen()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, onDisk: false);

        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Equal("encodedFileGone", Header(picture, PlaybackHeaders.FellBack));
        Assert.Equal(feature.Player.Picture, await picture.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AnArtefactABrowserWouldNotDecodeAsItIsIsLeftToTheTranscoder()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265);

        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Null(Header(picture, PlaybackHeaders.FellBack));
    }

    [Fact]
    public async Task NothingSaysAPlanFellBackWhenTheArtefactItNamesIsThere()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Null(Header(picture, PlaybackHeaders.FellBack));
    }

    [Fact]
    public async Task TheFileOfARecordingWithAnArtefactIsTheArtefactAndItIsHandedOverAsAnMp4()
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(4_000));
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage answer = await feature.GetAsync(recording);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("video/mp4", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(artefact, await answer.Content.ReadAsByteArrayAsync());
    }

    private static string? Header(HttpResponseMessage answer, string named)
        => answer.Headers.TryGetValues(named, out IEnumerable<string>? values) ? values.Single() : null;
}
