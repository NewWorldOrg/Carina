using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.Feature.Recordings;

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
            services.AddSingleton<ISkipEventRepository>(Skips);
            services.AddSingleton<ISeekEventRepository>(Seeks);
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

    public HeldSkipEvents Skips { get; } = new();

    public HeldSeekEvents Seeks { get; } = new();

    public HeldSegmentSettings Settings { get; } = new();

    public MovingClock Clock { get; } = new(RecordingFeature.Noon.AddHours(2));

    public static string Path(RecordingId id) => $"/api/recordings/{id.Wire}/segments";

    public static string SkipsPath(RecordingId id) => $"/api/recordings/{id.Wire}/skips";

    public static string SeeksPath(RecordingId id) => $"/api/recordings/{id.Wire}/seeks";

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
            request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
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
