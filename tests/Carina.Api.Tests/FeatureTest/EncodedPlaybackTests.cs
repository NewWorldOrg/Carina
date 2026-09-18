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

    [Fact(DisplayName = "BR-PD-008: when the recording itself is out of reach, the plan of an encoded recording asked for its second sound offers the artefact and the one sound it carries")]
    public async Task ThePlanNarrowsToTheArtefactWhenTheSoundAskedForCannotBeReachedAnyMore()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(
            RecordingOutcome.Complete,
            onDisk: false,
            audio: AudioMode.DualMono,
            sounds: 1);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage plan = await feature.PlanAsync(recording, "?sound=secondary");
        JsonElement read = (await PlayFeature.PlanOfAsync(plan)).GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, plan.StatusCode);
        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.False(read.GetProperty("transcodes").GetBoolean());
        Assert.Equal(artefact.Length, read.GetProperty("bytes").GetInt64());
        Assert.Equal(
            ["main"],
            read.GetProperty("sounds").EnumerateArray().Select(sound => sound.GetString()!).ToArray());
    }

    [Fact(DisplayName = "BR-PD-008: a picture asked for a sound that cannot be reached is refused rather than quietly handed the artefact of another sound")]
    public async Task ThePictureOfASoundThatCannotBeReachedIsRefusedRatherThanQuietlyHandedTheArtefact()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(
            RecordingOutcome.Complete,
            onDisk: false,
            audio: AudioMode.DualMono,
            sounds: 1);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?sound=secondary");

        Assert.Equal(HttpStatusCode.NotFound, picture.StatusCode);
        Assert.NotEqual(artefact, await picture.Content.ReadAsByteArrayAsync());
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

    [Fact]
    public async Task TheChaptersAJobMarkedComeBackWithThePlanOfTheArtefactItMade()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        await feature.MarkedAsync(
            feature.Jobs.Jobs[0],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme),
            new ChapterSegment(TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(150), ChapterKind.Break),
            new ChapterSegment(TimeSpan.FromSeconds(150), TimeSpan.FromSeconds(300), ChapterKind.Programme));

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");
        JsonElement[] chapters = [.. read.GetProperty("chapters").EnumerateArray()];

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal([0d, 90d, 150d], chapters.Select(chapter => chapter.GetProperty("startsAtSec").GetDouble()).ToArray());
        Assert.Equal([90d, 150d, 300d], chapters.Select(chapter => chapter.GetProperty("endsAtSec").GetDouble()).ToArray());
        Assert.Equal(
            ["programme", "break", "programme"],
            chapters.Select(chapter => chapter.GetProperty("kind").GetString()!).ToArray());
    }

    [Fact]
    public async Task TheChaptersThatComeBackBelongToTheArtefactThatIsPlayedRatherThanToAnEarlierOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, bytes: 700);
        feature.Encoded(recording, minutesLater: 90, bytes: 1_300);
        await feature.MarkedAsync(
            feature.Jobs.Jobs[0],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(30), ChapterKind.Break));
        await feature.MarkedAsync(
            feature.Jobs.Jobs[1],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(45), ChapterKind.Programme));

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");
        JsonElement[] chapters = [.. read.GetProperty("chapters").EnumerateArray()];
        JsonElement chapter = Assert.Single(chapters);

        Assert.Equal(45d, chapter.GetProperty("endsAtSec").GetDouble());
        Assert.Equal("programme", chapter.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task ThePlanOfAnArtefactWhoseRunMarkedNothingCarriesAnEmptyListRatherThanNoField()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Empty(read.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "A recording played as it was recorded is answered with no chapters, because the ledger holds them on the artefact's clock")]
    public async Task ThePlanOfARecordingPlayedAsItWasRecordedCarriesNoChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Empty(read.GetProperty("chapters").EnumerateArray());
    }

    [Fact]
    public async Task ThePlanOfAnEncodedRecordingAskedForItsSecondSoundCarriesNoneOfTheArtefactsChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, audio: AudioMode.DualMono, sounds: 1);
        feature.Encoded(recording);
        await feature.MarkedAsync(
            feature.Jobs.Jobs[0],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme));

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?sound=secondary"))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Empty(read.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "A-配信-074: a recording with an artefact asked for as it was recorded hands the recording itself to the transcoder rather than the artefact")]
    public async Task ARecordingAskedForAsItWasRecordedIsTranscodedFromTheRecordingAndNotFromTheArtefact()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, bytes: 3_500);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=recording");
        byte[] body = await picture.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(recording.FileName, Assert.Single(feature.Player.Opened).Name);
        Assert.Equal(PlayFeature.Root, Assert.Single(feature.Player.Opened).Root);
        Assert.Equal(feature.Player.Picture, body);
        Assert.NotEqual(artefact, body);
    }

    [Fact(DisplayName = "A-配信-074: the plan of a recording asked for as it was recorded says it plays the recording and names the artefact as the other one")]
    public async Task ThePlanOfARecordingAskedForAsItWasRecordedNamesTheArtefactAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?source=recording"))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal("artefact", read.GetProperty("alternative").GetString());
        Assert.True(read.GetProperty("transcodes").GetBoolean());
        Assert.False(read.GetProperty("canSeek").GetBoolean());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("bytes").ValueKind);
    }

    [Fact(DisplayName = "A-配信-074: the plan of an encoded recording asked for as it is says it plays the artefact and names the recording itself as the other one")]
    public async Task ThePlanOfAnEncodedRecordingNamesTheRecordingItselfAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal("artefact", read.GetProperty("source").GetString());
        Assert.Equal("recording", read.GetProperty("alternative").GetString());
        Assert.Equal(artefact.Length, read.GetProperty("bytes").GetInt64());
    }

    [Fact(DisplayName = "A-配信-074: asking outright for the artefact is asking for what playing a recording has always given")]
    public async Task AskingOutrightForTheArtefactHandsOverTheArtefact()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=artefact");

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact(DisplayName = "A-配信-074: an artefact the ledger names and the disk has not is not the other one the plan offers")]
    public async Task AnArtefactTheDiskHasNotIsNotOfferedAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, onDisk: false);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
    }

    [Fact(DisplayName = "A-配信-074: an artefact holding no bytes is not the other one the plan offers")]
    public async Task AnArtefactHoldingNoBytesIsNotOfferedAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, bytes: 0);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
    }

    [Fact(DisplayName = "A-配信-074: an artefact a browser would not decode as it is, is not the other one the plan offers")]
    public async Task AnArtefactABrowserWouldNotDecodeIsNotOfferedAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
    }

    [Fact(DisplayName = "A-配信-074: a recording asked for as it was recorded and no longer on the disk is refused rather than quietly handed its artefact")]
    public async Task ARecordingAskedForAsItWasRecordedAndNoLongerOnTheDiskIsRefused()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, onDisk: false);
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=recording");
        using HttpResponseMessage plan = await feature.PlanAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.NotFound, picture.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, plan.StatusCode);
        Assert.NotEqual(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
        Assert.Empty(feature.Player.Opened);
    }

    [Fact(DisplayName = "A-配信-074: a recording asked for as it was recorded with a second sound is not narrowed to the artefact when the recording is gone")]
    public async Task ARecordingAskedForAsItWasRecordedIsNotNarrowedToTheArtefactWhenItsSecondSoundIsAskedFor()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(
            RecordingOutcome.Complete,
            onDisk: false,
            audio: AudioMode.DualMono,
            sounds: 1);
        feature.Encoded(recording);

        using HttpResponseMessage plan = await feature.PlanAsync(recording, "?source=recording&sound=secondary");

        Assert.Equal(HttpStatusCode.NotFound, plan.StatusCode);
        Assert.Null(feature.Player.Handed);
    }

    [Fact(DisplayName = "A-配信-074: a recording asked for as it was recorded and holding no bytes is refused rather than quietly handed its artefact")]
    public async Task ARecordingAskedForAsItWasRecordedAndHoldingNoBytesIsRefused()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Failed, bytes: 0);
        feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.NotFound, picture.StatusCode);
        Assert.Empty(feature.Player.Opened);
    }

    [Fact(DisplayName = "A-配信-074: a recording asked for as it was recorded is moved about by starting again, because it is transcoded while playing")]
    public async Task ARecordingAskedForAsItWasRecordedIsMovedAboutByStartingAgainRatherThanByARange()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, bytes: 2_000);
        feature.Encoded(recording, bytes: 2_000);

        using HttpResponseMessage picture = await feature.PictureAsync(
            recording,
            "?source=recording",
            "bytes=1000-1099");

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("byStartingAgain", Header(picture, PlaybackHeaders.Seeking));
        Assert.Equal("none", Assert.Single(picture.Headers.AcceptRanges));
    }

    [Fact(DisplayName = "A-配信-074: the chapters the ledger holds belong to the artefact, so a recording asked for as it was recorded comes back with none")]
    public async Task ARecordingAskedForAsItWasRecordedComesBackWithNoneOfTheArtefactsChapters()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);
        await feature.MarkedAsync(
            feature.Jobs.Jobs[0],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(90), ChapterKind.Programme));

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?source=recording"))).GetProperty("data");

        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Empty(read.GetProperty("chapters").EnumerateArray());
    }

    [Fact(DisplayName = "A-配信-074: the file of a recording handed to an outside player is the artefact, whatever the browser asked the plan for")]
    public async Task TheFileHandedToAnOutsidePlayerIsStillTheArtefact()
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(4_000));
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage answer = await feature.Client.GetAsync(
            new Uri($"/api/videos/{recording.Id.Wire}?source=recording", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(artefact, await answer.Content.ReadAsByteArrayAsync());
    }

    private static string? Header(HttpResponseMessage answer, string named)
        => answer.Headers.TryGetValues(named, out IEnumerable<string>? values) ? values.Single() : null;
}
