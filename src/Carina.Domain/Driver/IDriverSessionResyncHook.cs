using Carina.Contracts;

namespace Carina.Domain.Driver;

public interface IDriverSessionResyncHook
{
    Task ReadoptAsync(IReadOnlyList<SessionSnapshot> sessions, CancellationToken cancellationToken);
}
