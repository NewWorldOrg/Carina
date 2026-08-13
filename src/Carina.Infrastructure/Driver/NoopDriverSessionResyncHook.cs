using Carina.Contracts;
using Carina.Domain.Driver;

namespace Carina.Infrastructure.Driver;

public sealed class NoopDriverSessionResyncHook : IDriverSessionResyncHook
{
    public Task ReadoptAsync(
        IReadOnlyList<SessionSnapshot> sessions,
        CancellationToken cancellationToken)
        => Task.CompletedTask;
}
