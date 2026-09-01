using System.Net.Http.Headers;

using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class PlaybackFeature : IAsyncDisposable
{
    public static readonly OutputRoot Root = new("bulk");

    private readonly TestingWebApplicationFactory factory = new();

    private readonly DirectoryInfo mounted = Directory.CreateTempSubdirectory("carina-playback-");

    public PlaybackFeature()
    {
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IRecordingDirectory>(Recordings);
                services.AddSingleton(new IntegritySettings
                {
                    OutputRoots = [new StorageRootPath(Root, mounted.FullName)],
                });
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        Stranger = configured.WithTestScheme().CreateClient();
    }

    public string MountedAt => mounted.FullName;

    public HttpClient Client { get; }

    public HttpClient Stranger { get; }

    public HeldRecordings Recordings { get; } = new();

    public Recording Ended(RecordingOutcome outcome, byte[] bytes, bool onDisk = true)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        Recording recording = RecordingFeature.Begin(RecordingId.New());
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

        recording.Settle(outcome, bytes.Length, RecordingFeature.Noon.AddMinutes(30));
        Recordings.Recordings.Add(recording);

        if (onDisk)
        {
            File.WriteAllBytes(Path.Combine(mounted.FullName, recording.FileName.Value), bytes);
        }

        return recording;
    }

    public Recording StillWriting()
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());
        Recordings.Recordings.Add(recording);
        File.WriteAllBytes(Path.Combine(mounted.FullName, recording.FileName.Value), Bytes(1_000));

        return recording;
    }

    public static byte[] Bytes(int count) => [.. Enumerable.Range(0, count).Select(index => (byte)(index % 251))];

    public Task<HttpResponseMessage> GetAsync(Recording recording, string? range = null)
        => SendAsync(HttpMethod.Get, recording, range);

    public Task<HttpResponseMessage> HeadAsync(Recording recording, string? range = null)
        => SendAsync(HttpMethod.Head, recording, range);

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Stranger.Dispose();
        await factory.DisposeAsync();
        mounted.Delete(recursive: true);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, Recording recording, string? range)
    {
        ArgumentNullException.ThrowIfNull(recording);

        using var asking = new HttpRequestMessage(
            method,
            new Uri($"/api/videos/{recording.Id.Wire}", UriKind.Relative));

        if (range is not null)
        {
            asking.Headers.TryAddWithoutValidation("Range", range);
        }

        return await Client.SendAsync(asking);
    }
}
