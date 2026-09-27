using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using Carina.Api.Playback;
using Carina.Domain.Encodings;
using Carina.Domain.Events;
using Carina.Domain.Integrity;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
using Carina.Domain.Viewing;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class MarkedRecordingFeature : IAsyncDisposable
{
    public static readonly OutputRoot Root = new("bulk");

    private readonly TestingWebApplicationFactory factory = new();

    private readonly DirectoryInfo mounted = Directory.CreateTempSubdirectory("carina-marked-");

    private readonly DirectoryInfo shelved = Directory.CreateTempSubdirectory("carina-marked-shelf-");

    public MarkedRecordingFeature()
    {
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IOnTheFlyPlayer>();
                services.AddSingleton<IRecordingDirectory>(Recordings);
                services.AddSingleton<IOnTheFlyPlayer>(Player);
                services.AddSingleton<IEncodeStandingReader>(Jobs);
                services.AddSingleton<IEncodeJobRepository>(Jobs);
                services.AddSingleton<IEncodeProfileRepository>(Profiles);
                services.AddSingleton<IPlaybackPositionRepository>(new HeldPlaybackPositions());
                services.AddSingleton<IQualityThresholdRepository>(Thresholds);
                services.AddSingleton<IAppEventPublisher>(Events);
                services.AddSingleton(new IntegritySettings
                {
                    OutputRoots = [new StorageRootPath(Root, mounted.FullName)],
                });
                services.AddSingleton(new EncodeSettings
                {
                    OutputRoots = [new StorageRootPath(EncodedArtefact.Shelf, shelved.FullName)],
                });
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
    }

    public HttpClient Client { get; }

    public HeldRecordings Recordings { get; } = new();

    public HeldEncodeJobs Jobs { get; } = new();

    public HeldEncodeProfiles Profiles { get; } = new();

    public HeldQualityThresholds Thresholds { get; } = new();

    public SilentEvents Events { get; } = new();

    public HeldOnTheFlyPlayer Player { get; } = new();

    public Recording EndedWithDropsAt(params int[] seconds)
    {
        ArgumentNullException.ThrowIfNull(seconds);

        Recording recording = RecordingFeature.Begin(RecordingId.New());
        int bytes = 4_000;

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Measure(
            DropCounters.Counted(seconds.Length, 1_000),
            DropTimeline.Rehydrate(
                900_000,
                [.. seconds.Order().Select(second => new DropBucket(second, 1, 0))],
                []),
            null,
            0,
            RecordingFeature.Noon.AddMinutes(20));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, bytes, RecordingFeature.Noon.AddMinutes(30));
        Recordings.Recordings.Add(recording);

        File.WriteAllBytes(
            Path.Combine(mounted.FullName, recording.FileName.Value),
            [.. Enumerable.Range(0, bytes).Select(index => (byte)(index % 251))]);

        return recording;
    }

    public async Task<IReadOnlyList<int>> SecondsTheDetailMarksAsync(Recording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        using HttpResponseMessage answer = await Client.GetAsync(
            new Uri($"/api/recordings/{recording.Id.Wire}", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());
        JsonElement positions = read.RootElement.GetProperty("data").GetProperty("positions");

        Assert.True(positions.GetProperty("located").GetBoolean());

        return
        [
            .. positions
                .GetProperty("buckets")
                .EnumerateArray()
                .Select(bucket => bucket.GetProperty("second").GetInt32()),
        ];
    }

    public async Task<HttpResponseMessage> PlayedFromAsync(Recording recording, int second)
    {
        ArgumentNullException.ThrowIfNull(recording);

        using var asking = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri($"/api/videos/{recording.Id.Wire}/play?{PlayDelivery.Position}={second}", UriKind.Relative));

        asking.Headers.TryAddWithoutValidation("Accept", "*/*");

        return await Client.SendAsync(asking, HttpCompletionOption.ResponseHeadersRead);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();

        if (Directory.Exists(mounted.FullName))
        {
            mounted.Delete(recursive: true);
        }

        if (Directory.Exists(shelved.FullName))
        {
            shelved.Delete(recursive: true);
        }
    }
}

public sealed class DropPositionsArePlayablePositionsTests
{
    [Fact]
    public async Task TheSecondTheDetailMarksADropAtIsTheSecondThePlayerIsAskedToStartAt()
    {
        await using var feature = new MarkedRecordingFeature();
        Recording recording = feature.EndedWithDropsAt(12);

        int marked = Assert.Single(await feature.SecondsTheDetailMarksAsync(recording));

        using HttpResponseMessage played = await feature.PlayedFromAsync(recording, marked);

        Assert.Equal(12, marked);
        Assert.Equal(HttpStatusCode.OK, played.StatusCode);
        Assert.Equal("12", Header(played, PlaybackHeaders.StartsAt));
        Assert.Equal(TimeSpan.FromSeconds(12), Assert.Single(feature.Player.AskedFrom));
    }

    [Fact]
    public async Task EverySecondTheDetailMarksIsOneThePlayerStartsAtRatherThanOnlyTheFirst()
    {
        await using var feature = new MarkedRecordingFeature();
        Recording recording = feature.EndedWithDropsAt(12, 40, 903);

        IReadOnlyList<int> marked = await feature.SecondsTheDetailMarksAsync(recording);

        foreach (int second in marked)
        {
            using HttpResponseMessage played = await feature.PlayedFromAsync(recording, second);

            Assert.Equal(HttpStatusCode.OK, played.StatusCode);
            Assert.Equal(
                second.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Header(played, PlaybackHeaders.StartsAt));
        }

        Assert.Equal([12, 40, 903], marked);
        Assert.Equal(
            [TimeSpan.FromSeconds(12), TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(903)],
            feature.Player.AskedFrom);
    }

    private static string? Header(HttpResponseMessage answer, string named)
        => answer.Headers.TryGetValues(named, out IEnumerable<string>? values) ? values.Single() : null;
}
