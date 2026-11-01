using Carina.Driver.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Driver.Tuning;

/// <summary>
/// Looks once a minute for every tuner no session holds, and writes down whether it is still in its place. A tuner a
/// session holds is left alone; reading from it is proof enough.
/// </summary>
public sealed class TunerPatrol(
    DriverConfiguration configuration,
    TunerHardwareBook book,
    ITunerPresence presence,
    Func<IEnumerable<string>> heldDevices,
    TimeProvider clock,
    ILogger<TunerPatrol>? logger = null
) : IHostedService, IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    private ITimer? timer;

    public void Walk()
    {
        HashSet<string> held = new(heldDevices(), StringComparer.Ordinal);

        foreach (DeviceSettings device in configuration.Devices ?? [])
        {
            if (device?.Id is not { } deviceId || held.Contains(deviceId))
            {
                continue;
            }

            book.Looked(device, presence.IsPresent(device, book.For(device)));
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        timer = clock.CreateTimer(_ => WalkQuietly(), null, TimeSpan.Zero, Interval);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        return Task.CompletedTask;
    }

    public void Dispose() => timer?.Dispose();

    private void WalkQuietly()
    {
        try
        {
            Walk();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            (logger ?? NullLogger<TunerPatrol>.Instance).LogWarning(
                error,
                "Looking for the idle tuners failed, and the next look is in {Interval}",
                Interval
            );
        }
    }
}
