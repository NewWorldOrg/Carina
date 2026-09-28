using Carina.Contracts;

namespace Carina.Domain.Driver;

public interface IDriverClient
{
    Task<DriverCall<DriverHello>> GetHealthAsync(CancellationToken cancellationToken);

    Task<DriverCall<IReadOnlyList<TunerSnapshot>>> GetTunersAsync(CancellationToken cancellationToken);

    Task<DriverCall<IReadOnlyList<DetectedDeviceDto>>> GetDetectedDevicesAsync(CancellationToken cancellationToken);

    Task<DriverCall<TunerLedgerDto>> GetTunerLedgerAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the saved ledger. When <paramref name="expectedSavedHash"/> is given, the driver takes the
    /// save only while its saved ledger is still the one that hash was read from.
    /// </summary>
    Task<DriverCall<TunerLedgerDto>> ReplaceTunerLedgerAsync(
        IReadOnlyList<TunerConfigEntry> tuners,
        string? expectedSavedHash,
        CancellationToken cancellationToken);

    /// <summary>
    /// Turns the low-noise block power of one satellite tuner on or off in the saved ledger alone.
    /// </summary>
    Task<DriverCall<TunerLedgerDto>> SwitchLnbPowerAsync(
        string deviceId,
        bool on,
        CancellationToken cancellationToken);

    Task<DriverCall<DriverRestartDto>> RequestRestartAsync(CancellationToken cancellationToken);

    Task<DriverCall<TunerSnapshot>> ToggleTunerAsync(
        string deviceId,
        bool disabled,
        CancellationToken cancellationToken);

    Task<DriverCall<IReadOnlyList<SessionSnapshot>>> GetActiveSessionsAsync(CancellationToken cancellationToken);

    Task<DriverCall<SessionSnapshot>> GetSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<DriverCall<SessionSnapshot>> StartSessionAsync(StartSessionRequest request, CancellationToken cancellationToken);

    Task<DriverCall<SessionSnapshot>> ExtendSessionAsync(
        SessionId sessionId,
        DateTimeOffset endsAt,
        CancellationToken cancellationToken);

    Task<DriverCall<SessionSnapshot>> StopSessionAsync(SessionId sessionId, string reason, CancellationToken cancellationToken);

    Task<DriverCall<IReadOnlyList<DiagnosticSnapshot>>> GetDiagnosticsAsync(CancellationToken cancellationToken);

    Task<DriverCall<IReadOnlyList<StorageRootDto>>> GetStorageAsync(CancellationToken cancellationToken);

    Task<DriverCall<RecordingErasedDto>> EraseRecordingAsync(
        string recordingId,
        string outputRoot,
        CancellationToken cancellationToken);

    Task<DriverCall<StrayFileErasedDto>> EraseStrayFileAsync(
        StrayFileErasureRequest request,
        CancellationToken cancellationToken);

    Task<DriverCall<Stream>> OpenSessionStreamAsync(
        SessionId sessionId,
        string? subscriber,
        CancellationToken cancellationToken);

    Task<DriverCall<Stream>> OpenEventsAsync(CancellationToken cancellationToken);
}
