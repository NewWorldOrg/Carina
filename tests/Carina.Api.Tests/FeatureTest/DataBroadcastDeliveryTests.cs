using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

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

namespace Carina.Api.Tests.FeatureTest;

internal sealed class DataBroadcastFeature : IAsyncDisposable
{
    public const int Entry = 0x40;

    public const int Other = 0x50;

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
                        Version(Entry, 0, 1, 6110, 6125.5, "startup.bml", "text/X-arib-bml", ResourceForm.Text, Encoding.UTF8.GetBytes("<bml>一</bml>")),
                        Version(Entry, 0, 2, 6125.5, 7900, "startup.bml", "text/X-arib-bml", ResourceForm.Text, Encoding.UTF8.GetBytes("<bml>二</bml>")),
                        Version(Entry, 2, 1, 6800, 7900, "news.bml", "text/X-arib-bml", ResourceForm.Text, Encoding.UTF8.GetBytes("<bml>三</bml>")),
                    ]),
            ],
            [
                new EventMessage(1, 2, 1, EventTiming.Immediate, At(6120.5), new byte[] { 0x0A, 0x0B }),
                new EventMessage(1, 3, 2, EventTiming.Npt, At(6900), ReadOnlyMemory<byte>.Empty),
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
        mounted.Delete(recursive: true);
        encoded.Delete(recursive: true);
        shelved.Delete(recursive: true);
    }

    private static ModuleVersion Version(int tag, int moduleId, int version, double first, double last, string path, string mediaType, ResourceForm form, byte[] body)
        => new(tag, moduleId, version, At(first), At(last), [new CarouselResource(path, mediaType, form, body)]);

    private static long At(double seconds) => (long)Math.Round(seconds * Second);
}

public sealed class DataBroadcastDeliveryTests
{
    [Fact(DisplayName = "BR-BD-006: on the recording itself the catalog places every version and event at its moment less where the file's clock begins")]
    public async Task OnTheRecordingItselfEveryVersionAndEventIsAtItsMomentLessWhereTheClockBegins()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();

        using HttpResponseMessage answer = await feature.CatalogAsync(recording, "?source=recording");
        JsonElement catalog = await DataBroadcastFeature.DataOfAsync(answer);

        Assert.Equal(
            ["entryTag", "autoStart", "startup", "incomplete", "carousels", "events"],
            catalog.EnumerateObject().Select(property => property.Name));
        Assert.Equal(
            (DataBroadcastFeature.Entry, true, "/40/0000/startup.bml", false),
            (catalog.GetProperty("entryTag").GetInt32(), catalog.GetProperty("autoStart").GetBoolean(), catalog.GetProperty("startup").GetString(), catalog.GetProperty("incomplete").GetBoolean()));
        Assert.Equal(
            [(DataBroadcastFeature.Entry, DataBroadcastFeature.Download), (DataBroadcastFeature.Other, 9u)],
            catalog.GetProperty("carousels").EnumerateArray().Select(carousel => (carousel.GetProperty("tag").GetInt32(), carousel.GetProperty("downloadId").GetUInt32())));
        Assert.Equal(["0/1 0-10", "0/2 10-1784.5", "2/1 684.5-1784.5"], DataBroadcastFeature.Versions(catalog, DataBroadcastFeature.Entry));
        Assert.Equal(["1/1 0-1784.5"], DataBroadcastFeature.Versions(catalog, DataBroadcastFeature.Other));
        Assert.Equal([5.0, 784.5], DataBroadcastFeature.Moments(catalog));
        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());

        JsonElement message = catalog.GetProperty("events")[0];
        Assert.Equal(
            ["group", "id", "type", "immediate", "atSec", "privateData"],
            message.EnumerateObject().Select(property => property.Name));
        Assert.Equal((1, 2, 1, true), (message.GetProperty("group").GetInt32(), message.GetProperty("id").GetInt32(), message.GetProperty("type").GetInt32(), message.GetProperty("immediate").GetBoolean()));
        Assert.Equal(new byte[] { 0x0A, 0x0B }, message.GetProperty("privateData").GetBytesFromBase64());
        Assert.False(catalog.GetProperty("events")[1].GetProperty("immediate").GetBoolean());
    }

    [Fact(DisplayName = "BR-BD-006: on the artefact every version and event is at its moment less the job's shift, so the same scene has the same second on both files")]
    public async Task OnTheArtefactEveryMomentIsLessTheJobsShiftAndAgreesWithTheRecording()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();
        feature.Encoded(recording, DataBroadcastFeature.Timeline);

        using HttpResponseMessage onTheArtefact = await feature.CatalogAsync(recording, "?source=artefact");
        using HttpResponseMessage onTheRecording = await feature.CatalogAsync(recording, "?source=recording");
        JsonElement artefact = await DataBroadcastFeature.DataOfAsync(onTheArtefact);
        JsonElement itself = await DataBroadcastFeature.DataOfAsync(onTheRecording);

        Assert.Equal(["0/1 0-9.5", "0/2 9.5-600"], DataBroadcastFeature.Versions(artefact, DataBroadcastFeature.Entry));
        Assert.Equal([4.5], DataBroadcastFeature.Moments(artefact));
        Assert.Equal(
            DataBroadcastFeature.Moments(itself)[0] - 0.5,
            DataBroadcastFeature.Moments(artefact)[0]);
        Assert.Equal(
            itself.GetProperty("carousels")[0].GetProperty("versions")[1].GetProperty("fromSec").GetDouble() - 0.5,
            artefact.GetProperty("carousels")[0].GetProperty("versions")[1].GetProperty("fromSec").GetDouble(),
            3);
    }

    [Fact(DisplayName = "BR-BD-006: from a later second only the versions still running there and the events firing there or later are given")]
    public async Task FromALaterSecondOnlyWhatRunsThereOrLaterIsGiven()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();

        using HttpResponseMessage answer = await feature.CatalogAsync(recording, "?source=recording&from=10.5");
        JsonElement catalog = await DataBroadcastFeature.DataOfAsync(answer);

        Assert.Equal(["0/2 10-1784.5", "2/1 684.5-1784.5"], DataBroadcastFeature.Versions(catalog, DataBroadcastFeature.Entry));
        Assert.Equal([784.5], DataBroadcastFeature.Moments(catalog));
    }

    [Fact(DisplayName = "BR-BA-001: each version is as large as the module surface answers it, and that answer is the side channel's module byte for byte")]
    public async Task EachModuleIsTheSideChannelsModuleByteForByte()
    {
        await using DataBroadcastFeature feature = new();
        DataBroadcastRecord record = DataBroadcastFeature.Record();
        Recording recording = await feature.MadeAsync(record);
        JsonElement catalog = await DataBroadcastFeature.DataOfAsync(await feature.CatalogAsync(recording, "?source=recording"));

        foreach (RecordedCarousel carousel in record.Carousels)
        {
            foreach (ModuleVersion version in carousel.Versions)
            {
                using HttpResponseMessage answer = await feature.ModuleAsync(recording, $"{carousel.Tag}/{carousel.DownloadId}/{version.ModuleId}/{version.Version}");
                byte[] body = await answer.Content.ReadAsByteArrayAsync();
                JsonElement listed = catalog.GetProperty("carousels").EnumerateArray()
                    .Single(each => each.GetProperty("tag").GetInt32() == carousel.Tag)
                    .GetProperty("versions").EnumerateArray()
                    .Single(each => each.GetProperty("id").GetInt32() == version.ModuleId && each.GetProperty("version").GetInt32() == version.Version);

                Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
                Assert.Equal(DataBroadcastFrames.ModulePayload(version), body);
                Assert.Equal(DataBroadcastFrames.ModuleKind, body[0]);
                Assert.Equal(body.Length, listed.GetProperty("size").GetInt64());
                Assert.Equal("application/octet-stream", answer.Content.Headers.ContentType?.MediaType);
                Assert.Equal((true, true, false, null), (answer.Headers.CacheControl!.Private, answer.Headers.CacheControl.NoCache, answer.Headers.CacheControl.NoStore, answer.Headers.CacheControl.MaxAge));
                Assert.False(answer.Headers.ETag!.IsWeak);
            }
        }
    }

    [Fact(DisplayName = "BR-BA-001: a module asked for again with the tag it was answered with is not sent again")]
    public async Task AModuleAskedForAgainWithItsTagIsNotSentAgain()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();
        using HttpResponseMessage first = await feature.ModuleAsync(recording, "64/7/0/1");
        EntityTagHeaderValue tag = first.Headers.ETag!;

        using HttpResponseMessage again = await feature.ModuleAsync(recording, "64/7/0/1", ifNoneMatch: tag.Tag);
        using HttpResponseMessage otherwise = await feature.ModuleAsync(recording, "64/7/0/1", ifNoneMatch: "\"another\"");

        Assert.Equal(HttpStatusCode.NotModified, again.StatusCode);
        Assert.Empty(await again.Content.ReadAsByteArrayAsync());
        Assert.Equal(tag, again.Headers.ETag);
        Assert.True(again.Headers.CacheControl!.NoCache);
        Assert.Equal(HttpStatusCode.OK, otherwise.StatusCode);
        Assert.Equal(tag, otherwise.Headers.ETag);
    }

    [Fact(DisplayName = "BR-BA-001: a record taken again answers its modules under another tag, so what was held before is sent again")]
    public async Task ARecordTakenAgainAnswersItsModulesUnderAnotherTag()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();
        using HttpResponseMessage before = await feature.ModuleAsync(recording, "64/7/0/1");
        EntityTagHeaderValue held = before.Headers.ETag!;

        recording.DataBroadcastAgain();
        recording.DataBroadcastTaken(3, RecordingFeature.Noon.AddHours(5));
        await new DataBroadcastShelf(feature.Settings).KeepAsync(recording.Id, DataBroadcastFeature.Record(), CancellationToken.None);

        using HttpResponseMessage after = await feature.ModuleAsync(recording, "64/7/0/1", ifNoneMatch: held.Tag);

        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
        Assert.NotEqual(held, after.Headers.ETag);
        Assert.Equal(await before.Content.ReadAsByteArrayAsync(), await after.Content.ReadAsByteArrayAsync());
    }

    [Theory(DisplayName = "BR-BA-001: a module the record does not hold is not found, and the refusal is never held")]
    [InlineData("64/7/0/3")]
    [InlineData("64/8/0/1")]
    [InlineData("80/7/0/1")]
    [InlineData("64/7/5/1")]
    public async Task AModuleTheRecordDoesNotHoldIsNotFound(string module)
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();

        using HttpResponseMessage answer = await feature.ModuleAsync(recording, module);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
        Assert.Equal("no-store, private", answer.Headers.CacheControl?.ToString());
    }

    [Theory(DisplayName = "BR-BA-001: a module asked for by numbers no carousel, download, module or version can have is refused")]
    [InlineData("256/7/0/1")]
    [InlineData("-1/7/0/1")]
    [InlineData("64/4294967296/0/1")]
    [InlineData("64/7/65536/1")]
    [InlineData("64/7/0/256")]
    [InlineData("64/seven/0/1")]
    public async Task AModuleAskedForByNumbersNothingCanHaveIsRefused(string module)
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();

        using HttpResponseMessage answer = await feature.ModuleAsync(recording, module);

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Fact(DisplayName = "BR-BS-001: a record still being taken is refused as coming, and so are its modules not found")]
    public async Task ARecordStillBeingTakenIsComing()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = feature.Ended();

        using HttpResponseMessage catalog = await feature.CatalogAsync(recording);
        using HttpResponseMessage module = await feature.ModuleAsync(recording, "64/7/0/1");

        Assert.Equal(HttpStatusCode.Conflict, catalog.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, module.StatusCode);
    }

    [Fact(DisplayName = "BR-BS-001: a record the row says is made with nothing on the shelf is coming, as it is taken again")]
    public async Task ARecordMadeWithNothingOnTheShelfIsComing()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync(onTheShelf: false);

        using HttpResponseMessage answer = await feature.CatalogAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
        Assert.Equal(DataBroadcastState.Made, recording.DataBroadcastState);
    }

    [Fact(DisplayName = "BR-BS-001: a record that failed with tries left is coming, and one that failed every try is not found")]
    public async Task ARecordThatFailedIsComingWhileTriesAreLeftAndThenNotFound()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = feature.Ended();
        recording.DataBroadcastFailed(RecordingFeature.Noon.AddHours(1));

        using HttpResponseMessage once = await feature.CatalogAsync(recording);

        recording.DataBroadcastAgain();
        recording.DataBroadcastFailed(RecordingFeature.Noon.AddHours(2));
        recording.DataBroadcastAgain();
        recording.DataBroadcastFailed(RecordingFeature.Noon.AddHours(3));

        using HttpResponseMessage everyTry = await feature.CatalogAsync(recording);

        Assert.Equal(HttpStatusCode.Conflict, once.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, everyTry.StatusCode);
    }

    [Fact(DisplayName = "BR-BS-001: a recording with no data broadcast is not found, and nor are its modules")]
    public async Task ARecordingWithNoDataBroadcastIsNotFound()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = feature.Ended();
        recording.DataBroadcastTaken(0, RecordingFeature.Noon.AddHours(1));
        await new DataBroadcastShelf(feature.Settings).KeepAsync(recording.Id, DataBroadcastFeature.Record(), CancellationToken.None);

        using HttpResponseMessage catalog = await feature.CatalogAsync(recording);
        using HttpResponseMessage module = await feature.ModuleAsync(recording, "64/7/0/1");

        Assert.Equal(HttpStatusCode.NotFound, catalog.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, module.StatusCode);
    }

    [Fact(DisplayName = "BR-BS-001: a record whose head reads and whose body does not is not found")]
    public async Task ARecordWhoseHeadReadsAndWhoseBodyDoesNotIsNotFound()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();
        string kept = new DataBroadcastShelf(feature.Settings).PathOf(recording.Id)!;
        byte[] whole = await File.ReadAllBytesAsync(kept);
        await File.WriteAllBytesAsync(kept, whole[..^3]);

        using HttpResponseMessage answer = await feature.CatalogAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Fact(DisplayName = "BR-BD-006: an artefact made from a file whose clock began elsewhere has no data broadcast placed over it, and the recording itself still does")]
    public async Task AnArtefactWhoseClockDisagreesHasNoDataBroadcastOverIt()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();
        feature.Encoded(recording, new EncodeTimeline(DataBroadcastFeature.FileBegins + TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(0.5), null, null));

        using HttpResponseMessage overTheArtefact = await feature.CatalogAsync(recording, "?source=artefact");
        using HttpResponseMessage overTheRecording = await feature.CatalogAsync(recording, "?source=recording");

        Assert.Equal(HttpStatusCode.NotFound, overTheArtefact.StatusCode);
        Assert.Equal(HttpStatusCode.OK, overTheRecording.StatusCode);
    }

    [Fact(DisplayName = "BR-BS-001: with nowhere to keep records there is no data broadcast")]
    public async Task WithNowhereToKeepRecordsThereIsNone()
    {
        await using DataBroadcastFeature feature = new(anywhereToKeepThem: false);
        Recording recording = feature.Ended();
        recording.DataBroadcastTaken(3, RecordingFeature.Noon.AddHours(1));

        using HttpResponseMessage answer = await feature.CatalogAsync(recording);

        Assert.Equal(HttpStatusCode.NotFound, answer.StatusCode);
    }

    [Theory(DisplayName = "the catalog asked from something that is not a second, a source or a decoding is refused")]
    [InlineData("?from=-1")]
    [InlineData("?from=soon")]
    [InlineData("?from=NaN")]
    [InlineData("?source=elsewhere")]
    [InlineData("?decodes=vp9")]
    public async Task AskingForSomethingThatIsNotASecondASourceOrADecodingIsRefused(string query)
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();

        using HttpResponseMessage answer = await feature.CatalogAsync(recording, query);

        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
    }

    [Fact(DisplayName = "BR-BS-001: a recording nobody has is not found, nor are its modules")]
    public async Task ARecordingNobodyHasIsNotFound()
    {
        await using DataBroadcastFeature feature = new();
        string id = RecordingId.New().Wire;

        using HttpResponseMessage catalog = await feature.Client.GetAsync(new Uri($"/api/videos/{id}/data-broadcast", UriKind.Relative));
        using HttpResponseMessage module = await feature.Client.GetAsync(new Uri($"/api/videos/{id}/data-broadcast/modules/64/7/0/1", UriKind.Relative));

        Assert.Equal(HttpStatusCode.NotFound, catalog.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, module.StatusCode);
    }

    [Fact(DisplayName = "BR-BS-001: a recording still being written is refused the way its plan is")]
    public async Task ARecordingStillBeingWrittenIsRefusedTheWayItsPlanIs()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = RecordingFeature.Begin(RecordingId.New());
        feature.Recordings.Recordings.Add(recording);

        using HttpResponseMessage answer = await feature.CatalogAsync(recording);

        Assert.Equal(HttpStatusCode.Conflict, answer.StatusCode);
    }

    [Fact(DisplayName = "BR-BA-001: nobody without a session is answered the catalog or a module")]
    public async Task NobodyWithoutASessionIsAnswered()
    {
        await using DataBroadcastFeature feature = new();
        Recording recording = await feature.MadeAsync();

        using HttpResponseMessage catalog = await feature.CatalogAsync(recording, asking: feature.Stranger);
        using HttpResponseMessage module = await feature.ModuleAsync(recording, "64/7/0/1", feature.Stranger);

        Assert.Equal(HttpStatusCode.Unauthorized, catalog.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, module.StatusCode);
    }
}
