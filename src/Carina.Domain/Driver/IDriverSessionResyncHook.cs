using Carina.Contracts;

namespace Carina.Domain.Driver;

/// <summary>
/// Called when the driver's greeting names an instance other than the one this side last took up,
/// and handed what that instance answered to a reading of its sessions. Those two together are the
/// only evidence there is that a recording lost the session it was being written on: a connection
/// that dropped and came back never reaches here, because the driver writes straight across one.
/// </summary>
public interface IDriverSessionResyncHook
{
    Task ReadoptAsync(
        DriverHello hello,
        IReadOnlyList<SessionSnapshot> sessions,
        CancellationToken cancellationToken);
}
