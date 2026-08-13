using Carina.Domain.DriverStatus;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.DriverStatus;

public sealed class NotConnectedDriverStatusReader(IOptions<DriverOptions> driverOptions) : IDriverStatusReader
{
    public DriverSocketPath SocketPath { get; } = new(driverOptions.Value.SocketPath!);

    public Task<DriverConnection> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(DriverConnection.NotConnected);
}
