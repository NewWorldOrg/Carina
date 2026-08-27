using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class SchedulingRunTests
{
    [Fact]
    public void ARunThatAllocatedCarriesThePlanItReached()
    {
        AllocationPlan plan = new([]);

        SchedulingRun run = SchedulingRun.Of(plan);

        Assert.True(run.Settled);
        Assert.Equal(SchedulingRefusal.None, run.Refusal);
        Assert.Same(plan, run.Plan);
    }

    [Fact]
    public void ARunThatAllocatedIsHandedThePlan()
        => Assert.Equal("plan", Assert.Throws<ArgumentNullException>(() => SchedulingRun.Of(null!)).ParamName);

    [Fact]
    public void ARefusedRunHasNoPlanToRead()
    {
        SchedulingRun run = SchedulingRun.Refused(SchedulingRefusal.CapacityUnknown);

        Assert.False(run.Settled);
        Assert.Equal(SchedulingRefusal.CapacityUnknown, run.Refusal);
        Assert.Throws<InvalidOperationException>(() => run.Plan);
    }

    [Fact]
    public void AllocatingIsNotAReasonForRefusing()
    {
        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => SchedulingRun.Refused(SchedulingRefusal.None));

        Assert.Equal("refusal", refused.ParamName);
    }

    [Fact]
    public void ARefusalNamesOneOfTheReasonsThereAre()
    {
        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => SchedulingRun.Refused((SchedulingRefusal)99));

        Assert.Equal("refusal", refused.ParamName);
    }
}
