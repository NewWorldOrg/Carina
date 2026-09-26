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
        List<Occupied> occupied = [];

        foreach (AllocationCandidate candidate in ranked.Where(candidate => candidate.Pinned))
        {
            if (candidate.Tuning is { } tuning)
            {
                held.Add(Hold(candidate, tuning, moment, horizon));
            }
            else
            {
                occupied.Add(new Occupied(candidate, EndsAt(candidate, moment, horizon)));
            }
        }

        List<AllocationDecision> decisions = [];

        foreach (AllocationCandidate candidate in ranked)
        {
            decisions.Add(Decide(candidate, held, occupied, capacity, moment, horizon));
        }

        return new AllocationPlan(decisions);
    }

    private static AllocationDecision Decide(
        AllocationCandidate candidate,
        List<Held> held,
        IReadOnlyList<Occupied> occupied,
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
        DateTime[] failures = [.. Failures(held, occupied, wanted, capacity)];

        if (failures.Length is 0)
        {
            return new AllocationDecision(candidate.Id, AllocationVerdict.Secured, []);
        }

        held.RemoveAt(held.Count - 1);

        return new AllocationDecision(
            candidate.Id,
            AllocationVerdict.Contended,
            RecordedInstead(held, occupied, wanted, failures, capacity));
    }

    private static Held Hold(
        AllocationCandidate candidate,
        TuningParameters tuning,
        DateTime at,
        RollingHorizon horizon)
        => new(candidate, tuning, EndsAt(candidate, at, horizon));

    private static DateTime EndsAt(AllocationCandidate candidate, DateTime at, RollingHorizon horizon)
    {
        DateTime promised = candidate.HeldUntil is { } held && held > candidate.EffectiveEndAt
            ? held
            : candidate.EffectiveEndAt;

        if (candidate.EndAtConfirmed || !candidate.Pinned)
        {
            return promised;
        }

        DateTime rolled = at + horizon.Value;

        return rolled > promised ? rolled : promised;
    }

    private static bool Seatable(
        List<Held> held,
        IReadOnlyList<Occupied> occupied,
        Held added,
        TunerCapacity capacity)
        => !Failures(held, occupied, added, capacity).Any();

    private static IEnumerable<DateTime> Failures(
        List<Held> held,
        IReadOnlyList<Occupied> occupied,
        Held added,
        TunerCapacity capacity)
        => Instants(held, occupied, added)
            .Where(moment => !RidesAlong(held, added, moment))
            .Where(moment => !Seats(
                capacity,
                [.. occupied.Where(taking => taking.Covers(moment))],
                DemandAt(held, moment)));

    private static bool Seats(TunerCapacity capacity, Occupied[] taking, Dictionary<TuneSystem, int> demand)
        => taking is [Occupied first, .. Occupied[] rest]
            ? capacity.LeftWhenTaken(first.Candidate.HeldOn?.Value).All(left => Seats(left, rest, demand))
            : capacity.CanSeat(demand);

    private static bool RidesAlong(List<Held> held, Held added, DateTime moment)
        => held.Any(hold =>
            !ReferenceEquals(hold, added)
            && hold.Covers(moment)
            && hold.Tuning == added.Tuning);

    private static IEnumerable<DateTime> Instants(List<Held> held, IReadOnlyList<Occupied> occupied, Held added)
        => held
            .Select(hold => hold.StartsAt)
            .Concat(occupied.Select(taking => taking.StartsAt))
            .Concat(held.Where(hold => hold.Tuning == added.Tuning).Select(hold => hold.EndsAt))
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
        IReadOnlyList<Occupied> occupied,
        Held loser,
        IReadOnlyList<DateTime> failures,
        TunerCapacity capacity)
        => [.. held
            .Where(hold => hold.Overlaps(loser))
            .Where(hold => !hold.Tuning.Equals(loser.Tuning))
            .Where(hold => HoldsASeatTheLoserWanted(held, occupied, hold, loser, failures, capacity))
            .Select(hold => hold.Candidate)
            .Concat(occupied
                .Where(taking => taking.Overlaps(loser))
                .Where(taking => Seatable(
                    [.. held, loser],
                    [.. occupied.Where(other => !ReferenceEquals(other, taking))],
                    loser,
                    capacity))
                .Select(taking => taking.Candidate))
            .Order(Ranking.Order)
            .Select(candidate => candidate.Id)];

    private static bool HoldsASeatTheLoserWanted(
        List<Held> held,
        IReadOnlyList<Occupied> occupied,
        Held hold,
        Held loser,
        IReadOnlyList<DateTime> failures,
        TunerCapacity capacity)
        => (capacity.SharesSeats(hold.Tuning.System, loser.Tuning.System) && failures.Any(hold.Covers))
           || Seatable([.. held.Where(other => !ReferenceEquals(other, hold)), loser], occupied, loser, capacity);

    private sealed record Held(AllocationCandidate Candidate, TuningParameters Tuning, DateTime EndsAt)
    {
        public DateTime StartsAt => Candidate.EffectiveStartAt;

        public bool Covers(DateTime moment) => StartsAt <= moment && moment < EndsAt;

        public bool Overlaps(Held other) => StartsAt < other.EndsAt && other.StartsAt < EndsAt;
    }

    private sealed record Occupied(AllocationCandidate Candidate, DateTime EndsAt)
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
