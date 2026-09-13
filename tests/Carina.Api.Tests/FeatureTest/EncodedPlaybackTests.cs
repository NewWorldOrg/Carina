using System.Net;
using System.Text.Json;

using Carina.Api.Playback;
using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

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
    public async Task AnArtefactCarriesTheOneSoundItWasEncodedWithSoThePlanNamesNoneToChooseFrom()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        feature.Player.Sounds = 2;

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Empty(read.GetProperty("sounds").EnumerateArray());
    }

    [Fact]
    public async Task AskingAnArtefactForASecondSoundIsRefusedRatherThanQuietlyGivingTheOneItHas()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.BadRequest, picture.StatusCode);
        Assert.Equal(
            PlayDelivery.NothingToChooseFrom,
            (await PlayFeature.PlanOfAsync(picture)).GetProperty("message").GetString());
        Assert.Null(feature.Player.Handed);
    }

    [Fact]
    public async Task AskingAnArtefactForTheMainSoundHandsItOverAsItAlwaysWas()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=main");

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "BR-PD-008: an encoded recording of a broadcast that announced two sounds still offers both of them")]
    public async Task ThePlanOfAnEncodedRecordingStillNamesTheSoundsTheBroadcastAnnounced()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        feature.Encoded(recording);
        feature.Player.SoundsCannotBeRead = "the stream is never asked when the broadcast announced its sound";

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal(
            ["main", "secondary"],
            read.GetProperty("sounds").EnumerateArray().Select(sound => sound.GetString()!).ToArray());
        Assert.Equal(0, feature.Player.AskedWhatItCarries);
    }

    [Fact(DisplayName = "BR-PD-008: the secondary sound of an encoded recording is transcoded from the recording, because the artefact was baked with the main one")]
    public async Task AskingAnEncodedRecordingForItsSecondSoundGoesBackToTheRecordingItself()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=secondary");
        byte[] body = await picture.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Equal([SoundPlacement.OneChannelOf(0, SoundChannel.Right)], feature.Player.AskedWith);
        Assert.Equal(feature.Player.Picture, body);
        Assert.NotEqual(artefact, body);
    }

    [Fact(DisplayName = "BR-PD-008: the main sound of an encoded recording is the artefact itself, whatever the broadcast announced")]
    public async Task AskingAnEncodedRecordingForItsMainSoundHandsOverTheArtefact()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=main");

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact]
    public async Task ThePlanOfAnEncodedRecordingAskedForItsSecondSoundIsThePlanOfATranscode()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        feature.Encoded(recording);

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?sound=secondary"))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.True(read.GetProperty("transcodes").GetBoolean());
        Assert.False(read.GetProperty("canSeek").GetBoolean());
        Assert.Equal("byStartingAgain", read.GetProperty("seeking").GetString());
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
