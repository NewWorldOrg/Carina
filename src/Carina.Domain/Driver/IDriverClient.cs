using Carina.Contracts;

namespace Carina.Domain.Driver;

public interface IDriverClient
{
    Task<DriverCall<DriverHello>> GetHealthAsync(CancellationToken cancellationToken);

    Task<DriverCall<IReadOnlyList<TunerSnapshot>>> GetTunersAsync(CancellationToken cancellationToken);

    Task<DriverCall<IReadOnlyList<DetectedDeviceDto>>> GetDetectedDevicesAsync(CancellationToken cancellationToken);

    Task<DriverCall<TunerLedgerDto>> GetTunerLedgerAsync(CancellationToken cancellationToken);

    Task<DriverCall<TunerLedgerDto>> ReplaceTunerLedgerAsync(
        IReadOnlyList<TunerConfigEntry> tuners,
        CancellationToken cancellationToken);

    Task<DriverCall<TunerSnapshot>> ToggleTunerAsync(
        string deviceId,
        bool disabled,
        CancellationToken cancellationToken);

    Task<DriverCall<IReadOnlyList<SessionSnapshot>>> GetActiveSessionsAsync(CancellationToken cancellationToken);

    Task<DriverCall<SessionSnapshot>> GetSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<DriverCall<SessionSnapshot>> StartSessionAsync(StartSessionRequest request, CancellationToken cancellationToken);

    Task<DriverCall<SessionSnapshot>> StopSessionAsync(SessionId sessionId, CancellationToken cancellationToken);

    Task<DriverCall<IReadOnlyList<DiagnosticSnapshot>>> GetDiagnosticsAsync(CancellationToken cancellationToken);

    Task<DriverCall<Stream>> OpenSessionStreamAsync(
        SessionId sessionId,
        string? subscriber,
        CancellationToken cancellationToken);

    Task<DriverCall<Stream>> OpenEventsAsync(CancellationToken cancellationToken);
}
