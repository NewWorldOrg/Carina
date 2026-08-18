using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Diagnostics;
using Carina.Driver.Sessions;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Driver.Tuning;

public sealed class TunerLedgerReconciler(
    DriverConfiguration configuration,
    ITunerDetector detector,
    TunerSessionManager sessions,
    DiagnosticsStore diagnostics,
    ILogger<TunerLedgerReconciler> logger
) : IHostedLifecycleService
{
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<TunerContradiction> contradictions = TunerLedgerCheck.Contradictions(
            configuration.Devices,
            detector.Detect()
        );

        foreach (TunerContradiction contradiction in contradictions)
        {
            sessions.Fault(contradiction.DeviceId, contradiction.Detail);

            diagnostics.Report(
                DiagnosticReason.DeviceFaulted,
                contradiction.Detail,
                contradiction.DeviceId
            );

            logger.LogError(
                "The tuner {DeviceId} was faulted at startup: {Detail}",
                contradiction.DeviceId,
                contradiction.Detail
            );
        }

        return Task.CompletedTask;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
