using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed record CollectionSettings
{
    public TimeSpan BetweenSweeps { get; init; } = TimeSpan.FromMinutes(30);

    public TimeSpan WantedCoverage { get; init; } = TimeSpan.FromDays(8);

    public TimeSpan RevisitsBelow { get; init; } = TimeSpan.FromDays(3);

    public TimeSpan BetweenVisits { get; init; } = TimeSpan.FromHours(6);

    public TimeSpan BeforeRetrying { get; init; } = TimeSpan.FromHours(2);

    public TimeSpan LongestVisit { get; init; } = TimeSpan.FromMinutes(3);

    public TimeSpan KeepEndedProgrammes { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan? ArchiveRetention { get; init; }

    public TimeSpan LongestBackOff { get; init; } = TimeSpan.FromHours(24);

    public TimeSpan BetweenBoosts { get; init; } = TimeSpan.FromMinutes(10);

    public TimeSpan LongestBoost { get; init; } = TimeSpan.FromMinutes(30);

    public bool RidesAlong { get; init; } = true;

    public TimeSpan BetweenRideAlongSaves { get; init; } = TimeSpan.FromMinutes(5);

    public TimeSpan BetweenSessionChecks { get; init; } = TimeSpan.FromSeconds(30);

    public RotationBackoff WhenTunersAreFull { get; init; } =
        new(TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(5), 4);

    /// <summary>
    /// The longest a collector that is running goes from one attempt to the next while a visit is due: the wait
    /// between sweeps, the waits a sweep spends on full tuners before it gives up, and one visit that listens
    /// for as long as it may and writes for as long again.
    /// </summary>
    public TimeSpan LongestBetweenAttempts()
        => BetweenSweeps + WhenTunersAreFull.WaitBeforeTheCeiling() + (LongestVisit * 2);
}
