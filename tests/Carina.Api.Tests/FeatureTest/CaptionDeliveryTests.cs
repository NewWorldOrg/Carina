using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Domain.Viewing;
using Carina.Infrastructure.Captions;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class CaptionFeature : IAsyncDisposable
{
    public static readonly OutputRoot Root = new("bulk");

    public static readonly TimeSpan FileBegins = TimeSpan.FromSeconds(6115.5);

    private const long Second = CaptionCue.Hertz;

    private readonly TestingWebApplicationFactory factory = new();

    private readonly DirectoryInfo mounted = Directory.CreateTempSubdirectory("carina-captions-mounted-");

    private readonly DirectoryInfo encoded = Directory.CreateTempSubdirectory("carina-captions-encoded-");

    private readonly DirectoryInfo shelved = Directory.CreateTempSubdirectory("carina-captions-shelf-");

    public CaptionFeature(bool anywhereToKeepThem = true)
    {
        Settings = new CaptionSettings { WrittenTo = anywhereToKeepThem ? shelved.FullName : null };

        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<CaptionSettings>();
                services.AddSingleton<IRecordingDirectory>(Recordings);
                services.AddSingleton(Settings);
                services.AddSingleton(new IntegritySettings
                {
                    OutputRoots = [new StorageRootPath(Root, mounted.FullName)],
                });
                services.AddSingleton(new EncodeSettings
                {
                    OutputRoots = [new StorageRootPath(EncodedArtefact.Shelf, encoded.FullName)],
                });
                services.AddSingleton<IEncodeJobRepository>(Jobs);
                services.AddSingleton<IEncodeProfileRepository>(Profiles);
                services.RemoveAll<IOnTheFlyPlayer>();
                services.AddSingleton<IOnTheFlyPlayer>(new HeldOnTheFlyPlayer());
                services.RemoveAll<IEncodeChapterRepository>();
                services.AddSingleton<IEncodeChapterRepository>(new HeldEncodeChapters());
                services.AddSingleton<IPlaybackPositionRepository>(new HeldPlaybackPositions());
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        Stranger = configured.WithTestScheme().CreateClient();
    }

    public CaptionSettings Settings { get; }

    public HttpClient Client { get; }

    public HttpClient Stranger { get; }

    public HeldRecordings Recordings { get; } = new();

    public HeldEncodeJobs Jobs { get; } = new();

    public HeldEncodeProfiles Profiles { get; } = new();

    public static CaptionRecord Record(TimeSpan? begins = null)
        => new(
            1440,
            1080,
            begins ?? FileBegins,
            [
                new CaptionCue(At(6115.7), Picture(10)),
                new CaptionCue(At(6120.5), Picture(20)),
                new CaptionCue(At(6123.5), null),
                new CaptionCue(At(6800), Picture(30)),
            ]);

    public Recording Ended(bool scrambled = false)
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));

        if (scrambled)
        {
            recording.Note(new OutcomeDetail(RecordingFault.ScramblingUnresolved, null, string.Empty, RecordingFeature.Noon.AddMinutes(30)));
        }

        recording.Settle(scrambled ? RecordingOutcome.Truncated : RecordingOutcome.Complete, 4_000, RecordingFeature.Noon.AddMinutes(30));
        Recordings.Recordings.Add(recording);
        File.WriteAllBytes(Path.Combine(mounted.FullName, recording.FileName.Value), new byte[4_000]);

        return recording;
    }

    public async Task<Recording> CaptionedAsync(CaptionRecord? record = null, bool onTheShelf = true)
    {
        Recording recording = Ended();
        recording.Caption(CaptionState.Ready, 3, RecordingFeature.Noon.AddHours(1));

        if (onTheShelf)
        {
            await new CaptionShelf(Settings).KeepAsync(recording.Id, record ?? Record(), CancellationToken.None);
        }

        return recording;
    }

    public void Encoded(Recording recording, EncodeTimeline? timeline)
    {
        EncodeProfile profile = EncodedArtefact.Profile(EncodeCodec.H264, RecordingFeature.Noon.AddHours(-1));
        Profiles.Profiles.Add(profile);

        EncodeJob job = EncodedArtefact.Made(recording, profile, RecordingFeature.Noon.AddHours(1), timeline);
        Jobs.Jobs.Add(job);
        File.WriteAllBytes(Path.Combine(encoded.FullName, job.ArtefactName!.Value), new byte[900]);
    }

    public Task<HttpResponseMessage> CaptionsAsync(Recording recording, string query = "", HttpClient? asking = null)
        => (asking ?? Client).GetAsync(new Uri($"/api/videos/{recording.Id.Wire}/captions{query}", UriKind.Relative));

    public async Task<string?> PlannedAsync(Recording recording, string query = "")
    {
        using HttpResponseMessage answer = await Client.GetAsync(new Uri($"/api/videos/{recording.Id.Wire}/play{query}", UriKind.Relative));

        Assert.True(answer.StatusCode is HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        return read.RootElement.GetProperty("data").GetProperty("captions").GetString();
    }

    public static async Task<JsonElement> WindowOfAsync(HttpResponseMessage answer)
    {
        Assert.True(answer.StatusCode is HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        return read.RootElement.GetProperty("data").Clone();
    }

    public static double[] Moments(JsonElement window)
        => [.. window.GetProperty("cues").EnumerateArray().Select(cue => Math.Round(cue.GetProperty("atSec").GetDouble(), 3))];

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Stranger.Dispose();
        await factory.DisposeAsync();
        mounted.Delete(recursive: true);
        encoded.Delete(recursive: true);
        shelved.Delete(recursive: true);
    }

    private static long At(double seconds) => (long)Math.Round(seconds * Second);

    private static CaptionPlacement Picture(int left) => new(left, 900, 320, 48, new byte[] { 0x89, 0x50, 0x4e, 0x47, (byte)left });
}

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

    [Fact]
    public async Task BrPd017OnTheArtefactEachCaptionIsAtItsMomentLessTheJobsCaptionShiftAndOneInTheSkippedHeadIsAtZero()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();
        feature.Encoded(recording, new EncodeTimeline(CaptionFeature.FileBegins, TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(600)));

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, "?source=artefact");
        JsonElement window = await CaptionFeature.WindowOfAsync(answer);

        Assert.Equal([0.0, 4.5, 7.5], CaptionFeature.Moments(window));
        Assert.Equal("ready", await feature.PlannedAsync(recording));
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
        Assert.Equal("none", await feature.PlannedAsync(recording, "?source=artefact"));
        Assert.Equal("ready", await feature.PlannedAsync(recording, "?source=recording"));
    }

    [Fact]
    public async Task AnArtefactWhoseJobNeverMeasuredItsTimelineHasNoCaptionsOverIt()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync();
        feature.Encoded(recording, null);

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Equal("none", await feature.PlannedAsync(recording));
    }

    [Fact]
    public async Task BrPd017CaptionsStillBeingTakenAreRefusedAsComingAndThePlanSaysSo()
    {
        await using var feature = new CaptionFeature();
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording);

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
        Assert.Equal("coming", await feature.PlannedAsync(recording));
    }

    [Fact]
    public async Task BrPd017ARecordingWithNoCaptionsIsRefusedAsNotFoundAndThePlanSaysNone()
    {
        await using var feature = new CaptionFeature();
        Recording recording = feature.Ended();
        recording.Caption(CaptionState.Absent, null, RecordingFeature.Noon.AddHours(1));

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Equal("none", await feature.PlannedAsync(recording));
    }

    [Fact]
    public async Task BrPd016CaptionsTheRowSaysAreReadyWithNoRecordOnTheShelfAreComingAndNothingIsWritten()
    {
        await using var feature = new CaptionFeature();
        Recording recording = await feature.CaptionedAsync(onTheShelf: false);

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
        Assert.Equal("coming", await feature.PlannedAsync(recording, "?source=recording"));
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
    public async Task WithNowhereToKeepCaptionsThereAreNone()
    {
        await using var feature = new CaptionFeature(anywhereToKeepThem: false);
        Recording recording = feature.Ended();

        using HttpResponseMessage answer = await feature.CaptionsAsync(recording);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Equal("none", await feature.PlannedAsync(recording));
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
