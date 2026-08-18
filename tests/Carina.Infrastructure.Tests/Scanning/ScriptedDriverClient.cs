using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed record ChannelScript
{
    public byte[]? Bytes { get; init; }

    public Func<PacedStream>? Paced { get; init; }

    public SignalLock Lock { get; init; } = SignalLock.Locked;

    public bool Measured { get; init; } = true;

    public int? CnrMilliDecibels { get; init; } = 21_500;

    public SessionState State { get; init; } = SessionState.Active;

    public string? FailureCause { get; init; }

    public DriverProblem? Refusal { get; init; }

    public DriverProblem? StreamRefusal { get; init; }

    public static ChannelScript Carrying(SyntheticStream stream) => new() { Bytes = stream.ToBytes() };

    public static ChannelScript NoLock() =>
        new() { Lock = SignalLock.NotLocked, Bytes = [] };

    public static ChannelScript Silent() => new() { Bytes = [] };
}

public sealed class ScriptedDriverClient : IDriverClient
{
    private readonly Dictionary<TuningParameters, ChannelScript> scripts = [];
    private readonly Dictionary<SessionId, TuningParameters> live = [];
    private readonly HashSet<SessionId> sampled = [];
    private readonly Lock gate = new();

    public string DeviceId { get; init; } = "adapter0";

    public string InstanceId { get; set; } = "instance-a";

    public int BusyRefusalsRemaining { get; set; }

    public string? UnreachableFrom { get; set; }

    public string? GreetingFailure { get; set; }

    public List<TuningParameters> Started { get; } = [];

    public List<SessionPurpose> Purposes { get; } = [];

    public List<SessionId> Stopped { get; } = [];

    public ScriptedDriverClient Script(TuningParameters tuning, ChannelScript script)
    {
        scripts[tuning] = script;

        return this;
    }

    public Task<DriverCall<DriverHello>> GetHealthAsync(CancellationToken cancellationToken)
        => Task.FromResult(GreetingFailure is { } failure
            ? DriverCall<DriverHello>.Unreachable(failure)
            : DriverCall<DriverHello>.Reached(
                new DriverHello(DriverProtocol.Version, InstanceId, [DriverCapabilities.TypedTuning])));

    public Task<DriverCall<IReadOnlyList<TunerSnapshot>>> GetTunersAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            TunerSnapshot[] snapshots = live
                .Select(entry => new TunerSnapshot(DeviceId, TunerKind.Terrestrial, TunerState.Busy)
                {
                    CurrentSession = new CurrentSessionDto
                    {
                        SessionId = entry.Key,
                        Purpose = SessionPurpose.Scan,
                    },
                    SignalQuality = sampled.Contains(entry.Key)
                        ? QualityOf(Script(entry.Value))
                        : null,
                })
                .ToArray();

            return Task.FromResult(
                DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(
                    snapshots.Length > 0
                        ? snapshots
                        : [new TunerSnapshot(DeviceId, TunerKind.Terrestrial, TunerState.Idle)]));
        }
    }

    public Task<DriverCall<SessionSnapshot>> StartSessionAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken)
    {
        TuningParameters tuning = TuningOf(request.Tune!);

        lock (gate)
        {
            Started.Add(tuning);
            Purposes.Add(request.Purpose);

            if (UnreachableFrom is { } failure && Started.Count > 1)
            {
                return Task.FromResult(DriverCall<SessionSnapshot>.Unreachable(failure));
            }

            if (BusyRefusalsRemaining > 0)
            {
                BusyRefusalsRemaining--;
                Started.RemoveAt(Started.Count - 1);

                return Task.FromResult(DriverCall<SessionSnapshot>.Refused(
                    new DriverProblem(SessionRefusalTitles.NoDeviceFree, ["Every usable tuner is busy."])));
            }

            ChannelScript script = Script(tuning);

            if (script.Refusal is { } refusal)
            {
                return Task.FromResult(DriverCall<SessionSnapshot>.Refused(refusal));
            }

            live[request.SessionId] = tuning;

            return Task.FromResult(DriverCall<SessionSnapshot>.Reached(
                new SessionSnapshot(
                    request.SessionId,
                    SessionPurpose.Scan,
                    DeviceId,
                    script.State,
                    DateTimeOffset.UnixEpoch)
                {
                    InstanceId = InstanceId,
                    FailureCause = script.FailureCause,
                }));
        }
    }

    public Task<DriverCall<Stream>> OpenSessionStreamAsync(
        SessionId sessionId,
        string? subscriber,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            if (!live.TryGetValue(sessionId, out TuningParameters? tuning))
            {
                return Task.FromResult(DriverCall<Stream>.Unreachable("That session is not open."));
            }

            ChannelScript script = Script(tuning);

            if (script.StreamRefusal is { } refusal)
            {
                return Task.FromResult(DriverCall<Stream>.Refused(refusal));
            }

            sampled.Add(sessionId);

            Stream stream = script.Paced is { } paced
                ? paced()
                : PacedStream.Ungated(script.Bytes ?? []);

            return Task.FromResult(DriverCall<Stream>.Reached(stream));
        }
    }

    public Task<DriverCall<SessionSnapshot>> StopSessionAsync(
        SessionId sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Stopped.Add(sessionId);
            live.Remove(sessionId);
            sampled.Remove(sessionId);
        }

        return Task.FromResult(DriverCall<SessionSnapshot>.Reached(null));
    }

    public Task<DriverCall<IReadOnlyList<DetectedDeviceDto>>> GetDetectedDevicesAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerLedgerDto>> GetTunerLedgerAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerLedgerDto>> ReplaceTunerLedgerAsync(
        IReadOnlyList<TunerConfigEntry> tuners,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<DriverRestartDto>> RequestRestartAsync(
        CancellationToken cancellationToken)
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

    public Task<DriverCall<IReadOnlyList<DiagnosticSnapshot>>> GetDiagnosticsAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenEventsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    private ChannelScript Script(TuningParameters tuning)
        => scripts.TryGetValue(tuning, out ChannelScript? script) ? script : ChannelScript.NoLock();

    private static SignalQualityDto? QualityOf(ChannelScript script)
        => script.Measured
            ? new SignalQualityDto
            {
                Lock = script.Lock,
                CnrMilliDecibels = script.CnrMilliDecibels,
                MeasuredAt = DateTimeOffset.UnixEpoch,
            }
            : null;

    private static TuningParameters TuningOf(TuneParams tune)
        => tune.System switch
        {
            TuneSystem.IsdbT => TuningParameters.Terrestrial(tune.IsdbT!.PhysicalChannel),
            TuneSystem.IsdbSBs => TuningParameters.Bs(
                tune.IsdbSBs!.BsChannel,
                new TransportStreamId(tune.IsdbSBs.Tsid)),
            _ => TuningParameters.Cs110(tune.IsdbSCs110!.CsChannel),
        };
}
