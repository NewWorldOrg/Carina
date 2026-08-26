namespace Carina.Domain.Reservations;

public enum AllocationVerdict
{
    Secured = 1,

    Contended = 2,

    Pinned = 3,

    Unreachable = 4,
}

public sealed class AllocationDecision
{
    public AllocationDecision(ReservationId id, AllocationVerdict verdict, IReadOnlyList<ReservationId> instead)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(instead);

        if (!Enum.IsDefined(verdict))
        {
            throw new ArgumentOutOfRangeException(
                nameof(verdict),
                verdict,
                "A decision names one of the verdicts the planner reaches.");
        }

        if (verdict is not AllocationVerdict.Contended && instead.Count > 0)
        {
            throw new ArgumentException(
                "Only a candidate that lost a contest names what is recorded in its place.",
                nameof(instead));
        }

        Id = id;
        Verdict = verdict;
        Instead = instead;
    }

    public ReservationId Id { get; }

    public AllocationVerdict Verdict { get; }

    public IReadOnlyList<ReservationId> Instead { get; }

    public bool KeepsATuner => Verdict is AllocationVerdict.Secured or AllocationVerdict.Pinned;
}

public sealed class AllocationPlan
{
    private readonly Dictionary<ReservationId, AllocationDecision> answered;

    public AllocationPlan(IReadOnlyList<AllocationDecision> decisions)
    {
        ArgumentNullException.ThrowIfNull(decisions);

        answered = [];

        foreach (AllocationDecision decision in decisions)
        {
            if (!answered.TryAdd(decision.Id, decision))
            {
                throw new ArgumentException("A plan answers each reservation once.", nameof(decisions));
            }
        }

        Decisions = decisions;
    }

    public IReadOnlyList<AllocationDecision> Decisions { get; }

    public IReadOnlyList<AllocationDecision> Contended =>
        [.. Decisions.Where(decision => decision.Verdict is AllocationVerdict.Contended)];

    public AllocationDecision For(ReservationId id)
    {
        ArgumentNullException.ThrowIfNull(id);

        return answered.TryGetValue(id, out AllocationDecision? decision)
            ? decision
            : throw new KeyNotFoundException($"The plan says nothing about reservation {id}.");
    }
}
