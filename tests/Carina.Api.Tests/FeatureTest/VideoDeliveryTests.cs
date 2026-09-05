using System.Net;

using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class VanishingPlaybackFiles(long bytes, PlaybackFileAbsence then = PlaybackFileAbsence.Gone) : IPlaybackFileStore
{
    public PlaybackFileSearch Find(OutputRoot root, RecordingFileName fileName)
        => PlaybackFileSearch.Of(new PlaybackFile(root, fileName, bytes));

    public PlaybackFileOpening OpenRead(PlaybackFile file) => PlaybackFileOpening.Missing(then);

    public StreamSource? SourceOf(PlaybackFile file) => null;
}

[Collection(FeatureTestCollection.Name)]
public sealed class VideoDeliveryTests
{
    private const int Size = 4_000;

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
}
