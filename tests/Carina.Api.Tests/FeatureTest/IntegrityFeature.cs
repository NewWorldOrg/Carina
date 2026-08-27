using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Integrity;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Integrity;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class MovingClock(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => new(Now, TimeSpan.Zero);
}

internal sealed class HeldLedgerFiles : IRecordingLedger
{
    public List<LedgerFile> Rows { get; } = [];

    public Exception? Throws { get; set; }

    public int Reads { get; private set; }

    public TaskCompletionSource? Gate { get; set; }

    public async Task<IReadOnlyList<LedgerFile>> ListAsync(CancellationToken cancellationToken)
    {
        Reads++;

        if (Gate is { } waiting)
        {
            await waiting.Task.WaitAsync(cancellationToken);
        }

        return Throws is { } refusal ? throw refusal : [.. Rows];
    }
}

internal sealed class HeldIntegrityChecks : IIntegrityCheckRepository
{
    public List<IntegrityReport> Saved { get; } = [];

    public Task SaveAsync(IntegrityReport report, CancellationToken cancellationToken)
    {
        Saved.Add(report);

        return Task.CompletedTask;
    }

    public Task<IntegrityCheck?> LatestAsync(CancellationToken cancellationToken)
        => Task.FromResult(Saved.Count is 0 ? null : Saved[^1].Check);

    public Task<PaginatedList<IntegrityFinding>> ListFindingsAsync(
        IntegrityCheckId checkId,
        IntegrityFindingQuery query,
        CancellationToken cancellationToken)
    {
        IntegrityFinding[] found =
        [
            .. Saved
                .Where(report => report.Check.Id.Equals(checkId))
                .SelectMany(report => report.Findings)
                .OrderBy(finding => finding.Root.Value, StringComparer.Ordinal)
                .ThenBy(finding => finding.Path, StringComparer.Ordinal),
        ];

        return Task.FromResult(new PaginatedList<IntegrityFinding>(
            [.. found.Skip((query.Page - 1) * query.PerPage).Take(query.PerPage)],
            found.Length,
            query.Page,
            query.PerPage));
    }
}

internal sealed class HeldInFlightRecordings : IRecordingRepository
{
    public List<Recording> InFlight { get; } = [];

    public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>([.. InFlight]);

    public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<IReadOnlyList<Recording>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task AddAsync(Recording recording, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task SaveAsync(Recording recording, CancellationToken cancellationToken)
        => throw new NotSupportedException();
}

internal sealed class StorageDriver : IDriverClient
{
    public List<StorageRootDto> Roots { get; } = [];

    public string? Unreachable { get; set; }

    public DriverProblem? Refuses { get; set; }

    public int Reads { get; private set; }

    public Task<DriverCall<IReadOnlyList<StorageRootDto>>> GetStorageAsync(CancellationToken cancellationToken)
    {
        Reads++;

        if (Unreachable is { } failure)
        {
            return Task.FromResult(DriverCall<IReadOnlyList<StorageRootDto>>.Unreachable(failure));
        }

        return Task.FromResult(Refuses is { } problem
            ? DriverCall<IReadOnlyList<StorageRootDto>>.Refused(problem)
            : DriverCall<IReadOnlyList<StorageRootDto>>.Reached([.. Roots]));
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

    public Task<DriverCall<IReadOnlyList<SessionSnapshot>>> GetActiveSessionsAsync(
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

    public Task<DriverCall<SessionSnapshot>> StopSessionAsync(
        SessionId sessionId,
        string reason,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<DiagnosticSnapshot>>> GetDiagnosticsAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenSessionStreamAsync(
        SessionId sessionId,
        string? subscriber,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenEventsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();
}

internal sealed class RecordingStore : IDisposable
{
    private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-integrity-feature-");

    public string Root => directory.FullName;

    public RecordingStore Holding(string name, int sizeBytes)
    {
        string full = Path.Combine(Root, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);

        byte[] bytes = new byte[sizeBytes];

        for (int index = 0; index < sizeBytes; index++)
        {
            bytes[index] = (byte)(name[index % name.Length] + index);
        }

        File.WriteAllBytes(full, bytes);

        return this;
    }

    public IReadOnlyList<string> Fingerprint()
        => [.. Directory
            .EnumerateFileSystemEntries(Root, "*", SearchOption.AllDirectories)
            .Select(Describe)
            .Order(StringComparer.Ordinal)];

    public void Dispose() => directory.Delete(recursive: true);

    private string Describe(string entry)
    {
        string relative = Path.GetRelativePath(Root, entry).Replace('\\', '/');

        if (Directory.Exists(entry))
        {
            return $"dir {relative}";
        }

        byte[] bytes = File.ReadAllBytes(entry);

        return $"file {relative} {bytes.Length} {Convert.ToHexString(SHA256.HashData(bytes))}";
    }
}

internal sealed class IntegrityFeature : IAsyncDisposable
{
    public static readonly DateTime Noon = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    public static readonly OutputRoot Primary = new("primary");

    public static readonly OutputRoot Bulk = new("bulk");

    private readonly TestingWebApplicationFactory factory = new();

    public IntegrityFeature(IntegritySettings? settings = null, string? walking = null)
    {
        Settings = settings ?? new IntegritySettings
        {
            OutputRoots = walking is null ? [] : [new StorageRootPath(Primary, walking)],
        };

        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton(Settings);
                services.AddSingleton<IDriverClient>(Driver);
                services.AddSingleton<IRecordingRepository>(Running);
                services.AddSingleton<IRecordingFileSurvey>(
                    new LocalRecordingFileSurvey(Settings, NullLogger<LocalRecordingFileSurvey>.Instance));
                services.AddScoped<IRecordingLedger>(_ => Ledger);
                services.AddScoped<IIntegrityCheckRepository>(_ => Checks);
            }));

        Client = configured.WithTestScheme().CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
    }

    public HttpClient Client { get; }

    public MovingClock Clock { get; } = new(Noon);

    public IntegritySettings Settings { get; }

    public HeldLedgerFiles Ledger { get; } = new();

    public HeldIntegrityChecks Checks { get; } = new();

    public HeldInFlightRecordings Running { get; } = new();

    public StorageDriver Driver { get; } = new();

    public static Recording Writing(OutputRoot root, DateTime start, DateTime end, int eventId = 4001)
    {
        var id = RecordingId.New();

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), start),
            root,
            RecordingFileName.For(id, ".m2ts"),
            start,
            end,
            new ProgrammeSnapshot("A programme", "What it is about", string.Empty, [], start),
            null,
            BroadcastGroupRole.Standalone,
            start,
            new TunerDeviceId("pt3-0"));
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> GetAsync(string path)
    {
        using HttpResponseMessage response = await Client.GetAsync(new Uri(path, UriKind.Relative));

        return await ReadAsync(response);
    }

    public async Task<(HttpStatusCode Status, JsonElement Body)> PostAsync(string path)
    {
        using HttpResponseMessage response = await Client.PostAsJsonAsync(
            new Uri(path, UriKind.Relative),
            new { });

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
