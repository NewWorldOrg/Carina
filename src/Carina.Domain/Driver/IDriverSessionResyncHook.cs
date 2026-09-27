using Carina.Contracts;

namespace Carina.Domain.Driver;

/// <summary>
/// Called when the driver's greeting names an instance other than the one this side last took up,
/// with what that instance answered about its sessions. A connection that drops and comes back to
/// the same instance does not reach here.
/// </summary>
public interface IDriverSessionResyncHook
{
    Task ReadoptAsync(
        DriverHello hello,
        IReadOnlyList<SessionSnapshot> sessions,
        CancellationToken cancellationToken);
}
