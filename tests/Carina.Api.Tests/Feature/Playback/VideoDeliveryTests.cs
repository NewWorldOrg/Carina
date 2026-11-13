using System.Net;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Auth;
using Carina.Domain.Encodings;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Domain.Viewing;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carina.Api.Tests.Feature.Playback;

[Trait("Category", "Feature")]
public sealed class VideoDeliveryTests
{
    private const int Size = 4_000;

    private const int GatedSize = 256 * 1024;

    private const int FirstHalf = GatedSize / 2;

    private const string AName = "a-programme.ts";

    private static readonly byte[] Written = [.. Enumerable.Range(0, GatedSize).Select(index => (byte)(index % 251))];

    [Fact]
    public async Task TheWholeFileComesBackWhenNoRangeIsAskedFor()
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(Size);
        Recording recording = feature.Ended(RecordingOutcome.Complete, written);

        using HttpResponseMessage answer = await feature.GetAsync(recording);
        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("bytes", Assert.Single(answer.Headers.AcceptRanges));
        Assert.Equal("video/mp2t", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Size, answer.Content.Headers.ContentLength);
        Assert.Equal(written, body);
    }

    [Fact]
    public async Task AskingFromTheStartIsAPartAndCarriesTheWholeFile()
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(Size);
        Recording recording = feature.Ended(RecordingOutcome.Complete, written);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=0-");
        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.PartialContent, answer.StatusCode);
        Assert.Equal($"bytes 0-{Size - 1}/{Size}", answer.Content.Headers.ContentRange?.ToString());
        Assert.Equal(Size, answer.Content.Headers.ContentLength);
        Assert.Equal(written, body);
    }

    [Fact]
    public async Task AskingFromAnOffsetHandsBackTheBytesFromThatOffset()
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(Size);
        Recording recording = feature.Ended(RecordingOutcome.Complete, written);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=100-");
        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.PartialContent, answer.StatusCode);
        Assert.Equal($"bytes 100-{Size - 1}/{Size}", answer.Content.Headers.ContentRange?.ToString());
        Assert.Equal(written[100..], body);
    }

    [Fact]
    public async Task AskingForTheLastBytesHandsBackTheEndOfTheFileAndNotItsStart()
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(Size);
        Recording recording = feature.Ended(RecordingOutcome.Complete, written);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=-500");
        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.PartialContent, answer.StatusCode);
        Assert.Equal($"bytes {Size - 500}-{Size - 1}/{Size}", answer.Content.Headers.ContentRange?.ToString());
        Assert.Equal(written[^500..], body);
        Assert.NotEqual(written[..500], body);
    }

    [Fact]
    public async Task AskingForBothEndsHandsBackWhatIsBetweenThem()
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(Size);
        Recording recording = feature.Ended(RecordingOutcome.Complete, written);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=100-199");
        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.PartialContent, answer.StatusCode);
        Assert.Equal(100, body.Length);
        Assert.Equal(written[100..200], body);
    }

    [Fact]
    public async Task ARangeThatStartsPastTheEndIsRefusedWithTheSizeOfWhatThereIs()
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(Size));

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=999999-");
        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, answer.StatusCode);
        Assert.Equal($"bytes */{Size}", answer.Content.Headers.ContentRange?.ToString());
        Assert.Equal("bytes", Assert.Single(answer.Headers.AcceptRanges));
        Assert.Empty(body);
    }

    [Theory]
    [InlineData("bytes=abc")]
    [InlineData("bytes=100-50")]
    [InlineData("items=0-99")]
    public async Task ARangeThatCannotBeReadIsIgnoredAndTheWholeFileComesBack(string range)
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(Size);
        Recording recording = feature.Ended(RecordingOutcome.Complete, written);

        using HttpResponseMessage answer = await feature.GetAsync(recording, range);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(written, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task MoreThanOneRangeIsNotServedAsSeveralParts()
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(Size);
        Recording recording = feature.Ended(RecordingOutcome.Complete, written);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=0-99,200-299");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Null(answer.Content.Headers.ContentRange);
        Assert.Equal(written, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AskingOnlyWhatIsThereSendsTheSameHeadersAndNoBody()
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(Size));

        using HttpResponseMessage whole = await feature.HeadAsync(recording);
        using HttpResponseMessage part = await feature.HeadAsync(recording, "bytes=-500");

        Assert.Equal(HttpStatusCode.OK, whole.StatusCode);
        Assert.Equal("bytes", Assert.Single(whole.Headers.AcceptRanges));
        Assert.Equal(Size, whole.Content.Headers.ContentLength);
        Assert.Empty(await whole.Content.ReadAsByteArrayAsync());

        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal($"bytes {Size - 500}-{Size - 1}/{Size}", part.Content.Headers.ContentRange?.ToString());
        Assert.Equal(500, part.Content.Headers.ContentLength);
        Assert.Empty(await part.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ARecordingCutShortIsStillHandedOverToAPlayerThatCanReadIt()
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(Size);
        Recording recording = feature.Ended(RecordingOutcome.Truncated, written);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=0-9");

        Assert.Equal(HttpStatusCode.PartialContent, answer.StatusCode);
        Assert.Equal(written[..10], await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ARecordingOfNoBytesHasNoVideoToHandOver()
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Failed, []);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=0-");

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ARecordingWhoseFileIsGoneWhileItsRootIsThereIsNotFound()
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(Size), onDisk: false);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=0-");

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ARecordingWhoseRootIsGoneSaysTheBytesAreOutOfReachRatherThanNotFound()
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(Size));
        Directory.Delete(feature.MountedAt, recursive: true);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=0-");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
    }

    [Fact]
    public async Task AFileTakenOffTheDiskBetweenBeingFoundAndBeingOpenedIsNotFoundRatherThanHalfServed()
    {
        await using var feature = new PlaybackFeature(new VanishingPlaybackFiles(Size));
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(Size), onDisk: false);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=1000-1999");

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ARootThatWentAwayBetweenTheFileBeingFoundAndOpenedIsOutOfReachRatherThanNotFound()
    {
        await using var feature = new PlaybackFeature(new VanishingPlaybackFiles(Size, PlaybackFileAbsence.OutOfReach));
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(Size), onDisk: false);

        using HttpResponseMessage answer = await feature.GetAsync(recording, "bytes=1000-1999");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, answer.StatusCode);
    }

    [Fact]
    public async Task ARecordingStillBeingWrittenIsNotHandedOverAsAWholeFile()
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.StillWriting();

        using HttpResponseMessage answer = await feature.GetAsync(recording);

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task ARecordingNobodyHasIsNotFound()
    {
        await using var feature = new PlaybackFeature();

        using HttpResponseMessage answer = await feature.Client.GetAsync(
            new Uri($"/api/videos/{RecordingId.New().Wire}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact]
    public async Task ARecordingWithNoArtefactAskedForAsItWasRecordedIsHandedOverAsItIs()
    {
        await using var feature = new PlaybackFeature();
        byte[] written = PlaybackFeature.Bytes(Size);
        Recording recording = feature.Ended(RecordingOutcome.Complete, written);

        using HttpResponseMessage answer = await feature.GetFromAsync(recording, "recording");

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("video/mp2t", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(written, await answer.Content.ReadAsByteArrayAsync());
    }

    [Theory]
    [InlineData("proxy")]
    [InlineData("Recording ")]
    public async Task AFileThatIsNeitherOfTheTwoIsRefusedBeforeAnythingIsHandedOver(string source)
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(Size));

        using HttpResponseMessage answer = await feature.GetFromAsync(recording, source);

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task SomethingThatIsNotARecordingIdIsRefusedBeforeAnythingIsLookedFor()
    {
        await using var feature = new PlaybackFeature();

        using HttpResponseMessage answer = await feature.Client.GetAsync(
            new Uri("/api/videos/not-an-id", UriKind.Relative));

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task AClientCarryingNoCredentialsIsRefusedRatherThanSentToASignInScreen(string method)
    {
        await using var feature = new PlaybackFeature();
        Recording recording = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(Size));

        using var asking = new HttpRequestMessage(
            new HttpMethod(method),
            new Uri($"/api/videos/{recording.Id.Wire}", UriKind.Relative));
        using HttpResponseMessage answer = await feature.Stranger.SendAsync(asking);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task NothingHandedBackSaysWhereOnThisMachineTheFileIs()
    {
        await using var feature = new PlaybackFeature();
        Recording gone = feature.Ended(RecordingOutcome.Complete, PlaybackFeature.Bytes(Size), onDisk: false);
        Recording empty = feature.Ended(RecordingOutcome.Failed, []);

        using HttpResponseMessage missing = await feature.GetAsync(gone);
        using HttpResponseMessage nothing = await feature.GetAsync(empty, "bytes=0-");

        Assert.Empty(await missing.Content.ReadAsByteArrayAsync());
        Assert.Empty(await nothing.Content.ReadAsByteArrayAsync());
        Assert.DoesNotContain(
            missing.Headers.Concat(missing.Content.Headers),
            header => header.Value.Any(value => value.Contains(feature.MountedAt, StringComparison.Ordinal)));
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
    public async Task ARecordingAlreadyBeingHandedOverKeepsFlowingAfterItsSessionIsEnded()
    {
        GatedPlaybackFiles files = new(Written);
        await using AuthProbe probe = Wiring(files, new GatedOnTheFlyPlayer(Written), out Recording recording);
        AuthSession session = await probe.SignedInAsync();

        files.Handed.Release(FirstHalf);

        using HttpResponseMessage response = await probe.Client.GetAsync(
            Bytes(recording),
            HttpCompletionOption.ResponseHeadersRead);
        await using Stream body = await response.Content.ReadAsStreamAsync();
        byte[] heard = new byte[GatedSize];

        await body.ReadExactlyAsync(heard.AsMemory(0, FirstHalf));

        session.Revoke(DateTime.UtcNow);
        files.Handed.Release(GatedSize);

        await body.ReadExactlyAsync(heard.AsMemory(FirstHalf));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Written, heard);
    }

    [Fact]
    public async Task TheNextRequestForTheRecordingAfterThatSessionEndedIsRefused()
    {
        await using AuthProbe probe = Wiring(new GatedPlaybackFiles(Written), new GatedOnTheFlyPlayer(Written), out Recording recording);
        AuthSession session = await probe.SignedInAsync();

        session.Revoke(DateTime.UtcNow);

        using HttpResponseMessage response = await probe.Client.GetAsync(Bytes(recording), HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "a player with a ticket asking for the artefact is handed the file made in H.264 after its profile was changed to H.265")]
    public async Task APlayerAskingForTheArtefactIsHandedTheFileMadeInH264AfterItsProfileWasChangedToH265()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        byte[] artefact = feature.Encoded(recording, EncodeCodec.H265, EncodeCodec.H264);
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}?source=artefact",
            ticket);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("video/mp4", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(artefact, await answer.Content.ReadAsByteArrayAsync());
    }

    [Theory(DisplayName = "a player with a ticket is handed an artefact made in H.265, however it is tagged, which no browser's decoding narrows")]
    [InlineData("", "hvc1")]
    [InlineData("?source=artefact", "hev1")]
    public async Task APlayerWithATicketIsHandedAnArtefactMadeInH265(string query, string tag)
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        byte[] artefact = feature.Encoded(recording, EncodeCodec.H265, EncodeCodec.H265, tag);
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}{query}",
            ticket);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal("video/mp4", answer.Content.Headers.ContentType?.MediaType);
        Assert.Equal(artefact, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "a player with a ticket is not handed an artefact whose file is neither of the two codecs, and gets the recording itself")]
    public async Task APlayerWithATicketIsNotHandedAnArtefactOfNeitherCodec()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        feature.Encoded(recording, EncodeCodec.H265, EncodeCodec.H265);
        feature.Codecs.Readings[feature.Jobs.Jobs[0].ArtefactName!.Value] = ArtefactCodecReading.Neither("av1");
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync($"/api/videos/{recording.Id.Wire}", ticket);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(feature.Written, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task APlayerThatAsksTheHeadersThenSeeksTwiceIsAdmittedEveryTime()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);
        string path = $"/api/videos/{recording.Id.Wire}";

        using HttpResponseMessage headers = await feature.AsAPlayerAsync(path, ticket, method: HttpMethod.Head);
        using HttpResponseMessage whole = await feature.AsAPlayerAsync(path, ticket, "bytes=0-");
        using HttpResponseMessage middle = await feature.AsAPlayerAsync(path, ticket, "bytes=1000-1999");
        using HttpResponseMessage tail = await feature.AsAPlayerAsync(path, ticket, "bytes=-500");

        Assert.Equal(HttpStatusCode.OK, headers.StatusCode);
        Assert.Equal(feature.Written.Length, headers.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.PartialContent, whole.StatusCode);
        Assert.Equal(feature.Written, await whole.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.PartialContent, middle.StatusCode);
        Assert.Equal(feature.Written[1000..2000], await middle.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.PartialContent, tail.StatusCode);
        Assert.Equal(feature.Written[^500..], await tail.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task AGrantOpensTheOneRecordingItWasEnteredWithAndNoOther()
    {
        await using var feature = new TicketedFeature();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage entered = await feature.AsAPlayerAsync($"/api/videos/{mine.Id.Wire}", ticket);
        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync(
            $"/api/videos/{another.Id.Wire}",
            ticket);

        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
    }

    [Fact]
    public async Task BrLa004ATicketIssuedForOneRecordingOpensNoOtherEvenOnItsFirstUse()
    {
        await using var feature = new TicketedFeature();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync(
            $"/api/videos/{another.Id.Wire}",
            ticket,
            "bytes=0-");

        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.NotEqual(feature.Written, await elsewhere.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task BrLa004ATicketOfferedAtTheWrongRecordingIsSpentAndOpensItsOwnNoLonger()
    {
        await using var feature = new TicketedFeature();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync($"/api/videos/{another.Id.Wire}", ticket);
        using HttpResponseMessage afterwards = await feature.AsAPlayerAsync($"/api/videos/{mine.Id.Wire}", ticket);

        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, afterwards.StatusCode);
    }

    [Fact]
    public async Task APlayerCarryingNothingIsRefusedWithoutBeingSentToASignInScreen()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync($"/api/videos/{recording.Id.Wire}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task SomethingThatWasNeverIssuedIsRefusedBeforeAnyByteLeaves()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}",
            new string('a', 43));

        byte[] body = await answer.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        Assert.NotEqual(feature.Written, body);
    }

    [Theory]
    [InlineData("/play")]
    [InlineData("/thumbnail")]
    [InlineData("/scrub")]
    [InlineData("/captions")]
    [InlineData("/data-broadcast")]
    [InlineData("/data-broadcast/modules/64/7/0/1")]
    public async Task TheTicketOpensTheBytesAndNothingElseUnderTheSamePrefix(string beneath)
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}{beneath}",
            ticket);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
    }

    [Fact]
    public async Task TheAnswerToATicketIsNeverHeldByAnythingInFront()
    {
        await using var feature = new TicketedFeature();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync($"/api/videos/{recording.Id.Wire}", ticket);

        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());
        Assert.Contains("Authorization", answer.Headers.Vary, StringComparer.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "a player handed the ticket in the path is handed the recording, asks its headers and seeks on the same URL")]
    public async Task APlayerHandedTheTicketInThePathIsHandedTheRecordingAndSeeksOnTheSameUrl()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();
        string path = WithTicket(recording, await feature.TicketForAsync(recording.Id), AName);

        using HttpResponseMessage headers = await feature.AsAPlayerAsync(path, null, method: HttpMethod.Head);
        using HttpResponseMessage whole = await feature.AsAPlayerAsync(path, null, "bytes=0-");
        using HttpResponseMessage middle = await feature.AsAPlayerAsync(path, null, "bytes=1000-1999");

        Assert.Equal(HttpStatusCode.OK, headers.StatusCode);
        Assert.Equal(feature.Written.Length, headers.Content.Headers.ContentLength);
        Assert.Equal(HttpStatusCode.PartialContent, whole.StatusCode);
        Assert.Equal(feature.Written, await whole.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.PartialContent, middle.StatusCode);
        Assert.Equal(feature.Written[1000..2000], await middle.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "the ticket in the path hands over the source the query asks for")]
    public async Task TheTicketInThePathHandsOverTheSourceTheQueryAsksFor()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();
        byte[] artefact = feature.Encoded(recording, EncodeCodec.H264, EncodeCodec.H264);
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage encoded = await feature.AsAPlayerAsync(
            $"{WithTicket(recording, ticket, "a-programme.mp4")}?source=artefact",
            null);
        using HttpResponseMessage itself = await feature.AsAPlayerAsync(
            $"{WithTicket(recording, ticket, AName)}?source=recording",
            null);

        Assert.Equal(HttpStatusCode.OK, encoded.StatusCode);
        Assert.Equal("video/mp4", encoded.Content.Headers.ContentType?.MediaType);
        Assert.Equal(artefact, await encoded.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, itself.StatusCode);
        Assert.Equal(feature.Written, await itself.Content.ReadAsByteArrayAsync());
    }

    [Theory(DisplayName = "the name at the end of the path is only for showing and is never read")]
    [InlineData("")]
    [InlineData("a-programme.mp4")]
    [InlineData("%E7%95%AA%E7%B5%84%20%E3%81%AE%E5%90%8D%E5%89%8D.ts")]
    [InlineData("two/segments.ts")]
    [InlineData("no-extension")]
    public async Task TheNameAtTheEndOfThePathIsOnlyForShowing(string name)
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            WithTicket(recording, await feature.TicketForAsync(recording.Id), name),
            null);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        Assert.Equal(feature.Written, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "the ticket in the path opens the one recording it was issued for and no other")]
    public async Task TheTicketInThePathOpensTheOneRecordingItWasIssuedForAndNoOther()
    {
        await using TicketedFeature feature = new();
        Recording mine = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(mine.Id);

        using HttpResponseMessage entered = await feature.AsAPlayerAsync(WithTicket(mine, ticket, AName), null);
        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync(WithTicket(another, ticket, AName), null);

        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.NotEqual(feature.Written, await elsewhere.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "something that was never issued in the ticket's place is refused before any byte leaves")]
    public async Task SomethingThatWasNeverIssuedInTheTicketsPlaceIsRefused()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            WithTicket(recording, new string('a', 43), AName),
            null);

        Assert.Equal(HttpStatusCode.Forbidden, answer.StatusCode);
        Assert.NotEqual(feature.Written, await answer.Content.ReadAsByteArrayAsync());
    }

    [Fact(DisplayName = "something that is not the shape of a ticket in its place is refused without being sent to a sign-in screen, whatever the header carries")]
    public async Task SomethingThatIsNotTheShapeOfATicketInItsPlaceIsRefused()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            WithTicket(recording, "short", AName),
            ticket);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
        Assert.Empty(await answer.Content.ReadAsByteArrayAsync());
    }

    [Theory(DisplayName = "the ticket in the path opens the bytes and nothing else under the same prefix")]
    [InlineData("play")]
    [InlineData("thumbnail")]
    [InlineData("scrub")]
    [InlineData("captions")]
    [InlineData("data-broadcast")]
    public async Task TheTicketInThePathOpensTheBytesAndNothingElse(string beneath)
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}/{beneath}/with-ticket/{ticket}/{AName}",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
        Assert.Null(answer.Headers.Location);
    }

    [Fact(DisplayName = "a ticket in the query is refused on both surfaces, because it shows as the title in a player")]
    public async Task ATicketInTheQueryIsRefusedOnBothSurfaces()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage plain = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}?ticket={ticket}",
            null);
        using HttpResponseMessage named = await feature.AsAPlayerAsync(
            $"/api/videos/{recording.Id.Wire}/with-ticket/short/{AName}?ticket={ticket}",
            null);

        Assert.Equal(HttpStatusCode.Unauthorized, plain.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, named.StatusCode);
    }

    [Fact(DisplayName = "the answer to a ticket in the path is never held by anything in front")]
    public async Task TheAnswerToATicketInThePathIsNeverHeldByAnythingInFront()
    {
        await using TicketedFeature feature = new();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.AsAPlayerAsync(
            WithTicket(recording, await feature.TicketForAsync(recording.Id), AName),
            null);

        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());
    }

    [Fact(DisplayName = "with every log turned up to its finest, no line carries the ticket in the path")]
    public async Task WithEveryLogTurnedUpNoLineCarriesTheTicketInThePath()
    {
        RecordedStartup heard = new();
        await using TicketedFeature feature = new(heard);
        Recording recording = feature.Ended();
        Recording another = feature.Ended();
        string ticket = await feature.TicketForAsync(recording.Id);

        using HttpResponseMessage entered = await feature.AsAPlayerAsync(WithTicket(recording, ticket, AName), null);
        using HttpResponseMessage elsewhere = await feature.AsAPlayerAsync(WithTicket(another, ticket, AName), null);

        Assert.Equal(HttpStatusCode.OK, entered.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, elsewhere.StatusCode);
        Assert.Contains(heard.Everything, line => line.Category.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
        Assert.Empty(heard.Everything
            .Where(line => line.Text.Contains(ticket, StringComparison.Ordinal))
            .Select(line => $"{line.Category} {line.Level}")
            .Distinct());
    }

    private static Uri Bytes(Recording recording) => new($"/api/videos/{recording.Id.Wire}", UriKind.Relative);

    private static AuthProbe Wiring(IPlaybackFileStore files, IOnTheFlyPlayer player, out Recording recording)
    {
        HeldRecordings recordings = new();

        recording = RecordingFeature.Begin(RecordingId.New());
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, GatedSize, RecordingFeature.Noon.AddMinutes(30));
        recordings.Recordings.Add(recording);

        return AuthProbe.OverHttp(services =>
        {
            services.RemoveAll<IPlaybackFileStore>();
            services.RemoveAll<IOnTheFlyPlayer>();
            services.AddSingleton<IRecordingDirectory>(recordings);
            services.AddSingleton<IEncodeJobRepository>(new HeldEncodeJobs());
            services.AddSingleton<IEncodeProfileRepository>(new HeldEncodeProfiles());
            services.AddSingleton<IPlaybackPositionRepository>(new HeldPlaybackPositions());
            services.AddSingleton(files);
            services.AddSingleton(player);
        });
    }

    private static string WithTicket(Recording recording, string ticket, string name)
        => $"/api/videos/{recording.Id.Wire}/with-ticket/{ticket}/{name}";
}
