using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Encodings;
using Carina.Domain.Integrity;
using Carina.Domain.Machines;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class ScriptedStrays : IStrayProgrammes
{
    public List<RunningProgramme> Stopped { get; } = [];

    public StrayFate Answer { get; set; } = StrayFate.Stopped;

    public StrayFate Stop(RunningProgramme written)
    {
        Stopped.Add(written);

        return Answer;
    }
}

internal sealed class EncodingFeature : IAsyncDisposable
{
    public static readonly DateTime Noon = new(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

    public static readonly OutputRoot Primary = new("primary");

    public static readonly OutputRoot Encodes = new("encodes");

    private readonly TestingWebApplicationFactory factory = new();

    private readonly RecordingStore shelf = new();

    public EncodingFeature()
    {
        Settings = new EncodeSettings { OutputRoots = [new StorageRootPath(Encodes, shelf.Root)] };
        Driver.Roots.Add(new StorageRootDto { Name = Primary.Value, FreeBytes = 900, TotalBytes = 1_000, Writable = true });

        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton(Settings);
                services.AddSingleton<IDriverClient>(Driver);
                services.AddSingleton<IRecordingDirectory>(Recordings);
                services.AddSingleton<IEncodeJobRepository>(Jobs);
                services.AddSingleton<IEncodeProfileRepository>(Profiles);
                services.AddSingleton<IEncodeDestinationRepository>(Destinations);
                services.AddSingleton<IEncodeScratchLedger>(Scratch);
                services.AddSingleton<IStrayProgrammes>(Strays);
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName, "anything");
    }

    public HttpClient Client { get; }

    public MovingClock Clock { get; } = new(Noon);

    public EncodeSettings Settings { get; }

    public StorageDriver Driver { get; } = new();

    public HeldRecordings Recordings { get; } = new();

    public HeldEncodeJobs Jobs { get; } = new();

    public HeldEncodeProfiles Profiles { get; } = new();

    public HeldEncodeDestinations Destinations { get; } = new();

    public HeldEncodeScratch Scratch { get; } = new();

    public ScriptedStrays Strays { get; } = new();

    public string Shelf => shelf.Root;

    public EncodeProfile Defined(string label = "Viewing")
    {
        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel(label),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            Noon.AddHours(-1));
        Profiles.Profiles.Add(profile);

        return profile;
    }

    public EncodeDestination Placed(EncodeProfile profile, OutputRoot? root = null)
    {
        EncodeDestination destination = EncodeDestination.Define(
            EncodeDestinationId.New(),
            new EncodeLabel("Shelf"),
            root ?? Encodes,
            profile.Id,
            Noon.AddHours(-1));
        Destinations.Destinations.Add(destination);

        return destination;
    }

    public Recording Recorded(RecordingOutcome? outcome = RecordingOutcome.Complete)
    {
        var id = RecordingId.New();
        DateTime started = Noon.AddHours(-3);
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), started),
            Primary,
            RecordingFileName.For(id, ".ts"),
            started,
            started.AddHours(1),
            new ProgrammeSnapshot("A programme", "What it is about", string.Empty, [], started),
            null,
            BroadcastGroupRole.Standalone,
            started,
            new TunerDeviceId("pt3-0"));

        if (outcome is { } ended)
        {
            recording.Wrote(TimeSpan.FromHours(1));
            recording.Abort(started.AddHours(1));

            if (ended is RecordingOutcome.Failed)
            {
                recording.Note(new OutcomeDetail(RecordingFault.DriverLost, null, "the driver went away", started.AddMinutes(30)));
            }

            recording.Settle(ended, ended is RecordingOutcome.Failed ? 0 : 1_000_000, started.AddHours(1));
        }

        Recordings.Recordings.Add(recording);

        return recording;
    }

    public EncodeJob Queued(Recording recording, EncodeProfile profile, EncodeDestination destination)
    {
        EncodeJob job = EncodeJob.Queue(EncodeJobId.New(), recording.Id, profile.Id, destination.Id, destination.OutputRoot, Noon.AddMinutes(-30));
        Jobs.Jobs.Add(job);

        return job;
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object? body = null)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(new Uri(path, UriKind.Relative), body ?? new { });

        return await ReadAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
        shelf.Dispose();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();

        if (!body.StartsWith('{') && !body.StartsWith('['))
        {
            return (response.StatusCode, default);
        }

        using var document = JsonDocument.Parse(body);

        return (response.StatusCode, document.RootElement.Clone());
    }
}
