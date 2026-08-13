using Carina.Contracts;

namespace Carina.Infrastructure.Driver;

public sealed record DriverSupervisionSettings(
    TimeSpan FirstDelay,
    TimeSpan DelayCap,
    IReadOnlyList<string> ExpectedCapabilities,
    Func<double>? Chance = null)
{
    public static DriverSupervisionSettings Default { get; } = new(
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(30),
        [DriverCapabilities.Recording, DriverCapabilities.Live]);
}
