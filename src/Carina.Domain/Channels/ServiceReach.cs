using Carina.Contracts;

namespace Carina.Domain.Channels;

public enum ServiceReachLevel
{
    Unmeasured,
    Reaching,
    Silent,
    Missing,
}

public sealed record SystemReach(
    TuneSystem System,
    ServiceReachLevel Level,
    int Services,
    DateTime? LastSeenAt);

public static class ServiceReach
{
    public static IReadOnlyList<SystemReach> Assess(
        IReadOnlyList<TuneSystem> served,
        IReadOnlyList<CandidateChannel> candidates,
        TimeSpan silence,
        DateTime now)
    {
        ArgumentNullException.ThrowIfNull(served);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(silence, TimeSpan.Zero, nameof(silence));

        return
        [
            .. served
                .Distinct()
                .Order()
                .Select(system => On(system, [.. candidates.Where(candidate => candidate.Tuning.System == system)], silence, now)),
        ];
    }

    private static SystemReach On(
        TuneSystem system,
        IReadOnlyList<CandidateChannel> candidates,
        TimeSpan silence,
        DateTime now)
    {
        if (candidates.Count is 0)
        {
            return new SystemReach(system, ServiceReachLevel.Unmeasured, 0, null);
        }

        int reaching = candidates
            .Where(candidate => candidate.IsInRotation)
            .DistinctBy(candidate => (candidate.NetworkId.Value, candidate.ServiceId.Value))
            .Count();

        DateTime lastSeenAt = candidates.Max(candidate => candidate.LastSeenAt);

        if (reaching > 0)
        {
            return new SystemReach(system, ServiceReachLevel.Reaching, reaching, lastSeenAt);
        }

        return new SystemReach(
            system,
            now - lastSeenAt >= silence ? ServiceReachLevel.Missing : ServiceReachLevel.Silent,
            0,
            lastSeenAt);
    }
}
