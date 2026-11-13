using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Captions;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Domain.Viewing;
using Carina.Infrastructure.DataBroadcast;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.Feature.DataBroadcast;

internal sealed class DataBroadcastFeature : IAsyncDisposable
{
    public const int Entry = 0x40;

    public const int Other = 0x50;

    public const int EventStream = 0x5E;

    public const uint Download = 7;

    public static readonly OutputRoot Root = new("bulk");

    public static readonly TimeSpan FileBegins = TimeSpan.FromSeconds(6115.5);

    public static readonly EncodeTimeline Timeline =
        new(FileBegins, TimeSpan.FromSeconds(0.5), TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(600));

    private const long Second = StreamClock.Hertz;

    private readonly TestingWebApplicationFactory factory = new();

    private readonly DirectoryInfo mounted = Directory.CreateTempSubdirectory("carina-data-broadcast-mounted-");

    private readonly DirectoryInfo encoded = Directory.CreateTempSubdirectory("carina-data-broadcast-encoded-");

    private readonly DirectoryInfo shelved = Directory.CreateTempSubdirectory("carina-data-broadcast-shelf-");

    public DataBroadcastFeature(bool anywhereToKeepThem = true)
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
                services.AddSingleton<IArtefactCodecReader>(new HeldArtefactCodecs());
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

    public static DataBroadcastRecord Record(TimeSpan? begins = null)
        => new(
            (long)Math.Round((begins ?? FileBegins).TotalSeconds * Second),
            Entry,
            [
                new RecordedCarousel(Other, 9, [Version(Other, 1, 1, 6110, 7900, "logo.png", "image/jpeg", ResourceForm.Binary, [0xFF, 0xD8, 0xFF])]),
                new RecordedCarousel(
                    Entry,
                    Download,
                    [
                        Version(Entry, 0, 1, 6110, 6125.5, "startup.bml", "text/X-arib-bml", ResourceForm.Text, System.Text.Encoding.UTF8.GetBytes("<bml>一</bml>")),
                        Version(Entry, 0, 2, 6125.5, 7900, "startup.bml", "text/X-arib-bml", ResourceForm.Text, System.Text.Encoding.UTF8.GetBytes("<bml>二</bml>")),
                        Version(Entry, 2, 1, 6800, 7900, "news.bml", "text/X-arib-bml", ResourceForm.Text, System.Text.Encoding.UTF8.GetBytes("<bml>三</bml>")),
                    ]),
            ],
            [
                new EventMessage(1, 2, 1, EventTiming.Immediate, At(6120.5), new byte[] { 0x0A, 0x0B }, Entry),
                new EventMessage(1, 3, 2, EventTiming.Npt, At(6900), ReadOnlyMemory<byte>.Empty, EventStream),
            ],
            false,
            autoStart: true);

    public Recording Ended()
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());
        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 4_000, RecordingFeature.Noon.AddMinutes(30));
        Recordings.Recordings.Add(recording);
        File.WriteAllBytes(Path.Combine(mounted.FullName, recording.FileName.Value), new byte[4_000]);

        return recording;
    }

    public async Task<Recording> MadeAsync(DataBroadcastRecord? record = null, bool onTheShelf = true)
    {
        Recording recording = Ended();
        recording.DataBroadcastTaken(3, RecordingFeature.Noon.AddHours(1));

        if (onTheShelf)
        {
            await new DataBroadcastShelf(Settings).KeepAsync(recording.Id, record ?? Record(), CancellationToken.None);
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

    public Task<HttpResponseMessage> CatalogAsync(Recording recording, string query = "", HttpClient? asking = null)
        => (asking ?? Client).GetAsync(new Uri($"/api/videos/{recording.Id.Wire}/data-broadcast{query}", UriKind.Relative));

    public async Task<HttpResponseMessage> ModuleAsync(Recording recording, string module, HttpClient? asking = null, string? ifNoneMatch = null)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, new Uri($"/api/videos/{recording.Id.Wire}/data-broadcast/modules/{module}", UriKind.Relative));

        if (ifNoneMatch is not null)
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", ifNoneMatch);
        }

        return await (asking ?? Client).SendAsync(request);
    }

    public static async Task<JsonElement> DataOfAsync(HttpResponseMessage answer)
    {
        Assert.True(answer.StatusCode is HttpStatusCode.OK, await answer.Content.ReadAsStringAsync());

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        return read.RootElement.GetProperty("data").Clone();
    }

    public static string[] Versions(JsonElement catalog, int tag)
        => [
            .. catalog.GetProperty("carousels").EnumerateArray()
                .Single(carousel => carousel.GetProperty("tag").GetInt32() == tag)
                .GetProperty("versions").EnumerateArray()
                .Select(version => string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{version.GetProperty("id").GetInt32()}/{version.GetProperty("version").GetInt32()} {Math.Round(version.GetProperty("fromSec").GetDouble(), 3)}-{Math.Round(version.GetProperty("toSec").GetDouble(), 3)}")),
        ];

    public static double[] Moments(JsonElement catalog)
        => [.. catalog.GetProperty("events").EnumerateArray().Select(message => Math.Round(message.GetProperty("atSec").GetDouble(), 3))];

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Stranger.Dispose();
        await factory.DisposeAsync();
        TemporaryDirectories.RemoveAll(mounted, encoded, shelved);
    }

    private static ModuleVersion Version(int tag, int moduleId, int version, double first, double last, string path, string mediaType, ResourceForm form, byte[] body)
        => new(tag, moduleId, version, At(first), At(last), [new CarouselResource(path, mediaType, form, body)]);

    private static long At(double seconds) => (long)Math.Round(seconds * Second);
}
