using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.TestSupport;

public sealed class RecordingDriver : IDriverClient
{
    private static readonly DateTimeOffset Epoch = new(2026, 8, 26, 20, 0, 0, TimeSpan.Zero);

    private readonly Lock gate = new();

    public string DeviceId { get; set; } = "adapter0";

    public long FreeBytes { get; set; } = 4_000_000_000_000L;

    public string RootName { get; set; } = "primary";

    public DriverCall<SessionSnapshot>? RefusesToStart { get; set; }

    public DriverCall<SessionSnapshot>? RefusesToStop { get; set; }

    public List<string> Log { get; } = [];

    public List<StartSessionRequest> Started { get; } = [];

    public List<string> StopReasons { get; } = [];

    public Task<DriverCall<IReadOnlyList<StorageRootDto>>> GetStorageAsync(CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Log.Add("storage");
        }

        return Task.FromResult(DriverCall<IReadOnlyList<StorageRootDto>>.Reached(
        [
            new StorageRootDto
            {
                Name = RootName,
                FreeBytes = FreeBytes,
                TotalBytes = 8_000_000_000_000L,
                Writable = true,
            },
        ]));
    }

    public Task<DriverCall<SessionSnapshot>> StartSessionAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Log.Add($"start:{request.SessionId}");
            Started.Add(request);
        }

        return Task.FromResult(RefusesToStart ?? DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                request.SessionId,
                request.Purpose,
                DeviceId,
                SessionState.Active,
                request.EndsAt!.Value.AddMinutes(-1))
            {
                EndsAt = request.EndsAt,
                OutputRoot = request.OutputRoot,
                RecordingId = request.RecordingId,
            }));
    }

    public Task<DriverCall<SessionSnapshot>> StopSessionAsync(
        SessionId sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        lock (gate)
        {
            Log.Add($"stop:{sessionId}");
            StopReasons.Add(reason);
        }

        return Task.FromResult(RefusesToStop ?? DriverCall<SessionSnapshot>.Reached(
            new SessionSnapshot(
                sessionId,
                SessionPurpose.Recording,
                DeviceId,
                SessionState.Stopped,
                Epoch)));
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

    public Task<DriverCall<SessionSnapshot>> GetSessionAsync(
        SessionId sessionId,
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
