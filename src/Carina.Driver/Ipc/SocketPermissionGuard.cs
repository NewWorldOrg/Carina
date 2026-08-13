using Carina.Driver.Configuration;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Driver.Ipc;

public sealed class SocketPermissionGuard(
    DriverConfiguration configuration,
    ILogger<SocketPermissionGuard> logger
) : IHostedLifecycleService
{
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        var path = configuration.SocketPath!;

        try
        {
            DriverSocket.Secure(path, configuration.SocketGroupId);
        }
        catch (DriverSocketException)
        {
            DriverSocket.TryUnlink(path);

            throw;
        }

        logger.LogInformation(
            "The driver answers on '{SocketPath}' as {Permissions} group {SocketGroupId}.",
            path,
            UnixFile.Octal(DriverSocket.RequiredPermissions),
            configuration.SocketGroupId
        );

        return Task.CompletedTask;
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
