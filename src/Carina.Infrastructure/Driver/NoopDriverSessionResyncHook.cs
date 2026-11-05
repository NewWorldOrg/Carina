using Carina.Domain.Driver;

using Puppis.Contracts;

namespace Carina.Infrastructure.Driver;

public sealed class NoopDriverSessionResyncHook : IDriverSessionResyncHook
{
    public Task ReadoptAsync(
        DriverHello hello,
        IReadOnlyList<SessionSnapshot> sessions,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
