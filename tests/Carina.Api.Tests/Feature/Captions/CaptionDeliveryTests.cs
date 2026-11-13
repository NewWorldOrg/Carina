using System.Net;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Captions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Carina.Api.Tests.Feature.Captions;

[Trait("Category", "Feature")]
public sealed class CaptionDeliveryTests
{
    [Fact]
    public async Task BrPd017OnTheRecordingItselfEachCaptionIsAtItsMomentLessWhereTheFilesClockBegins()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, "?source=recording");
        JsonElement window = await CaptionFeature.WindowOfAsync(answer);

        Assert.Equal([0.2, 5.0, 8.0], CaptionFeature.Moments(window));
        Assert.Equal(600.0, window.GetProperty("untilSec").GetDouble());
        Assert.Equal(1440, window.GetProperty("canvas").GetProperty("width").GetInt32());
        Assert.Equal(1080, window.GetProperty("canvas").GetProperty("height").GetInt32());

        JsonElement first = window.GetProperty("cues")[0].GetProperty("picture");
        Assert.Equal((10, 900, 320, 48), (first.GetProperty("left").GetInt32(), first.GetProperty("top").GetInt32(), first.GetProperty("width").GetInt32(), first.GetProperty("height").GetInt32()));
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4e, 0x47, 10 }, first.GetProperty("png").GetBytesFromBase64());
        Assert.Equal(JsonValueKind.Null, window.GetProperty("cues")[2].GetProperty("picture").ValueKind);
        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());
    }

    [Theory(DisplayName = "the captions are placed on an artefact made in H.265 only for a browser that says it decodes h265, as its plan is")]
    [InlineData("?source=artefact&decodes=h265", new[] { 0.0, 4.5, 7.5 })]
    [InlineData("?source=artefact", new[] { 0.2, 5.0, 8.0 })]
    public async Task TheCaptionsArePlacedOnAnArtefactInH265OnlyForABrowserThatDecodesH265(string query, double[] moments)
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();
        feature.Encoded(
            recording,
            new EncodeTimeline(CaptionFeature.FileBegins, TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(600)),
            EncodeCodec.H265,
            "hvc1");

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, query);

        Assert.Equal(moments, CaptionFeature.Moments(await CaptionFeature.WindowOfAsync(answer)));
    }

    [Fact(DisplayName = "captions asked for with a decoding that is not one of the two are refused")]
    public async Task CaptionsAskedForWithADecodingThatIsNotOneOfTheTwoAreRefused()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, "?decodes=vp9");

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Fact]
    public async Task BrPd017OnTheArtefactEachCaptionIsAtItsMomentLessTheJobsCaptionShiftAndOneInTheSkippedHeadIsAtZero()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();
        feature.Encoded(recording, new EncodeTimeline(CaptionFeature.FileBegins, TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(600)));

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, "?source=artefact");
        JsonElement window = await CaptionFeature.WindowOfAsync(answer);

        Assert.Equal([0.0, 4.5, 7.5], CaptionFeature.Moments(window));
    }

    [Fact]
    public async Task BrPd017ACaptionPastTheEndOfTheArtefactIsLeftOut()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();
        feature.Encoded(recording, new EncodeTimeline(CaptionFeature.FileBegins, TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(600)));

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, "?from=590");

        Assert.Empty(CaptionFeature.Moments(await CaptionFeature.WindowOfAsync(answer)));
    }

    [Fact]
    public async Task BrPd017AWindowFurtherInStartsWithTheCaptionShowingThereAndCoversTenMinutesFromIt()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, "?source=recording&from=6.5");
        JsonElement window = await CaptionFeature.WindowOfAsync(answer);

        Assert.Equal([5.0, 8.0], CaptionFeature.Moments(window));
        Assert.Equal(606.5, window.GetProperty("untilSec").GetDouble());
    }

    [Fact]
    public async Task BrPd017TheTwoClocksDisagreeingByAMillisecondDrawsNothingOverTheArtefactAndStillDrawsOverTheRecording()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();
        feature.Encoded(recording, new EncodeTimeline(CaptionFeature.FileBegins + TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(0.5), null, null));

        using HttpResponseMessage overTheArtefact = await feature.CaptionsAsync(recording, "?source=artefact");
        using HttpResponseMessage overTheRecording = await feature.CaptionsAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.NotFound, overTheArtefact.StatusCode);
        Assert.Equal(HttpStatusCode.OK, overTheRecording.StatusCode);
    }

    [Fact]
    public async Task AnArtefactWhoseJobNeverMeasuredItsTimelineHasNoCaptionsOverIt()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();
        feature.Encoded(recording, null);

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact]
    public async Task BrPd017CaptionsStillBeingTakenAreRefusedAsComing()
    {
        await using var feature = new CaptionFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording);

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
    }

    [Fact]
    public async Task BrPd017ARecordingWithNoCaptionsIsRefusedAsNotFound()
    {
        await using var feature = new CaptionFeature();
        Recording recording = feature.Ended();
        recording.Caption(CaptionState.Absent, null, RecordingFeature.Noon.AddHours(1));

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact]
    public async Task BrPd016CaptionsTheRowSaysAreReadyWithNoRecordOnTheShelfAreComingAndNothingIsWritten()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync(onTheShelf: false);

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
        Assert.Equal(CaptionState.Ready, recording.CaptionState);
    }

    [Fact]
    public async Task BrPd016CaptionsTakenBeforeTheFileWasDescrambledAreComingAgain()
    {
        await using var feature = new CaptionFeature();
        Recording recording = feature.Ended(scrambled: true);
        recording.Caption(CaptionState.Ready, 3, RecordingFeature.Noon.AddHours(1));
        await new CaptionShelf(feature.Settings).KeepAsync(recording.Id, CaptionFeature.Record(), CancellationToken.None);
        recording.Descrambled(RecordingFeature.Noon.AddHours(2));

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
    }

    [Fact]
    public async Task ARecordWhoseHeadReadsAndWhoseBodyDoesNotIsRefusedAsHavingNone()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();
        string kept = feature.Settings.PathOf(recording.Id)!;
        byte[] whole = await File.ReadAllBytesAsync(kept);
        await File.WriteAllBytesAsync(kept, whole[..^3]);

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact]
    public async Task WithNowhereToKeepCaptionsThereAreNone()
    {
        await using var feature = new CaptionFeature(anywhereToKeepThem: false);
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Theory]
    [InlineData("?from=-1")]
    [InlineData("?from=soon")]
    [InlineData("?from=NaN")]
    [InlineData("?source=elsewhere")]
    public async Task AskingForSomethingThatIsNotASecondOrASourceIsRefused(string query)
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, query);

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Fact]
    public async Task ARecordingNobodyHasIsNotFound()
    {
        await using var feature = new CaptionFeature();

        using HttpResponseMessage answer = await feature.Client.GetAsync(new Uri($"/api/videos/{RecordingId.New().Wire}/captions", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact]
    public async Task ARecordingStillBeingWrittenIsRefusedTheWayItsPlanIs()
    {
        await using var feature = new CaptionFeature();
        Recording recording = RecordingFeature.Begin(RecordingId.New());
        feature.Recordings.Recordings.Add(recording);

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording);

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
    }

    [Fact]
    public async Task NobodyWithoutASessionIsAnswered()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, asking: feature.Stranger);

        Assert.Equal(HttpStatusCode.Unauthorized, answer.StatusCode);
    }
}
