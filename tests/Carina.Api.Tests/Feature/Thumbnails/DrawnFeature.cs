using System.Net.Http.Headers;

using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.Feature.Thumbnails;

internal sealed class DrawnFeature : IAsyncDisposable
{
    private readonly TestingWebApplicationFactory factory = new();

    private readonly DirectoryInfo pictures = Directory.CreateTempSubdirectory("carina-pictures-");

    public DrawnFeature(bool anywhereToPutThem = true)
    {
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<ThumbnailSettings>();
                services.AddSingleton<IRecordingDirectory>(Recordings);
                services.AddSingleton(new ThumbnailSettings
                {
                    WrittenTo = anywhereToPutThem ? pictures.FullName : null,
                });
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        Stranger = configured.WithTestScheme().CreateClient();
    }

    public HttpClient Client { get; }

    public HttpClient Stranger { get; }

    public HeldRecordings Recordings { get; } = new();

    public string PicturesAt => pictures.FullName;

    public static byte[] APicture => [.. Enumerable.Range(0, 512).Select(index => (byte)(index % 251))];

    public Recording Illustrated(ThumbnailState state, bool onDisk = true)
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 4_000, RecordingFeature.Noon.AddMinutes(30));
        recording.Illustrate(state, state is ThumbnailState.Failed ? ThumbnailFault.Refused : null);
        Recordings.Recordings.Add(recording);

        if (onDisk)
        {
            File.WriteAllBytes(Path.Combine(pictures.FullName, recording.Id.Wire + ".jpg"), APicture);
        }

        return recording;
    }

    public Task<HttpResponseMessage> GetAsync(Recording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return Client.GetAsync(new Uri($"/api/videos/{recording.Id.Wire}/thumbnail", UriKind.Relative));
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Stranger.Dispose();
        await factory.DisposeAsync();
        pictures.Delete(recursive: true);
    }
}
