using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.Infrastructure.Tests.Channels;

internal sealed class LedgerOnlyDriverClient : IDriverClient
{
    public DriverCall<TunerLedgerDto> Ledger { get; set; } =
        DriverCall<TunerLedgerDto>.Reached(new TunerLedgerDto());

    public DriverCall<IReadOnlyList<TunerSnapshot>> Tuners { get; set; } =
        DriverCall<IReadOnlyList<TunerSnapshot>>.Reached([]);

    public int LedgerReads { get; private set; }

    public Task<DriverCall<TunerLedgerDto>> GetTunerLedgerAsync(CancellationToken cancellationToken)
    {
        LedgerReads++;

        return Task.FromResult(Ledger);
    }

    public Task<DriverCall<IReadOnlyList<TunerSnapshot>>> GetTunersAsync(CancellationToken cancellationToken)
        => Task.FromResult(Tuners);

    public Task<DriverCall<DriverHello>> GetHealthAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<IReadOnlyList<DetectedDeviceDto>>> GetDetectedDevicesAsync(CancellationToken cancellationToken)
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

    public Task<DriverCall<IReadOnlyList<SessionSnapshot>>> GetActiveSessionsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<SessionSnapshot>> GetSessionAsync(SessionId sessionId, CancellationToken cancellationToken)
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

    public Task<DriverCall<IReadOnlyList<DiagnosticSnapshot>>> GetDiagnosticsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenSessionStreamAsync(
        SessionId sessionId,
        string? subscriber,
        CancellationToken cancellationToken)
        => throw new NotSupportedException();

    public Task<DriverCall<Stream>> OpenEventsAsync(CancellationToken cancellationToken)
        => throw new NotSupportedException();
}
