using Carina.Contracts;

namespace Carina.Infrastructure.Driver;

public sealed record DriverSupervisionSettings(
    TimeSpan FirstDelay,
    TimeSpan DelayCap,
    IReadOnlyList<string> ExpectedCapabilities,
    Func<double>? Chance = null)
{
    public TimeSpan DrainPoll { get; init; } = TimeSpan.FromSeconds(15);

    public TimeSpan MinimumFeedDwell { get; init; } = TimeSpan.FromSeconds(10);

    public static DriverSupervisionSettings Default { get; } = new(
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(30),
        [DriverCapabilities.Recording, DriverCapabilities.Live]);
}
