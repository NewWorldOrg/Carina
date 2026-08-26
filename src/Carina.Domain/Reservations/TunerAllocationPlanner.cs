using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Reservations;

public static class TunerAllocationPlanner
{
    public static AllocationPlan Plan(
        IReadOnlyList<AllocationCandidate> candidates,
        TunerCapacity capacity,
        RollingHorizon horizon,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(capacity);
        ArgumentNullException.ThrowIfNull(horizon);

        DateTime moment = UtcTimes.Required(at, nameof(at));
        AllocationCandidate[] ranked = [.. candidates.Order(Ranking.Order)];
        List<Held> held = [];

        foreach (AllocationCandidate candidate in ranked)
        {
            if (candidate.Pinned && candidate.Tuning is { } tuning)
            {
                held.Add(Hold(candidate, tuning, moment, horizon));
            }
        }

        List<AllocationDecision> decisions = [];

        foreach (AllocationCandidate candidate in ranked)
        {
            decisions.Add(Decide(candidate, held, capacity, moment, horizon));
        }

        return new AllocationPlan(decisions);
    }

    private static AllocationDecision Decide(
        AllocationCandidate candidate,
        List<Held> held,
        TunerCapacity capacity,
        DateTime at,
        RollingHorizon horizon)
    {
        if (candidate.Pinned)
        {
            return new AllocationDecision(candidate.Id, AllocationVerdict.Pinned, []);
        }

        if (candidate.Tuning is not { } tuning)
        {
            return new AllocationDecision(candidate.Id, AllocationVerdict.Unreachable, []);
        }

        Held wanted = Hold(candidate, tuning, at, horizon);
        held.Add(wanted);

        if (Seatable(held, wanted, capacity))
        {
            return new AllocationDecision(candidate.Id, AllocationVerdict.Secured, []);
        }

        held.RemoveAt(held.Count - 1);

        return new AllocationDecision(
            candidate.Id,
            AllocationVerdict.Contended,
            RecordedInstead(held, wanted, capacity));
    }

    private static Held Hold(
        AllocationCandidate candidate,
        TuningParameters tuning,
        DateTime at,
        RollingHorizon horizon)
        => new(candidate, tuning, EndsAt(candidate, at, horizon));

    private static DateTime EndsAt(AllocationCandidate candidate, DateTime at, RollingHorizon horizon)
    {
        if (candidate.EndAtConfirmed || !candidate.Pinned)
        {
            return candidate.EffectiveEndAt;
        }

        DateTime rolled = at + horizon.Value;

        return rolled > candidate.EffectiveEndAt ? rolled : candidate.EffectiveEndAt;
    }

    private static bool Seatable(List<Held> held, Held added, TunerCapacity capacity)
    {
        foreach (DateTime moment in Instants(held, added))
        {
            if (RidesAlong(held, added, moment))
            {
                continue;
            }

            if (!capacity.CanSeat(DemandAt(held, moment)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RidesAlong(List<Held> held, Held added, DateTime moment)
        => held.Any(hold =>
            !ReferenceEquals(hold, added)
            && hold.Covers(moment)
            && hold.Tuning == added.Tuning);

    private static IEnumerable<DateTime> Instants(List<Held> held, Held added)
        => held
            .Select(hold => hold.StartsAt)
            .Where(added.Covers)
            .Distinct();

    private static Dictionary<TuneSystem, int> DemandAt(List<Held> held, DateTime moment)
        => held
            .Where(hold => hold.Covers(moment))
            .Select(hold => hold.Tuning)
            .Distinct()
            .GroupBy(tuning => tuning.System)
            .ToDictionary(group => group.Key, group => group.Count());

    private static IReadOnlyList<ReservationId> RecordedInstead(
        List<Held> held,
        Held loser,
        TunerCapacity capacity)
    {
        if (!capacity.CanSeat(Wanting(loser.Tuning.System)))
        {
            return [];
        }

        return
        [
            .. held
                .Where(hold => hold.Overlaps(loser))
                .Where(hold => !hold.Tuning.Equals(loser.Tuning))
                .Where(hold => HoldsASeatTheLoserWanted(held, hold, loser, capacity))
                .Select(hold => hold.Candidate)
                .Order(Ranking.Order)
                .Select(candidate => candidate.Id),
        ];
    }

    private static bool HoldsASeatTheLoserWanted(
        List<Held> held,
        Held hold,
        Held loser,
        TunerCapacity capacity)
        => !capacity.CanSeat(Wanting(hold.Tuning.System, loser.Tuning.System))
           || Seatable([.. held.Where(other => !ReferenceEquals(other, hold)), loser], loser, capacity);

    private static Dictionary<TuneSystem, int> Wanting(params TuneSystem[] systems)
        => systems
            .GroupBy(system => system)
            .ToDictionary(group => group.Key, group => group.Count());

    private sealed record Held(AllocationCandidate Candidate, TuningParameters Tuning, DateTime EndsAt)
    {
        public DateTime StartsAt => Candidate.EffectiveStartAt;

        public bool Covers(DateTime moment) => StartsAt <= moment && moment < EndsAt;

        public bool Overlaps(Held other) => StartsAt < other.EndsAt && other.StartsAt < EndsAt;
    }

    private sealed class Ranking : IComparer<AllocationCandidate>
    {
        public static Ranking Order { get; } = new();

        public int Compare(AllocationCandidate? x, AllocationCandidate? y)
        {
            ArgumentNullException.ThrowIfNull(x);
            ArgumentNullException.ThrowIfNull(y);

            int byPriority = y.Priority.Value.CompareTo(x.Priority.Value);

            if (byPriority is not 0)
            {
                return byPriority;
            }

            int byStart = x.EffectiveStartAt.CompareTo(y.EffectiveStartAt);

            if (byStart is not 0)
            {
                return byStart;
            }

            int byProgramme = ByProgramme(x.Programme, y.Programme);

            return byProgramme is not 0
                ? byProgramme
                : string.CompareOrdinal(x.Id.Value.ToString(), y.Id.Value.ToString());
        }

        private static int ByProgramme(ProgrammeRef x, ProgrammeRef y)
        {
            int byNetwork = x.NetworkId.Value.CompareTo(y.NetworkId.Value);

            if (byNetwork is not 0)
            {
                return byNetwork;
            }

            int byService = x.ServiceId.Value.CompareTo(y.ServiceId.Value);

            if (byService is not 0)
            {
                return byService;
            }

            int byEvent = x.EventId.Value.CompareTo(y.EventId.Value);

            return byEvent is not 0 ? byEvent : x.StartsAt.CompareTo(y.StartsAt);
        }
    }
}
