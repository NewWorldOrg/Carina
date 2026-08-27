using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

using Carina.Api.Tests.Unit;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Thumbnails;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class WritingDriver : IDriverClient
{
    private readonly List<SessionSnapshot> sessions = [];

    public List<(SessionId Session, string Reason)> Stopped { get; } = [];

    public string? Unreachable { get; set; }

    public DriverProblem? RefusesToStop { get; set; }

    public WritingDriver Writing(SessionId session, string recordingId)
    {
        sessions.Add(new SessionSnapshot(
            session,
            SessionPurpose.Recording,
            "pt3-0",
            SessionState.Active,
            RecordingFeature.Noon)
        {
            RecordingId = recordingId,
        });

        return this;
    }

    public Task<DriverCall<IReadOnlyList<SessionSnapshot>>> GetActiveSessionsAsync(
        CancellationToken cancellationToken)
        => Task.FromResult(Unreachable is { } failure
            ? DriverCall<IReadOnlyList<SessionSnapshot>>.Unreachable(failure)
            : DriverCall<IReadOnlyList<SessionSnapshot>>.Reached([.. sessions]));

    public Task<DriverCall<SessionSnapshot>> StopSessionAsync(
        SessionId sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (RefusesToStop is { } problem)
        {
            return Task.FromResult(DriverCall<SessionSnapshot>.Refused(problem));
        }

        Stopped.Add((sessionId, reason));
        sessions.RemoveAll(session => session.SessionId == sessionId);

        return Task.FromResult(DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                sessionId,
                SessionPurpose.Recording,
                "pt3-0",
                SessionState.Stopped,
                RecordingFeature.Noon)));
    }

    public Task<DriverCall<DriverHello>> GetHealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(DriverCall<DriverHello>.Reached(
            new DriverHello(DriverProtocol.Version, "instance-a", [])));

    public Task<DriverCall<IReadOnlyList<TunerSnapshot>>> GetTunersAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<DetectedDeviceDto>>> GetDetectedDevicesAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerLedgerDto>> GetTunerLedgerAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerLedgerDto>> ReplaceTunerLedgerAsync(
        IReadOnlyList<TunerConfigEntry> tuners,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<DriverRestartDto>> RequestRestartAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerSnapshot>> ToggleTunerAsync(
        string deviceId,
        bool disabled,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<SessionSnapshot>> GetSessionAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<SessionSnapshot>> StartSessionAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<DiagnosticSnapshot>>> GetDiagnosticsAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<StorageRootDto>>> GetStorageAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenSessionStreamAsync(
        SessionId sessionId,
        string? subscriber,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenEventsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();
}

internal sealed class ScriptedRemaker(HeldRecordings recordings) : IThumbnailRemaker
{
    public ThumbnailRemake Answer { get; set; } = ThumbnailRemake.Drawn;

    public List<RecordingId> Asked { get; } = [];

    public Task<ThumbnailRemake> RemakeAsync(RecordingId id, CancellationToken cancellationToken)
    {
        Asked.Add(id);

        if (Answer is ThumbnailRemake.Drawn or ThumbnailRemake.Skipped or ThumbnailRemake.Failed
            && recordings.Recordings.FirstOrDefault(recording => recording.Id.Equals(id)) is { } held)
        {
            held.Illustrate(
                Answer switch
                {
                    ThumbnailRemake.Drawn => ThumbnailState.Ready,
                    ThumbnailRemake.Skipped => ThumbnailState.Skipped,
                    _ => ThumbnailState.Failed,
                },
                Answer is ThumbnailRemake.Failed ? ThumbnailFault.SourceOutOfReach : null);
        }

        return Task.FromResult(Answer);
    }
}

internal sealed class RecordingFeature : IAsyncDisposable
{
    public static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private readonly TestingWebApplicationFactory factory = new();

    public RecordingFeature()
    {
        Remaker = new ScriptedRemaker(Recordings);

        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IRecordingDirectory>(Recordings);
                services.AddSingleton<IDriverClient>(Driver);
                services.AddSingleton<IThumbnailRemaker>(Remaker);
                services.AddSingleton<TimeProvider>(new FixedTimeProvider(Noon.AddMinutes(30)));
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
    }

    public HttpClient Client { get; }

    public HeldRecordings Recordings { get; } = new();

    public WritingDriver Driver { get; } = new();

    public ScriptedRemaker Remaker { get; }

    public static Recording Begin(
        RecordingId id,
        int networkId = 32736,
        int serviceId = 1024,
        int eventId = 4001,
        DateTime? startedAt = null,
        TimeSpan? window = null,
        string name = "A programme",
        string summary = "What it is about",
        string extended = "",
        IReadOnlyList<ProgrammeGenre>? genres = null,
        string? groupKey = null,
        BroadcastGroupRole groupRole = BroadcastGroupRole.Standalone)
    {
        DateTime started = startedAt ?? Noon;

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(networkId), new ServiceId(serviceId), new EventId(eventId), started),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            started,
            started + (window ?? TimeSpan.FromHours(1)),
            new ProgrammeSnapshot(name, summary, extended, genres ?? [], started),
            groupKey is null ? null : new BroadcastGroupKey(groupKey),
            groupRole,
            started,
            new TunerDeviceId("pt3-0"));
    }

    public Recording Held(
        int networkId = 32736,
        int serviceId = 1024,
        int eventId = 4001,
        DateTime? startedAt = null,
        TimeSpan? window = null,
        string name = "A programme",
        string summary = "What it is about",
        string extended = "",
        IReadOnlyList<ProgrammeGenre>? genres = null,
        string? groupKey = null,
        BroadcastGroupRole groupRole = BroadcastGroupRole.Standalone)
    {
        Recording recording = Begin(
            RecordingId.New(),
            networkId,
            serviceId,
            eventId,
            startedAt,
            window,
            name,
            summary,
            extended,
            genres,
            groupKey,
            groupRole);

        Recordings.Recordings.Add(recording);

        return recording;
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path, object? body = null)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            body ?? new { });

        return await ReadAsync(response);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await factory.DisposeAsync();
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
