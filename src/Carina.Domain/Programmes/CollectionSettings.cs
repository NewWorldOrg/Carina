using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed record CollectionSettings
{
    public TimeSpan BetweenSweeps { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan WantedCoverage { get; init; } = TimeSpan.FromDays(3);

    public TimeSpan BetweenVisits { get; init; } = TimeSpan.FromHours(6);

    public TimeSpan BeforeRetrying { get; init; } = TimeSpan.FromHours(2);

    public TimeSpan LongestVisit { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan KeepEndedProgrammes { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan LongestBackOff { get; init; } = TimeSpan.FromHours(24);

    public RotationBackoff WhenTunersAreFull { get; init; } =
        new(TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(5), 4);
}
