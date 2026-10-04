using System.Net;
using System.Text.Json;

using Carina.Api.Playback;
using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

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

    [Fact(DisplayName = "an encoded recording of a broadcast that announced two sounds still offers both of them")]
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

    [Fact(DisplayName = "the secondary sound of an encoded recording is transcoded from the recording, because the artefact was baked with the main one")]
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

    [Fact(DisplayName = "the main sound of an encoded recording is the artefact itself, whatever the broadcast announced")]
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

    [Fact(DisplayName = "when the recording itself is out of reach, the plan of an encoded recording asked for its second sound offers the artefact and the one sound it carries")]
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

    [Fact(DisplayName = "a picture asked for a sound that cannot be reached is refused rather than quietly handed the artefact of another sound")]
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

    [Fact(DisplayName = "an artefact a newer one replaced is never played, even while its file is still on the disk, so a newer one a browser cannot decode as it is leaves the recording to the transcoder")]
    public async Task AnArtefactANewerOneReplacedIsNeverPlayed()
    {
        await using PlayFeature feature = new();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, bytes: 700);
        feature.Encoded(recording, EncodeCodec.H265, minutesLater: 90);
        feature.Jobs.Jobs[0].Replaced(feature.Jobs.Jobs[1], RecordingFeature.Noon.AddHours(3));

        using HttpResponseMessage picture = await feature.PictureAsync(recording);

        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("onTheFly", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(recording.FileName, Assert.Single(feature.Player.Opened).Name);
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

    [Fact(DisplayName = "an artefact made in H.264 is still played as it is after its profile was changed to H.265")]
    public async Task AnArtefactMadeInH264IsStillPlayedAsItIsAfterItsProfileWasChangedToH265()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H264);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");
        JsonElement asked = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?source=artefact"))).GetProperty("data");
        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=artefact");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal("artefact", read.GetProperty("source").GetString());
        Assert.Equal("recording", read.GetProperty("alternative").GetString());
        Assert.Equal("artefact", asked.GetProperty("source").GetString());
        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Fact(DisplayName = "the plan of a recording asked for as it was recorded names the artefact made in H.264 as the other one after its profile was changed to H.265")]
    public async Task ThePlanAskedForTheRecordingNamesTheArtefactMadeInH264AfterItsProfileWasChangedToH265()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H264);

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?source=recording"))).GetProperty("data");

        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal("artefact", read.GetProperty("alternative").GetString());
    }

    [Fact(DisplayName = "an artefact whose file is H.265 is left to the transcoder even when its profile says H.264 now")]
    public async Task AnArtefactWhoseFileIsH265IsLeftToTheTranscoderEvenWhenItsProfileSaysH264Now()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H264, fileReadAs: EncodeCodec.H265);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
    }

    [Fact(DisplayName = "an artefact made in H.265 and tagged hvc1 is handed over as it is to a browser that says it decodes h265")]
    public async Task AnArtefactInH265TaggedHvc1IsHandedOverToABrowserThatSaysItDecodesH265()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        byte[] artefact = feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H265, taggedAs: "hvc1");

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?decodes=h265"))).GetProperty("data");
        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=artefact&decodes=h265");

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal("artefact", read.GetProperty("source").GetString());
        Assert.Equal("recording", read.GetProperty("alternative").GetString());
        Assert.Equal(["artefact", "recording"], ExternalPlayerSources(read));
        Assert.Equal("direct", Header(picture, PlaybackHeaders.Route));
        Assert.Equal(artefact, await picture.Content.ReadAsByteArrayAsync());
        Assert.Null(feature.Player.Handed);
    }

    [Theory(DisplayName = "a browser that does not say it decodes h265 has an artefact made in H.265 transcoded, and the plan still names it for external players")]
    [InlineData("")]
    [InlineData("?decodes=h264")]
    public async Task ABrowserThatDoesNotSayItDecodesH265HasTheArtefactTranscoded(string query)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H265, taggedAs: "hvc1");

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording, query))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
        Assert.Equal(["artefact", "recording"], ExternalPlayerSources(read));
    }

    [Fact(DisplayName = "an artefact made in H.265 and tagged hev1 is transcoded even for a browser that says it decodes h265")]
    public async Task AnArtefactInH265TaggedHev1IsTranscodedEvenForABrowserThatDecodesH265()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265, fileReadAs: EncodeCodec.H265, taggedAs: "hev1");

        JsonElement read = (await PlayFeature.PlanOfAsync(
            await feature.PlanAsync(recording, "?decodes=h264&decodes=h265"))).GetProperty("data");

        Assert.Equal("onTheFly", read.GetProperty("route").GetString());
        Assert.Equal(["artefact", "recording"], ExternalPlayerSources(read));
    }

    [Fact(DisplayName = "the plan of a recording with nothing encoded names only the recording itself for external players")]
    public async Task ThePlanOfARecordingWithNothingEncodedNamesOnlyTheRecordingForExternalPlayers()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal(["recording"], ExternalPlayerSources(read));
    }

    [Theory(DisplayName = "a decoding that is not one of the two is refused, for the plan and for the picture alike")]
    [InlineData("?decodes=av1")]
    [InlineData("?decodes=h265&decodes=H264")]
    public async Task ADecodingThatIsNotOneOfTheTwoIsRefused(string query)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording);

        using HttpResponseMessage plan = await feature.PlanAsync(recording, query);
        using HttpResponseMessage picture = await feature.PictureAsync(recording, query);

        Assert.Equal(HttpStatusCode.BadRequest, plan.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, picture.StatusCode);
        Assert.Equal(PlayDelivery.TheDecodingsThereAre, (await PlayFeature.PlanOfAsync(plan)).GetProperty("message").GetString());
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
    public async Task TheFileOfAnEncodedRecordingAskedForAsItWasRecordedIsTheRecordingHandedOverAsATransportStream()
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(4_000);
        Recording recording = feature.Ended(RecordingOutcome.Complete, written);
        feature.Encoded(recording);

        using HttpResponseMessage answer = await feature.GetFromAsync(recording, "recording");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("video/mp2t", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(written, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheRecordingItselfIsMovedAboutByARangeAndAskedForItsSizeAloneLikeTheArtefact()
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(4_000);
        Recording recording = feature.Ended(RecordingOutcome.Complete, written);
        feature.Encoded(recording);

        using HttpResponseMessage part = await feature.GetFromAsync(recording, "recording", "bytes=100-199");
        using HttpResponseMessage head = await feature.HeadFromAsync(recording, "recording");

        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(written[100..200], await part.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal(written.Length, head.Content.Headers.ContentLength);
    }

    [Fact]
    public async Task TheFileOfAnEncodedRecordingAskedForAsTheArtefactByNameIsTheArtefact()
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(4_000));
        byte[] artefact = feature.Encoded(recording);

        using HttpResponseMessage answer = await feature.GetFromAsync(recording, "artefact");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("video/mp4", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(artefact, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task TheRecordingItselfAskedForWhenItIsGoneIsRefusedRatherThanQuietlyHandedTheArtefact()
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(4_000), onDisk: false);
        feature.Encoded(recording);

        using HttpResponseMessage answer = await feature.GetFromAsync(recording, "recording");

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
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

    [Theory(DisplayName = "with an earlier artefact in H.264 and a later one in H.265, a browser is handed the newest one it decodes, with the chapters that artefact's job marked")]
    [InlineData("?decodes=h265", 1_300, 45d)]
    [InlineData("", 700, 30d)]
    public async Task ABrowserIsHandedTheNewestArtefactItDecodesWithItsOwnChapters(string query, long bytes, double endsAt)
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H264, bytes: 700, fileReadAs: EncodeCodec.H264);
        feature.Encoded(recording, EncodeCodec.H265, minutesLater: 90, bytes: 1_300, fileReadAs: EncodeCodec.H265, taggedAs: "hvc1");
        await feature.MarkedAsync(
            feature.Jobs.Jobs[0],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(30), ChapterKind.Break));
        await feature.MarkedAsync(
            feature.Jobs.Jobs[1],
            new ChapterSegment(TimeSpan.Zero, TimeSpan.FromSeconds(45), ChapterKind.Programme));

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording, query))).GetProperty("data");
        JsonElement chapter = Assert.Single(read.GetProperty("chapters").EnumerateArray());

        Assert.Equal("direct", read.GetProperty("route").GetString());
        Assert.Equal(bytes, read.GetProperty("bytes").GetInt64());
        Assert.Equal(endsAt, chapter.GetProperty("endsAtSec").GetDouble());
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

    [Fact(DisplayName = "a recording with an artefact asked for as it was recorded hands the recording itself to the transcoder rather than the artefact")]
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

    [Fact(DisplayName = "the plan of a recording asked for as it was recorded says it plays the recording and names the artefact as the other one")]
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

    [Fact(DisplayName = "the plan of an encoded recording asked for as it is says it plays the artefact and names the recording itself as the other one")]
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

    [Fact(DisplayName = "asking outright for the artefact is asking for what playing a recording has always given")]
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

    [Fact(DisplayName = "an artefact the ledger names and the disk has not is not the other one the plan offers")]
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

    [Fact(DisplayName = "an artefact holding no bytes is not the other one the plan offers")]
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

    [Fact(DisplayName = "an artefact a browser would not decode as it is, is not the other one the plan offers")]
    public async Task AnArtefactABrowserWouldNotDecodeIsNotOfferedAsTheOtherOne()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete);
        feature.Encoded(recording, EncodeCodec.H265);

        JsonElement read = (await PlayFeature.PlanOfAsync(await feature.PlanAsync(recording))).GetProperty("data");

        Assert.Equal("recording", read.GetProperty("source").GetString());
        Assert.Equal(JsonValueKind.Null, read.GetProperty("alternative").ValueKind);
    }

    [Fact(DisplayName = "a recording asked for as it was recorded and no longer on the disk is refused rather than quietly handed its artefact")]
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

    [Fact(DisplayName = "a recording asked for as it was recorded with a second sound is not narrowed to the artefact when the recording is gone")]
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

    [Fact(DisplayName = "a recording asked for as it was recorded and holding no bytes is refused rather than quietly handed its artefact")]
    public async Task ARecordingAskedForAsItWasRecordedAndHoldingNoBytesIsRefused()
    {
        await using var feature = new PlayFeature();
        Recording recording = feature.Ended(RecordingOutcome.Failed, bytes: 0);
        feature.Encoded(recording);

        using HttpResponseMessage picture = await feature.PictureAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.NotFound, picture.StatusCode);
        Assert.Empty(feature.Player.Opened);
    }

    [Fact(DisplayName = "a recording asked for as it was recorded is moved about by starting again, because it is transcoded while playing")]
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

    [Fact(DisplayName = "the chapters the ledger holds belong to the artefact, so a recording asked for as it was recorded comes back with none")]
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

    private static string? Header(HttpResponseMessage answer, string named)
        => answer.Headers.TryGetValues(named, out IEnumerable<string>? values) ? values.Single() : null;

    private static string[] ExternalPlayerSources(JsonElement plan)
        => [.. plan.GetProperty("externalPlayerSources").EnumerateArray().Select(source => source.GetString()!)];
}
