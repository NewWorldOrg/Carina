using Carina.Domain.Recordings;

namespace Carina.TestSupport;

/// <summary>
/// Answers what decides whether the machine is idle from whatever a test holds at the moment it is asked, and
/// keeps the moments it was asked at.
/// </summary>
public sealed class HeldBusyness(Func<Busyness> reading) : IBusynessReader
{
    public List<DateTime> Asked { get; } = [];

    public Task<Busyness> ReadAsync(DateTime now, CancellationToken cancellationToken)
    {
        Asked.Add(now);

        return Task.FromResult(reading());
    }
}
