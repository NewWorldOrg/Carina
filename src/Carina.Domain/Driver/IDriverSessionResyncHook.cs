using Puppis.Contracts;

namespace Carina.Domain.Driver;

public interface IDriverSessionResyncHook
{
    Task ReadoptAsync(
        DriverHello hello,
        IReadOnlyList<SessionSnapshot> sessions,
        CancellationToken cancellationToken);
}
