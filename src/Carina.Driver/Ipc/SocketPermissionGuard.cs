using Carina.Driver.Configuration;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Driver.Ipc;

public sealed class SocketPermissionGuard(
    DriverConfiguration configuration,
    IServer server,
    ILogger<SocketPermissionGuard> logger
) : IHostedLifecycleService
{
    public Task StartedAsync(CancellationToken cancellationToken)
    {
        string path = configuration.SocketPath!;

        AssertOnlyTheSocketIsServed(path);

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

    private void AssertOnlyTheSocketIsServed(string path)
    {
        ICollection<string> addresses =
            server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        string expected = $"http://unix:{path}";

        string[] strangers = addresses
            .Where(address => !string.Equals(address, expected, StringComparison.Ordinal))
            .ToArray();

        if (strangers.Length is 0)
        {
            return;
        }

        DriverSocket.TryUnlink(path);

        throw new DriverSocketException(
            $"The server is answering on {string.Join(", ", strangers.Select(address => $"'{address}'"))} besides the socket. The driver answers on a Unix socket only and never binds a TCP port."
        );
    }

    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
