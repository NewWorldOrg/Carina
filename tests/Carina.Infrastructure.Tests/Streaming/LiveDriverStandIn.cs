using System.IO.Pipelines;

using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.Infrastructure.Tests.Streaming;

internal sealed class LiveDriverStandIn : IDriverClient
{
    private readonly Lock gate = new();

    private readonly Pipe pipe = new();

    public const string DeviceId = "adapter3";

    public List<StartSessionRequest> Started { get; } = [];

    public List<(SessionId Session, string Reason)> Stopped { get; } = [];

    public List<(SessionId Session, string? Seat)> Opened { get; } = [];

    public List<SessionId> Looked { get; } = [];

    public int HealthAsked { get; private set; }

    public DriverHello Hello { get; set; } = new(DriverProtocol.Version, "stand-in", ["recording", "live", "typedTuning"]);

    public bool Unreachable { get; set; }

    public DriverProblem? RefusingToStart { get; set; }

    public SessionState StateOnStart { get; set; } = SessionState.Active;

    public string? FailureCauseOnStart { get; set; }

    public DriverProblem? RefusingToOpen { get; set; }

    public TaskCompletionSource? BeforeOpening { get; set; }

    public Func<SessionId, DriverCall<SessionSnapshot>>? Recalled { get; set; }

    public PipeWriter Writer => pipe.Writer;

    public SessionId? Held
    {
        get
        {
            lock (gate)
            {
                return Started.Count > 0 ? Started[^1].SessionId : null;
            }
        }
    }

    public SessionSnapshot Snapshot(SessionState state, SessionStopReason reason, string? failureCause = null)
        => new(Held!.Value, SessionPurpose.Live, DeviceId, state, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(4))
        {
            StopReason = reason,
            Concluded = state is SessionState.Stopped or SessionState.Failed,
            FailureCause = failureCause,
        };

    public Task<DriverCall<DriverHello>> GetHealthAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            HealthAsked++;
        }

        return Task.FromResult(Unreachable
            ? DriverCall<DriverHello>.Unreachable("The driver's socket could not be reached.")
            : DriverCall<DriverHello>.Reached(Hello));
    }

    public Task<DriverCall<SessionSnapshot>> StartSessionAsync(StartSessionRequest request, CancellationToken cancellationToken)
    {
        if (Unreachable)
        {
            return Task.FromResult(DriverCall<SessionSnapshot>.Unreachable("The driver's socket could not be reached."));
        }

        if (RefusingToStart is { } refusal)
        {
            return Task.FromResult(DriverCall<SessionSnapshot>.Refused(refusal));
        }

        lock (gate)
        {
            Started.Add(request);
        }

        return Task.FromResult(DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(request.SessionId, request.Purpose, DeviceId, StateOnStart, DateTimeOffset.UnixEpoch)
            {
                FailureCause = FailureCauseOnStart,
            }));
    }

    public async Task<DriverCall<Stream>> OpenSessionStreamAsync(SessionId sessionId, string? subscriber, CancellationToken cancellationToken)
    {
        if (BeforeOpening is { } held)
        {
            await held.Task.WaitAsync(cancellationToken);
        }

        lock (gate)
        {
            Opened.Add((sessionId, subscriber));
        }

        return RefusingToOpen is { } refusal
            ? DriverCall<Stream>.Refused(refusal)
            : DriverCall<Stream>.Reached(pipe.Reader.AsStream());
    }

    public Task<DriverCall<SessionSnapshot>> StopSessionAsync(SessionId sessionId, string reason, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Stopped.Add((sessionId, reason));
        }

        return Task.FromResult(DriverCall<SessionSnapshot>.Reached(null));
    }

    public Task<DriverCall<SessionSnapshot>> GetSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Looked.Add(sessionId);
        }

        if (Unreachable)
        {
            return Task.FromResult(DriverCall<SessionSnapshot>.Unreachable("The driver's socket could not be reached."));
        }

        return Task.FromResult(Recalled is { } recalled
            ? recalled(sessionId)
            : DriverCall<SessionSnapshot>.Reached(Snapshot(SessionState.Active, SessionStopReason.Running)));
    }

    public Task<DriverCall<IReadOnlyList<TunerSnapshot>>> GetTunersAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<DetectedDeviceDto>>> GetDetectedDevicesAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerLedgerDto>> GetTunerLedgerAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerLedgerDto>> ReplaceTunerLedgerAsync(IReadOnlyList<TunerConfigEntry> tuners, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<DriverRestartDto>> RequestRestartAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerSnapshot>> ToggleTunerAsync(string deviceId, bool disabled, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<SessionSnapshot>>> GetActiveSessionsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<DiagnosticSnapshot>>> GetDiagnosticsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<StorageRootDto>>> GetStorageAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<RecordingErasedDto>> EraseRecordingAsync(string recordingId, string outputRoot, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenEventsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();
}
