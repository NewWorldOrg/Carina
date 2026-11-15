using Carina.Domain.Driver;

using Puppis.Contracts;

namespace Carina.Api.Tests.Support;

internal sealed class TunerHoldingDriverClient : IDriverClient
{
    public static readonly DateTime At = new(2026, 8, 15, 0, 0, 0, DateTimeKind.Utc);

    public IReadOnlyList<TunerSnapshot> Tuners { get; set; } =
    [
        new TunerSnapshot("adapter0", TunerKind.Terrestrial, TunerState.Idle),
        new TunerSnapshot("adapter1", TunerKind.Satellite, TunerState.Idle),
    ];

    public string? Unreachable { get; set; }

    public Task<DriverCall<IReadOnlyList<TunerSnapshot>>> GetTunersAsync(CancellationToken cancellationToken)
        => Task.FromResult(Unreachable is { } failure
            ? DriverCall<IReadOnlyList<TunerSnapshot>>.Unreachable(failure)
            : DriverCall<IReadOnlyList<TunerSnapshot>>.Reached(Tuners));

    public Task<DriverCall<DriverHello>> GetHealthAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<DetectedDeviceDto>>> GetDetectedDevicesAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerLedgerDto>> GetTunerLedgerAsync(CancellationToken cancellationToken)
        => Task.FromResult(Unreachable is { } failure
            ? DriverCall<TunerLedgerDto>.Unreachable(failure)
            : DriverCall<TunerLedgerDto>.Reached(new TunerLedgerDto
            {
                Tuners = [.. Tuners.Select(tuner => new TunerConfigEntry { DeviceId = tuner.DeviceId, Kind = tuner.Kind })],
            }));

    public Task<DriverCall<TunerLedgerDto>> ReplaceTunerLedgerAsync(
        IReadOnlyList<TunerConfigEntry> tuners,
        string? expectedSavedHash,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<TunerLedgerDto>> SwitchLnbPowerAsync(
        string deviceId,
        bool on,
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

    public Task<DriverCall<SessionSnapshot>> StartSessionAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<SessionSnapshot>> ExtendSessionAsync(
        SessionId sessionId,
        DateTimeOffset endsAt,
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

    public Task<DriverCall<StrayFileErasedDto>> EraseStrayFileAsync(
        StrayFileErasureRequest request,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<RecordingErasedDto>> EraseRecordingAsync(
        string recordingId,
        string outputRoot,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenEventsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<StorageRootDto>>> GetStorageAsync(
        CancellationToken cancellationToken)
        => throw new NotSupportedException();
}
