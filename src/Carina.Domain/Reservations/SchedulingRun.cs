namespace Carina.Domain.Reservations;

public enum SchedulingRefusal
{
    None = 1,

    CapacityUnknown = 2,

    SomethingArrivedWhileReading = 3,
}

public sealed class SchedulingRun
{
    private readonly AllocationPlan? plan;

    private SchedulingRun(
        SchedulingRefusal refusal,
        AllocationPlan? plan,
        int seatsLeftOut,
        IReadOnlyList<ReservationId> displaced)
    {
        if (seatsLeftOut < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seatsLeftOut),
                seatsLeftOut,
                "A run leaves out a whole number of seats it could not place, and no fewer than none.");
        }

        Refusal = refusal;
        SeatsLeftOut = seatsLeftOut;
        Displaced = displaced;
        this.plan = plan;
    }

    public SchedulingRefusal Refusal { get; }

    public int SeatsLeftOut { get; }

    /// <summary>
    /// The reservations that held a tuner before this run and contend for one after it.
    /// </summary>
    public IReadOnlyList<ReservationId> Displaced { get; }

    public bool Settled => Refusal is SchedulingRefusal.None;

    public AllocationPlan Plan => plan
        ?? throw new InvalidOperationException(
            $"Nothing was allocated because the tuners could not be counted ({Refusal}), so there is no plan to read.");

    public static SchedulingRun Of(AllocationPlan plan, int seatsLeftOut)
    {
        ArgumentNullException.ThrowIfNull(plan);

        return new SchedulingRun(SchedulingRefusal.None, plan, seatsLeftOut, []);
    }

    /// <summary>
    /// The same run, naming who lost their tuner to it.
    /// </summary>
    public SchedulingRun Displacing(IReadOnlyList<ReservationId> displaced)
    {
        ArgumentNullException.ThrowIfNull(displaced);

        return new SchedulingRun(Refusal, Plan, SeatsLeftOut, [.. displaced]);
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

        return new SchedulingRun(refusal, null, 0, []);
    }
}
