using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class RecordingSegmentsFeature : IAsyncDisposable
{
    private readonly TestingWebApplicationFactory factory = new();

    public RecordingSegmentsFeature()
    {
        Built = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IHostedService>();
            services.AddSingleton<IRecordingDirectory>(Recordings);
            services.AddSingleton<ISegmentModelVersionRepository>(Versions);
            services.AddSingleton<IRecordingDetectionRepository>(Detections);
            services.AddSingleton<ISegmentAnswerRepository>(Answers);
            services.AddSingleton<ISkipEventRepository>(Skips);
            services.AddSingleton<ISegmentSettingsRepository>(Settings);
            services.AddSingleton<TimeProvider>(Clock);
        }));

        Client = Built.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
    }

    public WebApplicationFactory<Program> Built { get; }

    public HttpClient Client { get; }

    public HeldRecordings Recordings { get; } = new();

    public HeldSegmentModelVersions Versions { get; } = new();

    public HeldRecordingDetections Detections { get; } = new();

    public HeldSegmentAnswers Answers { get; } = new();

    public HeldSkipEvents Skips { get; } = new();

    public HeldSegmentSettings Settings { get; } = new();

    public MovingClock Clock { get; } = new(RecordingFeature.Noon.AddHours(2));

    public static string Path(RecordingId id) => $"/api/recordings/{id.Wire}/segments";

    public static string AnswerPath(RecordingId id) => $"/api/recordings/{id.Wire}/segments/answer";

    public static string SkipsPath(RecordingId id) => $"/api/recordings/{id.Wire}/skips";

    public Recording Ended()
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 4_000, RecordingFeature.Noon.AddMinutes(30));
        Recordings.Recordings.Add(recording);

        return recording;
    }

    public Recording StillWriting()
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());

        recording.Wrote(TimeSpan.FromMinutes(12.5));
        Recordings.Recordings.Add(recording);

        return recording;
    }

    public async Task DetectedAsync(RecordingId recording, IReadOnlyList<TimeSpan> snaps, params (double Starts, double Ends, SegmentKind Kind, double Probability)[] stretches)
    {
        SegmentSpan[] segments = [.. stretches.Select(stretch => new SegmentSpan(TimeSpan.FromSeconds(stretch.Starts), TimeSpan.FromSeconds(stretch.Ends), stretch.Kind, stretch.Probability))];

        await Detections.KeepAsync(
            RecordingDetection.Of(
                recording,
                SegmentModelVersion.FirstNumber,
                SegmentDetection.Judged(segments, TimeSpan.FromMinutes(30), snaps),
                2,
                RecordingFeature.Noon.AddMinutes(31),
                RecordingFeature.Noon.AddMinutes(32)),
            CancellationToken.None);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path, HttpClient? client = null)
    {
        using HttpResponseMessage response = await (client ?? Client).GetAsync(new Uri(path, UriKind.Relative));
        string body = await response.Content.ReadAsStringAsync();

        if (body.Length is 0)
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }

    public Task<(HttpStatusCode Status, JsonElement Body)> PutAsync(string path, string json, HttpClient? client = null)
        => SendAsync(HttpMethod.Put, path, json, client);

    public Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, string json, HttpClient? client = null)
        => SendAsync(HttpMethod.Post, path, json, client);

    public Task<(HttpStatusCode Status, JsonElement Body)> DeleteAsync(string path, HttpClient? client = null)
        => SendAsync(HttpMethod.Delete, path, null, client);

    public HttpClient Anonymous() => Built.WithTestScheme().CreateClient();

    public HttpClient FromElsewhere()
    {
        HttpClient client = Built.WithTestScheme().CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
        client.DefaultRequestHeaders.Remove(Microsoft.Net.Http.Headers.HeaderNames.Origin);
        client.DefaultRequestHeaders.Add(Microsoft.Net.Http.Headers.HeaderNames.Origin, "https://elsewhere.example");

        return client;
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> SendAsync(HttpMethod method, string path, string? json, HttpClient? client)
    {
        using var request = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));

        if (json is not null)
        {
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await (client ?? Client).SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{'))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
    }
}

public sealed class RecordingSegmentsEndpointTests
{
    [Fact(DisplayName = "BR-CS-002: the stretches the version in use detected are answered in the recording's own time, with the boundary candidates' seconds")]
    public async Task TheDetectedStretchesAreAnsweredInTheRecordingsOwnTime()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        await feature.DetectedAsync(
            recording.Id,
            [TimeSpan.FromSeconds(912.28), TimeSpan.FromSeconds(912.31), TimeSpan.FromSeconds(1002.3), TimeSpan.FromSeconds(1347)],
            (0, 912.3, SegmentKind.Programme, 0.98),
            (912.3, 1002.3, SegmentKind.Break, 0.93),
            (1002.3, 1800, SegmentKind.Programme, 0.99));

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id));
        JsonElement data = body.GetProperty("data");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            ["answeredAt", "cmShare", "confirmed", "detectedAt", "lengthSec", "modelVersion", "segments", "snaps", "source", "state"],
            data.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(1800, data.GetProperty("lengthSec").GetDouble());
        Assert.Equal("detected", data.GetProperty("source").GetString());
        Assert.Equal("marked", data.GetProperty("state").GetString());
        Assert.Equal(1, data.GetProperty("modelVersion").GetInt32());
        Assert.False(data.GetProperty("confirmed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("answeredAt").ValueKind);
        Assert.Equal(RecordingFeature.Noon.AddMinutes(32), data.GetProperty("detectedAt").GetDateTime().ToUniversalTime());
        Assert.Equal(0.05, data.GetProperty("cmShare").GetDouble());
        Assert.Equal(
            [(0d, 912.3d, "programme", 0.98), (912.3d, 1002.3d, "break", 0.93), (1002.3d, 1800d, "programme", 0.99)],
            data.GetProperty("segments").EnumerateArray().Select(segment => (
                segment.GetProperty("startsAtSec").GetDouble(),
                segment.GetProperty("endsAtSec").GetDouble(),
                segment.GetProperty("kind").GetString()!,
                segment.GetProperty("probability").GetDouble())));
        Assert.Equal([912.28, 912.31, 1002.3, 1347], data.GetProperty("snaps").EnumerateArray().Select(snap => snap.GetDouble()));
    }

    [Fact(DisplayName = "BR-CS-002: a recording where only the programme was found is answered as nothing found, apart from one not detected")]
    public async Task NothingFoundIsApartFromNotDetected()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording plain = feature.Ended();
        Recording notDetected = feature.Ended();
        await feature.DetectedAsync(plain.Id, [], (0, 1800, SegmentKind.Programme, 0.97));

        JsonElement found = (await feature.GetAsync(RecordingSegmentsFeature.Path(plain.Id))).Body.GetProperty("data");
        JsonElement none = (await feature.GetAsync(RecordingSegmentsFeature.Path(notDetected.Id))).Body.GetProperty("data");

        Assert.Equal(("detected", "nothingFound", 0d), (found.GetProperty("source").GetString(), found.GetProperty("state").GetString(), found.GetProperty("cmShare").GetDouble()));
        Assert.Single(found.GetProperty("segments").EnumerateArray());
        Assert.Equal(("none", "none"), (none.GetProperty("source").GetString(), none.GetProperty("state").GetString()));
        Assert.Equal(JsonValueKind.Null, none.GetProperty("modelVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("detectedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("cmShare").ValueKind);
        Assert.Empty(none.GetProperty("segments").EnumerateArray());
        Assert.Empty(none.GetProperty("snaps").EnumerateArray());
        Assert.Equal(1800, none.GetProperty("lengthSec").GetDouble());
    }

    [Fact(DisplayName = "BR-CS-002: a recording whose sequence was thrown away is answered with no stretches and the share breaks took")]
    public async Task ASequenceThrownAwayIsAnsweredWithTheShareAlone()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        await feature.DetectedAsync(recording.Id, [TimeSpan.FromSeconds(600)], (0, 600, SegmentKind.Programme, 0.6), (600, 1800, SegmentKind.Break, 0.8));

        JsonElement data = (await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id))).Body.GetProperty("data");

        Assert.Equal("discarded", data.GetProperty("state").GetString());
        Assert.Empty(data.GetProperty("segments").EnumerateArray());
        Assert.Equal(0.6667, data.GetProperty("cmShare").GetDouble());
        Assert.Equal([600d], data.GetProperty("snaps").EnumerateArray().Select(snap => snap.GetDouble()));
    }

    [Fact(DisplayName = "a recording still being written is answered, with the length written so far")]
    public async Task ARecordingStillBeingWrittenIsAnswered()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.StillWriting();

        (HttpStatusCode status, JsonElement body) = await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(750, body.GetProperty("data").GetProperty("lengthSec").GetDouble());
        Assert.Equal("none", body.GetProperty("data").GetProperty("state").GetString());
    }

    [Fact(DisplayName = "a recording whose row is gone is not found, whatever detection is kept of it, and an id that is not one is refused")]
    public async Task ARecordingWhoseRowIsGoneIsNotFound()
    {
        await using var feature = new RecordingSegmentsFeature();
        RecordingId gone = RecordingId.New();
        await feature.DetectedAsync(gone, [], (0, 1800, SegmentKind.Programme, 0.97));

        (HttpStatusCode missing, JsonElement body) = await feature.GetAsync(RecordingSegmentsFeature.Path(gone));
        (HttpStatusCode malformed, _) = await feature.GetAsync("/api/recordings/not-an-id/segments");

        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.False(body.GetProperty("status").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("data").ValueKind);
        Assert.Equal(HttpStatusCode.BadRequest, malformed);
    }

    [Fact(DisplayName = "a caller who has not signed in cannot read the stretches of a recording")]
    public async Task ACallerWhoHasNotSignedInCannotRead()
    {
        await using var feature = new RecordingSegmentsFeature();
        Recording recording = feature.Ended();
        using HttpClient anonymous = feature.Anonymous();

        (HttpStatusCode status, _) = await feature.GetAsync(RecordingSegmentsFeature.Path(recording.Id), anonymous);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal(0, feature.Versions.Reads);
    }
}
