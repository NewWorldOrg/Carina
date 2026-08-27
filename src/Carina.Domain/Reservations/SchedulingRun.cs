namespace Carina.Domain.Reservations;

public enum SchedulingRefusal
{
    None = 1,

    CapacityUnknown = 2,
}

public sealed class SchedulingRun
{
    private readonly AllocationPlan? plan;

    private SchedulingRun(SchedulingRefusal refusal, AllocationPlan? plan)
    {
        Refusal = refusal;
        this.plan = plan;
    }

    public SchedulingRefusal Refusal { get; }

    public bool Settled => Refusal is SchedulingRefusal.None;

    public AllocationPlan Plan => plan
        ?? throw new InvalidOperationException(
            $"Nothing was allocated because the tuners could not be counted ({Refusal}), so there is no plan to read.");

    public static SchedulingRun Of(AllocationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new SchedulingRun(SchedulingRefusal.None, plan);
    }

    public static SchedulingRun Refused(SchedulingRefusal refusal)
    {
        if (refusal is SchedulingRefusal.None)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A refusal says why nothing was allocated, and there is no such reason for a run that allocated.");
        }

        if (!Enum.IsDefined(refusal))
        {
            throw new ArgumentOutOfRangeException(
                nameof(refusal),
                refusal,
                "A refusal names one of the reasons scheduling stops short.");
        }

        return new SchedulingRun(refusal, null);
    }
}
