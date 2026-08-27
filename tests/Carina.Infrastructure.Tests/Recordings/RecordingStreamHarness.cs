using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Recordings;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using EventId = Carina.Domain.Programmes.EventId;

namespace Carina.Infrastructure.Tests.Recordings;

internal sealed class StreamLedger : IRecordingRepository
{
    private readonly Dictionary<Guid, Recording> rows = [];

    private int listings;

    public int Listings => Volatile.Read(ref listings);

    public List<RecordingId> Saved { get; } = [];

    public int Collisions { get; set; }

    public Exception? RefusingToList { get; set; }

    public Action? AfterListing { get; set; }

    public void Hold(params Recording[] recordings)
    {
        foreach (Recording recording in recordings)
        {
            rows[recording.Id.Value] = recording;
        }
    }

    public Recording Read(RecordingId id) => LedgerCopy.Of(rows[id.Value]);

    public Task<Recording?> FindAsync(RecordingId id, CancellationToken cancellationToken)
        => Task.FromResult(rows.TryGetValue(id.Value, out Recording? row) ? LedgerCopy.Of(row) : null);

    public Task<IReadOnlyList<Recording>> ListInFlightAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref listings);

        if (RefusingToList is { } refusal)
        {
            return Task.FromException<IReadOnlyList<Recording>>(refusal);
        }

        IReadOnlyList<Recording> listed =
        [
            .. rows.Values
                .Where(row => row.IsInFlight)
                .OrderBy(row => row.ExpectedWindowEnd)
                .Select(LedgerCopy.Of),
        ];

        AfterListing?.Invoke();

        return Task.FromResult(listed);
    }

    public Task<IReadOnlyList<Recording>> ListForReservationAsync(
        ReservationId reservationId,
        CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<Recording>>(
            [.. rows.Values.Where(row => reservationId.Equals(row.ReservationId)).Select(LedgerCopy.Of)]);

    public Task AddAsync(Recording recording, CancellationToken cancellationToken)
    {
        Hold(recording);

        return Task.CompletedTask;
    }

    public Task SaveAsync(Recording recording, CancellationToken cancellationToken)
    {
        Saved.Add(recording.Id);

        if (Collisions > 0)
        {
            Collisions--;

            return Task.FromException(
                new DbUpdateConcurrencyException("Something else moved this row between the read and the write."));
        }

        rows[recording.Id.Value] = recording;

        return Task.CompletedTask;
    }
}

internal static class LedgerCopy
{
    public static Recording Of(Recording recording)
        => Recording.Rehydrate(
            recording.Id,
            recording.ReservationId,
            recording.Programme,
            recording.OutputRoot,
            recording.FileName,
            recording.FileSizeObserved,
            recording.ObservedAt,
            recording.StartedAtActual,
            recording.StoppedAtActual,
            recording.AbortedAt,
            recording.WrittenDurationMs,
            recording.ResumeCount,
            recording.Interruptions,
            recording.ExpectedWindowStart,
            recording.ExpectedWindowEnd,
            recording.Outcome,
            recording.OutcomeDetail,
            recording.Counters,
            recording.Positions,
            recording.ScrambledPackets,
            recording.EovfCount,
            recording.MeasuredUpdatedAt,
            recording.TunerDeviceId,
            recording.ThumbnailState,
            new ProgrammeSnapshot(
                recording.SnapshotName,
                recording.SnapshotSummary,
                recording.SnapshotExtended,
                recording.SnapshotGenres,
                recording.CapturedAt),
            recording.BroadcastGroupKey,
            recording.BroadcastGroupRole,
            recording.ThumbnailFault);
}

internal sealed class WatchedDriver : IDriverClient
{
    public Dictionary<SessionId, DriverCall<SessionSnapshot>> Holding { get; } = [];

    public DriverCall<SessionSnapshot> WhenAsked { get; set; } =
        DriverCall<SessionSnapshot>.Refused(new DriverProblem("noSuchSession", []));

    public DriverCall<SessionSnapshot>? WhenStarted { get; set; }

    public Exception? ThrowingWhenAsked { get; set; }

    public List<SessionId> Asked { get; } = [];

    public List<StartSessionRequest> Started { get; } = [];

    public List<SessionId> Stopped { get; } = [];

    public List<string> StopReasons { get; } = [];

    public Task<DriverCall<SessionSnapshot>> GetSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        Asked.Add(sessionId);

        if (ThrowingWhenAsked is { } thrown)
        {
            return Task.FromException<DriverCall<SessionSnapshot>>(thrown);
        }

        return Task.FromResult(Holding.TryGetValue(sessionId, out DriverCall<SessionSnapshot>? held)
            ? held
            : WhenAsked);
    }

    public Task<DriverCall<SessionSnapshot>> StartSessionAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken)
    {
        Started.Add(request);

        return Task.FromResult(WhenStarted ?? DriverCall<SessionSnapshot>.Refused(
            new DriverProblem("noDeviceFree", [])));
    }

    public Task<DriverCall<DriverHello>> GetHealthAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

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

    public Task<DriverCall<SessionSnapshot>> StopSessionAsync(
        SessionId sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        Stopped.Add(sessionId);
        StopReasons.Add(reason);

        return Task.FromResult(DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                sessionId,
                SessionPurpose.Recording,
                "adapter1",
                SessionState.Stopped,
                RecordingStreamFixture.Airs)
            {
                Concluded = true,
            }));
    }

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

internal sealed class HeldStatus(DriverObservation observation) : IDriverStatusReader
{
    public DriverObservation Observation { get; set; } = observation;

    public Task<DriverObservation> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(Observation);
}

internal sealed class WeighedFiles : IRecordingFileWeigher
{
    public long? Weighs { get; set; }

    public List<string> Read { get; } = [];

    public Task<long?> WeighAsync(OutputRoot root, RecordingFileName fileName, CancellationToken cancellationToken)
    {
        Read.Add($"{root.Value}/{fileName.Value}");

        return Task.FromResult(Weighs);
    }
}

internal sealed class WatchClock(DateTime now) : TimeProvider
{
    private readonly List<TimeSpan> waits = [];

    public DateTime Now { get; set; } = now;

    public IReadOnlyList<TimeSpan> Waits
    {
        get
        {
            lock (waits)
            {
                return [.. waits];
            }
        }
    }

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (waits)
        {
            waits.Add(dueTime);
        }

        return base.CreateTimer(callback, state, TimeSpan.FromMilliseconds(1), period);
    }
}

internal sealed class StillClock(DateTime now) : TimeProvider
{
    private int armed;

    public int Armed => Volatile.Read(ref armed);

    public override DateTimeOffset GetUtcNow() => now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        Interlocked.Increment(ref armed);

        return base.CreateTimer(callback, state, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }
}

internal sealed class WhatTheWatchSaid
{
    private readonly List<string> lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (lines)
            {
                return [.. lines];
            }
        }
    }

    public ILogger<RecordingStreamSupervisor> Logger() => new Listening(this);

    private void Heard(string line)
    {
        lock (lines)
        {
            lines.Add(line);
        }
    }

    private sealed class Listening(WhatTheWatchSaid said) : ILogger<RecordingStreamSupervisor>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            Microsoft.Extensions.Logging.EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => said.Heard($"{logLevel}: {formatter(state, exception)}");
    }
}

internal static class RecordingStreamFixture
{
    public static readonly DateTime Airs = new(2026, 8, 26, 20, 0, 0, DateTimeKind.Utc);

    public static readonly RecordingWatchSettings Settings = new(
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(10),
        5,
        TimeSpan.FromSeconds(2),
        3);

    public static readonly TuningResolution Terrestrial = TuningResolution.Tunable(
        new CandidateChannelId(Guid.NewGuid()),
        TuningParameters.Terrestrial(27),
        impaired: false);

    public static readonly TuningResolution Satellite = TuningResolution.Tunable(
        new CandidateChannelId(Guid.NewGuid()),
        TuningParameters.Bs(1, new TransportStreamId(16400)),
        impaired: false);

    public static DriverHello Greeting(params string[] capabilities)
        => new(
            DriverProtocol.Version,
            "driver-1",
            capabilities.Length is 0
                ?
                [
                    DriverCapabilities.Recording,
                    DriverCapabilities.CcMeasurement,
                    DriverCapabilities.ScrambleMeasurement,
                    DriverCapabilities.DropPositions,
                ]
                : capabilities);

    public static DriverObservation Connected(params string[] capabilities)
        => DriverObservation.Of(Greeting(capabilities), []);

    public static Recording InFlight(
        DateTime? from = null,
        DateTime? until = null,
        string? deviceId = "adapter1",
        int eventId = 9)
    {
        RecordingId id = RecordingId.New();
        DateTime start = from ?? Airs;

        return Recording.Begin(
            id,
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1025), new EventId(eventId), Airs),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            start,
            until ?? Airs.AddMinutes(30),
            new ProgrammeSnapshot("Another programme", string.Empty, string.Empty, [], Airs.AddHours(-6)),
            null,
            BroadcastGroupRole.Standalone,
            start,
            deviceId is null ? null : new TunerDeviceId(deviceId));
    }

    public static DriverCall<SessionSnapshot> Live(
        Recording recording,
        DateTime openedAt,
        SessionCounters? counters = null,
        string deviceId = "adapter1")
        => DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                RecordingSessions.Named(recording.Id),
                SessionPurpose.Recording,
                deviceId,
                SessionState.Active,
                openedAt)
            {
                RecordingId = recording.Id.Wire,
                OutputRoot = recording.OutputRoot.Value,
                Counters = counters ?? SessionCounters.Nothing,
            });

    public static DriverCall<SessionSnapshot> In(
        Recording recording,
        SessionState state,
        DateTime openedAt,
        SessionCounters? counters = null)
        => DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                RecordingSessions.Named(recording.Id),
                SessionPurpose.Recording,
                "adapter1",
                state,
                openedAt)
            {
                RecordingId = recording.Id.Wire,
                Counters = counters ?? SessionCounters.Nothing,
            });

    public static DriverCall<SessionSnapshot> Over(
        Recording recording,
        SessionStopReason reason = SessionStopReason.EndTimeReached)
        => DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                RecordingSessions.Named(recording.Id),
                SessionPurpose.Recording,
                "adapter1",
                SessionState.Stopped,
                Airs)
            {
                RecordingId = recording.Id.Wire,
                Concluded = true,
                StopReason = reason,
            });

    public static RecordingStreamSupervisor Supervisor(
        StreamLedger ledger,
        WatchedDriver driver,
        TimeProvider clock,
        WeighedFiles? files = null,
        HeldStatus? status = null,
        TuningResolution? tuning = null,
        RecordingWatchSettings? settings = null,
        ILogger<RecordingStreamSupervisor>? logger = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IRecordingRepository>(_ => ledger);
        services.AddScoped<IServiceTuningDirectory>(_ => new ResolvedTuning(tuning ?? Terrestrial));

        return new RecordingStreamSupervisor(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            driver,
            status ?? new HeldStatus(Connected()),
            files ?? new WeighedFiles { Weighs = 0 },
            settings ?? Settings,
            clock,
            logger ?? NullLogger<RecordingStreamSupervisor>.Instance);
    }
}
