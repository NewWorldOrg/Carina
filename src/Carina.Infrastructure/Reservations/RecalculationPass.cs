using Carina.Domain.Reservations;
using Carina.Infrastructure.Rules;

namespace Carina.Infrastructure.Reservations;

public enum RecalculationRefusal
{
    OneIsAlreadyRunning = 1,

    NothingAsked = 2,
}

public enum RecalculationStage
{
    Rules = 1,

    Scheduling = 2,

    Outcomes = 3,
}

public sealed record RecalculationFault(RecalculationStage Stage, string Fault);

public sealed record RecalculationPass(
    IReadOnlyList<RecalculationTrigger> Answering,
    RecalculationReach Reach,
    long Revision,
    RuleApplicationRun? Applied,
    ReservationOutcomeRun? Recorded,
    SchedulingRun? Settled,
    IReadOnlyList<RecalculationFault> Faults,
    RecalculationRefusal? Refusal)
{
    public bool Ran => Refusal is null;

    public static RecalculationPass Refused(RecalculationRefusal refusal)
        => new([], RecalculationReach.Nothing, 0, null, null, null, [], refusal);

    public static RecalculationPass Of(
        IReadOnlyList<RecalculationTrigger> answering,
        RecalculationReach reach,
        long revision,
        RuleApplicationRun? applied,
        ReservationOutcomeRun? recorded,
        SchedulingRun? settled,
        IReadOnlyList<RecalculationFault> faults)
        => new(answering, reach, revision, applied, recorded, settled, faults, null);
}
