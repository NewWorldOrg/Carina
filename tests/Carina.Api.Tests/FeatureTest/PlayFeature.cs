using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Playback;
using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Streaming;
using Carina.Domain.Viewing;
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

internal sealed class PlayFeature : IAsyncDisposable
{
    public static readonly OutputRoot Root = new("bulk");

    private readonly TestingWebApplicationFactory factory = new();

    private readonly DirectoryInfo mounted = Directory.CreateTempSubdirectory("carina-play-");

    private readonly DirectoryInfo shelved = Directory.CreateTempSubdirectory("carina-play-shelf-");

    public PlayFeature(IReadOnlyList<VideoCodec>? formats = null)
    {
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                if (formats is not null)
                {
                    services.AddSingleton<ILiveEncoderSelector>(new AlreadyChosen(LiveEncoder.Vaapi, formats));
                }

                services.RemoveAll<IHostedService>();
                services.RemoveAll<IOnTheFlyPlayer>();
                services.AddSingleton<IRecordingDirectory>(Recordings);
                services.AddSingleton<IOnTheFlyPlayer>(Player);
                services.AddSingleton(new IntegritySettings
                {
                    OutputRoots = [new StorageRootPath(Root, mounted.FullName)],
                });
                services.AddSingleton(new EncodeSettings
                {
                    OutputRoots = [new StorageRootPath(EncodedArtefact.Shelf, shelved.FullName)],
                });
                services.AddSingleton<IEncodeJobRepository>(Jobs);
                services.AddSingleton<IEncodeProfileRepository>(Profiles);
                services.RemoveAll<IArtefactCodecReader>();
                services.AddSingleton<IArtefactCodecReader>(Codecs);
                services.RemoveAll<IEncodeChapterRepository>();
                services.AddSingleton<IEncodeChapterRepository>(Chapters);
                services.AddSingleton<IPlaybackPositionRepository>(Positions);
                services.AddSingleton<ISegmentSettingsRepository>(SegmentSettings);
                services.AddSingleton<ISegmentModelVersionRepository>(Versions);
                services.AddSingleton<IRecordingDetectionRepository>(Detections);
            }));

        WebApplicationFactory<Program> serving = configured.WithTestScheme();
        Client = serving.CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        Stranger = configured.WithTestScheme().CreateClient();
        Reads = serving.Services.GetRequiredService<IArtefactOpenings>();
    }

    public IArtefactOpenings Reads { get; }

    public HttpClient Client { get; }

    public HttpClient Stranger { get; }

    public HeldRecordings Recordings { get; } = new();

    public HeldEncodeJobs Jobs { get; } = new();

    public HeldEncodeProfiles Profiles { get; } = new();

    public HeldArtefactCodecs Codecs { get; } = new();

    public HeldEncodeChapters Chapters { get; } = new();

    public HeldPlaybackPositions Positions { get; } = new();

    public HeldOnTheFlyPlayer Player { get; } = new();

    public HeldSegmentSettings SegmentSettings { get; } = new();

    public HeldSegmentModelVersions Versions { get; } = new();

    public HeldRecordingDetections Detections { get; } = new();

    public string MountedAt => mounted.FullName;

    public Recording Ended(
        RecordingOutcome outcome,
        int bytes = 4_000,
        bool onDisk = true,
        AudioMode audio = AudioMode.Undetermined,
        int sounds = ProgrammeSnapshot.SoundsUnannounced)
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New(), audio: audio, sounds: sounds);
        recording.Wrote(TimeSpan.FromMinutes(30));

        if (outcome is RecordingOutcome.Complete)
        {
            recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        }
        else
        {
            recording.Note(new OutcomeDetail(
                RecordingFault.DriverLost,
                null,
                string.Empty,
                RecordingFeature.Noon.AddMinutes(20)));
        }

        recording.Settle(outcome, bytes, RecordingFeature.Noon.AddMinutes(30));
        Recordings.Recordings.Add(recording);

        if (onDisk)
        {
            File.WriteAllBytes(
                Path.Combine(mounted.FullName, recording.FileName.Value),
                [.. Enumerable.Range(0, bytes).Select(index => (byte)(index % 251))]);
        }

        return recording;
    }

    public Recording StillWriting()
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());

        Recordings.Recordings.Add(recording);
        File.WriteAllBytes(Path.Combine(mounted.FullName, recording.FileName.Value), new byte[1_000]);

        return recording;
    }

    public byte[] Encoded(
        Recording recording,
        EncodeCodec codec = EncodeCodec.H264,
        int minutesLater = 0,
        int bytes = 900,
        bool onDisk = true,
        EncodeCodec? fileReadAs = null,
        string? taggedAs = null,
        EncodeTimeline? timeline = null,
        ChapterReading? chapters = null)
    {
        EncodeProfile profile = EncodedArtefact.Profile(codec, RecordingFeature.Noon.AddHours(-1));
        Profiles.Profiles.Add(profile);

        EncodeJob job = EncodedArtefact.Made(
            recording,
            profile,
            RecordingFeature.Noon.AddHours(1).AddMinutes(minutesLater),
            timeline,
            chapters);
        Jobs.Jobs.Add(job);

        if (fileReadAs is { } read)
        {
            Codecs.ReadAs(job.ArtefactName!, read, taggedAs);
        }

        byte[] made = [.. Enumerable.Range(0, bytes).Select(index => (byte)((index * 3) % 251))];

        if (onDisk)
        {
            File.WriteAllBytes(Path.Combine(shelved.FullName, job.ArtefactName!.Value), made);
        }

        return made;
    }

    public async Task<IReadOnlyList<EncodeChapter>> MarkedAsync(EncodeJob job, params ChapterSegment[] segments)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(segments);

        IReadOnlyList<EncodeChapter> marked = EncodeChapter.Mark(job.Id, segments);
        await Chapters.RecordAsync(job.Id, marked, CancellationToken.None);

        return marked;
    }

    public Task<HttpResponseMessage> PlanAsync(Recording recording, string query = "")
        => AskAsync(recording, query, PlayDelivery.Json);

    public Task<HttpResponseMessage> PictureAsync(Recording recording, string query = "")
        => AskAsync(recording, query, "*/*");

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Stranger.Dispose();
        await factory.DisposeAsync();
        TemporaryDirectories.RemoveAll(mounted, shelved);
    }

    public static async Task<JsonElement> PlanOfAsync(HttpResponseMessage answer)
    {
        ArgumentNullException.ThrowIfNull(answer);

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        return read.RootElement.Clone();
    }

    public Task<HttpResponseMessage> PictureAsync(Recording recording, string query, string range)
        => AskAsync(recording, query, "*/*", range);

    private async Task<HttpResponseMessage> AskAsync(
        Recording recording,
        string query,
        string accepting,
        string? range = null)
    {
        ArgumentNullException.ThrowIfNull(recording);

        using var asking = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/api/videos/{recording.Id.Wire}/play{query}", UriKind.Relative));

        asking.Headers.TryAddWithoutValidation("Accept", accepting);

        if (range is not null)
        {
            asking.Headers.TryAddWithoutValidation("Range", range);
        }

        return await Client.SendAsync(asking, HttpCompletionOption.ResponseHeadersRead);
    }
}
