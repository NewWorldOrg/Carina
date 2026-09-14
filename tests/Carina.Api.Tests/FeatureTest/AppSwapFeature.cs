using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Carina.Api.Tests.FeatureTest;

internal sealed class SwappedReservations : IReservationRecordingContract
{
    private readonly Lock gate = new();
    private readonly List<RecordingTick> due = [];

    public void Add(RecordingTick tick)
    {
        lock (gate)
        {
            due.Add(tick);
        }
    }

    public Task<IReadOnlyList<RecordingTick>> DueAtAsync(DateTime at, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<RecordingTick>>([.. due]);
        }
    }

    public Task<bool> ClaimAsync(ReservationId id, DateTime at, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            int index = due.FindIndex(tick => tick.Id.Equals(id));

            if (index < 0 || due[index].StartedAt is not null)
            {
                return Task.FromResult(false);
            }

            due[index] = due[index] with { StartedAt = at };

            return Task.FromResult(true);
        }
    }

    public Task<bool> ReleaseAsync(ReservationId id, DateTime claimedAt, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            int index = due.FindIndex(tick => tick.Id.Equals(id));

            if (index >= 0)
            {
                due[index] = due[index] with { StartedAt = null };
            }

            return Task.FromResult(true);
        }
    }
}

internal sealed class SwappedRecordings : IRecordingRepository
{
    private readonly Lock gate = new();
    private readonly List<Recording> rows = [];

    public IReadOnlyList<Recording> Rows
    {
        get
        {
            lock (gate)
            {
                return [.. rows];
            }
        }
    }

    public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult(rows.FirstOrDefault(row => row.Id.Equals(id)));
        }
    }

    public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<Recording>>(
                [.. rows.Where(row => row.IsInFlight).OrderBy(row => row.ExpectedWindowEnd)]);
        }
    }

    public Task<IReadOnlyList<Recording>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<Recording>>(
                [.. rows.Where(row => reservationId.Equals(row.ReservationId))]);
        }
    }

    public Task AddAsync(Recording recording, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            rows.Add(recording);
        }

        return Task.CompletedTask;
    }

    public Task SaveAsync(Recording recording, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class RunningApp : IAsyncDisposable
{
    private static readonly Uri StatusPath = new("/api/driver/status", UriKind.Relative);

    private readonly TestingWebApplicationFactory factory;
    private readonly WebApplicationFactory<Program> configured;

    public RunningApp(
        TestingWebApplicationFactory factory,
        WebApplicationFactory<Program> configured,
        RecordingResyncHook readoptions)
    {
        this.factory = factory;
        this.configured = configured;
        Readoptions = readoptions;
        Client = configured.CreateClient();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            TestAuthenticationHandler.SchemeName,
            "anything");
        Services = configured.Services;
        Lifetime = Services.GetRequiredService<IHostApplicationLifetime>();
    }

    public HttpClient Client { get; }

    public IServiceProvider Services { get; }

    public RecordingResyncHook Readoptions { get; }

    public IHostApplicationLifetime Lifetime { get; }

    public async Task<RecordingRun> TickAsync()
    {
        await using AsyncServiceScope scope = Services
            .GetRequiredService<IServiceScopeFactory>()
            .CreateAsyncScope();

        return await scope.ServiceProvider
            .GetRequiredService<RecordingRound>()
            .RunAsync(CancellationToken.None);
    }

    public async Task<IReadOnlyList<SessionSnapshot>> SessionsAsync()
    {
        DriverCall<IReadOnlyList<SessionSnapshot>> answer = await Services
            .GetRequiredService<IDriverClient>()
            .GetActiveSessionsAsync(CancellationToken.None);

        Assert.True(
            answer.TryGetValue(out IReadOnlyList<SessionSnapshot>? held),
            $"The app could not read the driver's sessions: {answer.Outcome} {answer.Failure} {answer.Problem?.Title}.");

        return held!;
    }

    public Task UntilConnectedAsync()
        => Eventually.Yields(
            StatusAsync,
            data => data.GetProperty("connection").GetString() == "connected",
            data => data.GetProperty("connection").GetString() ?? "nothing",
            "the app reports the driver as connected");

    public Task UntilReadoptions(int count)
        => Eventually.Happens(
            () => Readoptions.CallCount == count,
            $"the app has readopted the driver's sessions {count} time(s)");

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        Lifetime.StopApplication();

        await configured.DisposeAsync();
        await factory.DisposeAsync();
    }

    private async Task<JsonElement> StatusAsync()
    {
        using HttpResponseMessage response = await Client.GetAsync(StatusPath);
        string payload = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(payload);

        return body.RootElement.GetProperty("data").Clone();
    }
}

[SupportedOSPlatform("linux")]
internal sealed class AppSwapFeature : IAsyncDisposable
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private static readonly DriverSupervisionSettings Impatient = new(
        TimeSpan.FromMilliseconds(20),
        TimeSpan.FromMilliseconds(200),
        [DriverCapabilities.Recording, DriverCapabilities.Live],
        () => 1.0);

    private readonly SyntheticDriverHost driver;

    private RunningApp? running;

    private AppSwapFeature(SyntheticDriverHost driver, DateTimeOffset from)
    {
        this.driver = driver;
        Clock = new HandTurnedClock(from);
    }

    public HandTurnedClock Clock { get; }

    public SwappedReservations Reservations { get; } = new();

    public SwappedRecordings Recordings { get; } = new();

    public HeldProgrammes Programmes { get; } = new();

    public ResolvedTuning Tuning { get; } = new(TuningResolution.Tunable(
        new CandidateChannelId(Guid.NewGuid()),
        TuningParameters.Terrestrial(27),
        impaired: false));

    public SilentEvents Events { get; } = new();

    public CountedNotices Notices { get; } = new();

    public string RecordingsDirectory => driver.RecordingsDirectory;

    public RunningApp App => running
        ?? throw new InvalidOperationException("No app is running against the driver.");

    public static async Task<AppSwapFeature> StartAsync()
    {
        SyntheticDriverHost driver = await SyntheticDriverHost.StartAsync();
        var feature = new AppSwapFeature(driver, DateTimeOffset.UtcNow);

        feature.Reservations.Add(feature.Due());

        await feature.StartAppAsync();

        return feature;
    }

    public async Task StartAppAsync()
    {
        await StopAppAsync();

        var factory = new TestingWebApplicationFactory { DriverSocketPath = driver.SocketPath };
        var readoptions = new RecordingResyncHook();
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddHostedService<DriverConnectionSupervisor>();
                services.AddSingleton<IDriverSessionResyncHook>(readoptions);
                services.AddSingleton(Impatient);
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IReservationRecordingContract>(Reservations);
                services.AddSingleton<IRecordingRepository>(Recordings);
                services.AddSingleton<IAnnouncedProgrammes>(Programmes);
                services.AddSingleton<IServiceTuningDirectory>(Tuning);
                services.AddSingleton<IAppEventPublisher>(Events);
                services.AddSingleton<IRecalculationNotice>(Notices);
                services.AddSingleton(new RecordingSettings(
                    RecordingSettings.NoticingItIsDue,
                    RecordingSettings.NoticingItIsDue,
                    RecordingSettings.LongestWayToTheFirstByte,
                    new OutputRoot(SyntheticDriverHost.RootName)));
            }))
            .WithTestScheme();

        running = new RunningApp(factory, configured, readoptions);

        await running.UntilConnectedAsync();
    }

    public async Task StopAppAsync()
    {
        if (running is { } going)
        {
            running = null;

            await going.DisposeAsync();
        }
    }

    public string FileOf(RecordingId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return Path.Combine(RecordingsDirectory, RecordingFile.Of(id.Wire));
    }

    public IReadOnlyList<string> FilesWritten()
        => [.. new DirectoryInfo(RecordingsDirectory).EnumerateFiles().Select(file => file.Name).Order(StringComparer.Ordinal)];

    public Task UntilTheFileGrowsPast(string path, long size)
        => Eventually.Happens(
            () => File.Exists(path) && new FileInfo(path).Length > size,
            $"the recording file grows past {size} bytes");

    public Task UntilTheRecordingSessionIsStopped()
        => Eventually.Yields(
            App.SessionsAsync,
            held => held.All(one => one.State is SessionState.Stopped),
            held => string.Join(", ", held.Select(one => one.State.ToString())),
            "the driver has stopped the session the recording was written on");

    public async ValueTask DisposeAsync()
    {
        await StopAppAsync();
        await driver.DisposeAsync();
    }

    private RecordingTick Due()
    {
        DateTime airs = Clock.GetUtcNow().UtcDateTime;

        return new RecordingTick(
            ReservationId.New(),
            new NetworkId(32736),
            new ServiceId(1024),
            new EventId(4001),
            airs,
            new ProgrammeSnapshot(
                "A programme carried across a deployment",
                "What it is about",
                string.Empty,
                [],
                airs,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            Priority.Default,
            null,
            BroadcastGroupRole.Standalone,
            airs,
            airs + Window,
            true,
            null);
    }
}
