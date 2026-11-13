using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class TicketedFeature : IAsyncDisposable
{
    public static readonly OutputRoot Root = new("bulk");

    private readonly TestingWebApplicationFactory factory = new();

    private readonly DirectoryInfo mounted = Directory.CreateTempSubdirectory("carina-ticketed-");

    private readonly DirectoryInfo shelved = Directory.CreateTempSubdirectory("carina-ticketed-shelf-");

    public TicketedFeature()
        : this(null)
    {
    }

    public TicketedFeature(RecordedStartup? heard)
    {
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                if (heard is not null)
                {
                    services.AddSingleton<ILoggerProvider>(heard);
                }

                services.RemoveAll<IHostedService>();
                services.AddSingleton<IRecordingDirectory>(Recordings);
                services.AddSingleton<IEncodeJobRepository>(Jobs);
                services.AddSingleton<IEncodeProfileRepository>(Profiles);
                services.RemoveAll<IArtefactCodecReader>();
                services.AddSingleton<IArtefactCodecReader>(Codecs);
                services.AddSingleton(new IntegritySettings
                {
                    OutputRoots = [new StorageRootPath(Root, mounted.FullName)],
                });
                services.AddSingleton(new EncodeSettings
                {
                    OutputRoots = [new StorageRootPath(EncodedArtefact.Shelf, shelved.FullName)],
                });
            }));

        if (heard is not null)
        {
            configured = configured.WithWebHostBuilder(builder => builder
                .UseSetting("Logging:LogLevel:Default", nameof(LogLevel.Trace))
                .UseSetting("Logging:LogLevel:Microsoft", nameof(LogLevel.Trace))
                .UseSetting("Logging:LogLevel:Microsoft.AspNetCore", nameof(LogLevel.Trace))
                .UseSetting("Logging:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics", nameof(LogLevel.Trace))
                .UseSetting($"Logging:{typeof(RecordedStartup).FullName}:LogLevel:Default", nameof(LogLevel.Trace))
                .UseSetting(
                    $"Logging:{typeof(RecordedStartup).FullName}:LogLevel:Microsoft.AspNetCore.Hosting.Diagnostics",
                    nameof(LogLevel.Trace))
                .UseSetting(
                    $"Logging:{typeof(RecordedStartup).FullName}:LogLevel:Microsoft.AspNetCore.Routing.Matching.DfaMatcher",
                    nameof(LogLevel.Trace)));
        }

        WebApplicationFactory<Program> served = configured.WithTestScheme();

        Client = served.CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        Player = served.CreateClient();
    }

    public HttpClient Client { get; }

    public HttpClient Player { get; }

    public HeldRecordings Recordings { get; } = new();

    public HeldEncodeJobs Jobs { get; } = new();

    public HeldEncodeProfiles Profiles { get; } = new();

    public HeldArtefactCodecs Codecs { get; } = new();

    public byte[] Written { get; } = [.. Enumerable.Range(0, 4_000).Select(index => (byte)(index % 251))];

    public Recording Ended()
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(RecordingFeature.Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, Written.Length, RecordingFeature.Noon.AddMinutes(30));
        Recordings.Recordings.Add(recording);
        File.WriteAllBytes(Path.Combine(mounted.FullName, recording.FileName.Value), Written);

        return recording;
    }

    public Recording StillWriting()
    {
        Recording recording = RecordingFeature.Begin(RecordingId.New());

        Recordings.Recordings.Add(recording);

        return recording;
    }

    public byte[] Encoded(Recording recording, EncodeCodec profileSays, EncodeCodec fileReadAs, string? taggedAs = null)
    {
        EncodeProfile profile = EncodedArtefact.Profile(profileSays, RecordingFeature.Noon.AddHours(-1));
        Profiles.Profiles.Add(profile);

        EncodeJob job = EncodedArtefact.Made(recording, profile, RecordingFeature.Noon.AddHours(1));
        Jobs.Jobs.Add(job);
        Codecs.ReadAs(job.ArtefactName!, fileReadAs, taggedAs);

        byte[] made = [.. Enumerable.Range(0, 900).Select(index => (byte)((index * 7) % 251))];
        File.WriteAllBytes(Path.Combine(shelved.FullName, job.ArtefactName!.Value), made);

        return made;
    }

    public async Task<HttpResponseMessage> AskForATicketAsync(
        RecordingId id,
        string mediaType = "application/json")
    {
        ArgumentNullException.ThrowIfNull(id);

        using var content = new StringContent("{}", Encoding.UTF8, mediaType);

        return await Client.PostAsync(new Uri($"/api/videos/{id.Wire}/ticket", UriKind.Relative), content);
    }

    public async Task<string> TicketForAsync(RecordingId id)
    {
        using HttpResponseMessage answer = await AskForATicketAsync(id);

        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);

        using JsonDocument read = JsonDocument.Parse(await answer.Content.ReadAsStringAsync());

        return read.RootElement.GetProperty("data").GetProperty("inTheClear").GetString()!;
    }

    public async Task<HttpResponseMessage> AsAPlayerAsync(
        string path,
        string? ticket,
        string? range = null,
        HttpMethod? method = null)
    {
        using var asking = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(path, UriKind.Relative));

        if (ticket is not null)
        {
            asking.Headers.TryAddWithoutValidation(
                "Authorization",
                $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"carina:{ticket}"))}");
        }

        if (range is not null)
        {
            asking.Headers.TryAddWithoutValidation("Range", range);
        }

        return await Player.SendAsync(asking);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Player.Dispose();
        await factory.DisposeAsync();
        mounted.Delete(recursive: true);
        shelved.Delete(recursive: true);
    }
}
