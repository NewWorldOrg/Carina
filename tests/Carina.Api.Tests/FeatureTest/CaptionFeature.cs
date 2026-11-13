using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Domain.Captions;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Playback;
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

using IRecordingDetectionRepository = Carina.Domain.Segments.IRecordingDetectionRepository;
using ISegmentModelVersionRepository = Carina.Domain.Segments.ISegmentModelVersionRepository;
using ISegmentSettingsRepository = Carina.Domain.Segments.ISegmentSettingsRepository;

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
                services.RemoveAll<IArtefactCodecReader>();
                services.AddSingleton<IArtefactCodecReader>(Codecs);
                services.RemoveAll<IOnTheFlyPlayer>();
                services.AddSingleton<IOnTheFlyPlayer>(new HeldOnTheFlyPlayer());
                services.RemoveAll<IEncodeChapterRepository>();
                services.AddSingleton<IEncodeChapterRepository>(new HeldEncodeChapters());
                services.AddSingleton<IPlaybackPositionRepository>(new HeldPlaybackPositions());
                services.AddSingleton<ISegmentSettingsRepository>(new HeldSegmentSettings());
                services.AddSingleton<ISegmentModelVersionRepository>(new HeldSegmentModelVersions());
                services.AddSingleton<IRecordingDetectionRepository>(new HeldRecordingDetections());
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

    public HeldArtefactCodecs Codecs { get; } = new();

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

    public void Encoded(Recording recording, EncodeTimeline? timeline, EncodeCodec codec = EncodeCodec.H264, string? taggedAs = null)
    {
        EncodeProfile profile = EncodedArtefact.Profile(codec, RecordingFeature.Noon.AddHours(-1));
        Profiles.Profiles.Add(profile);

        EncodeJob job = EncodedArtefact.Made(recording, profile, RecordingFeature.Noon.AddHours(1), timeline);
        Jobs.Jobs.Add(job);

        if (codec is EncodeCodec.H265)
        {
            Codecs.ReadAs(job.ArtefactName!, codec, taggedAs);
        }

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
        TemporaryDirectories.RemoveAll(mounted, encoded, shelved);
    }

    private static long At(double seconds) => (long)Math.Round(seconds * Second);

    private static CaptionPlacement Picture(int left) => new(left, 900, 320, 48, new byte[] { 0x89, 0x50, 0x4e, 0x47, (byte)left });
}
