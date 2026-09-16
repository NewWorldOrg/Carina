using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Text.Json;

using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Events;
using Carina.Domain.Integrity;
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

    public Task UntilConnectedAsync() => UntilConnectionIs("connected");

    public Task UntilConnectionIs(string connection)
        => Eventually.Yields(
            StatusAsync,
            data => ConnectionOf(data) == connection,
            ConnectionOf,
            $"the app reports the driver as {connection}");

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

    private static string ConnectionOf(JsonElement data) => data.GetProperty("connection").GetString() ?? "nothing";

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

    /// <summary>
    /// Where this side may read the disk the driver writes to, for the tests that turn on weighing
    /// what a recording actually left behind. Left unset, nothing tells the app where the output
    /// root is mounted and a file can only be reported as one that could not be weighed.
    /// </summary>
    private IntegritySettings? weighing;

    /// <summary>
    /// The pace the watch keeps, for the tests that cannot afford the pauses the default keeps: a
    /// pause is served by the hand-turned clock, which only rings when a test turns it.
    /// </summary>
    private RecordingWatchSettings? watching;

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

    public HeldOutcomeLedger Outcomes { get; } = new();

    public SyntheticDriverHost Driver => driver;

    public string RecordingsDirectory => driver.RecordingsDirectory;

    public RunningApp App => running
        ?? throw new InvalidOperationException("No app is running against the driver.");

    public static async Task<AppSwapFeature> StartAsync(
        bool takingRecordingsBack = false,
        TimeSpan? window = null,
        Action<IServiceCollection>? reshapeDriver = null,
        bool weighingWhatIsOnTheDisk = false,
        RecordingWatchSettings? watching = null)
    {
        SyntheticDriverHost driver = await SyntheticDriverHost.StartAsync(reshapeDriver);
        var feature = new AppSwapFeature(driver, DateTimeOffset.UtcNow);

        if (weighingWhatIsOnTheDisk)
        {
            feature.weighing = new IntegritySettings
            {
                OutputRoots =
                [
                    new StorageRootPath(
                        new OutputRoot(SyntheticDriverHost.RootName),
                        driver.RecordingsDirectory),
                ],
            };
        }

        feature.watching = watching;

        await feature.StartAppAsync(takingRecordingsBack);

        feature.Reservations.Add(feature.DueFromNow(window ?? Window));

        return feature;
    }

    public Task RaiseAnotherDriverAsync() => driver.RaiseAnotherDriverAsync();

    public async Task StartAppAsync(bool takingRecordingsBack = false)
    {
        await StopAppAsync();

        var factory = new TestingWebApplicationFactory { DriverSocketPath = driver.SocketPath };
        var readoptions = new RecordingResyncHook();
        WebApplicationFactory<Program> configured = factory
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.AddSingleton<IHostedService>(WatchingTheDriver);

                if (!takingRecordingsBack)
                {
                    services.AddSingleton<IDriverSessionResyncHook>(readoptions);
                }

                services.AddSingleton(Impatient);

                if (weighing is { } mounts)
                {
                    services.AddSingleton(mounts);
                }

                if (watching is { } pace)
                {
                    services.AddSingleton(pace);
                }

                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton<IReservationRecordingContract>(Reservations);
                services.AddSingleton<IReservationOutcomeRepository>(Outcomes);
                services.AddSingleton<IRecordingRepository>(Recordings);
                services.AddSingleton<IAnnouncedProgrammes>(Programmes);
                services.AddSingleton<IServiceTuningDirectory>(Tuning);
                services.AddSingleton<IAppEventPublisher>(Events);
                services.AddSingleton<IRecalculationNotice>(Notices);
                services.AddSingleton(new RecordingSettings(
                    RecordingSettings.NoticingItIsDue,
                    RecordingSettings.NoticingItIsDue,
                    RecordingSettings.LongestWayToTheFirstByte,
                    new OutputRoot(SyntheticDriverHost.RootName),
                    RecordingSettings.HoldingAnUnannouncedEnd));
            }))
            .WithTestScheme();

        running = new RunningApp(factory, configured, readoptions);

        await running.UntilConnectedAsync();
    }

    /// <summary>
    /// The supervisor's own retry cadence keeps the real clock, while everything the application
    /// reasons about time with is hand turned. Its pauses are a retry interval rather than
    /// something a test means to hold still: parked on a clock nothing turns, the app gets one
    /// look at a driver that went away, and a driver that answered that one look with a refusal
    /// rather than with silence — which is what a server part way through stopping answers — is
    /// reported as connected for the rest of the test.
    /// </summary>
    private static IHostedService WatchingTheDriver(IServiceProvider provider)
        => ActivatorUtilities.CreateInstance<DriverConnectionSupervisor>(provider, TimeProvider.System);

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

    /// <summary>
    /// The programme airs from the moment it is added, on a clock brought up to the real one first,
    /// because the driver ends a recording on its own clock at the end it was started with.
    /// </summary>
    private RecordingTick DueFromNow(TimeSpan window)
    {
        TimeSpan behind = DateTimeOffset.UtcNow - Clock.GetUtcNow();

        if (behind > TimeSpan.Zero)
        {
            Clock.Turn(behind);
        }

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
            airs + window,
            true,
            TimeSpan.Zero,
            null);
    }
}
