using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class SchedulingRunTests
{
    [Fact]
    public void ARunThatAllocatedCarriesThePlanItReached()
    {
        AllocationPlan plan = new([]);

        SchedulingRun run = SchedulingRun.Of(plan, 0);

        Assert.True(run.Settled);
        Assert.Equal(SchedulingRefusal.None, run.Refusal);
        Assert.Same(plan, run.Plan);
        Assert.Equal(0, run.SeatsLeftOut);
    }

    [Fact]
    public void ARunSaysHowManySeatsItCouldNotPlaceAndSoLeftOut()
        => Assert.Equal(2, SchedulingRun.Of(new AllocationPlan([]), 2).SeatsLeftOut);

    [Fact]
    public void ARunCannotLeaveOutFewerSeatsThanNone()
    {
        ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => SchedulingRun.Of(new AllocationPlan([]), -1));

        Assert.Equal("seatsLeftOut", refused.ParamName);
    }

    [Fact]
    public void ARefusedRunLeavesOutNoSeatsBecauseItNeverCountedAny()
        => Assert.Equal(0, SchedulingRun.Refused(SchedulingRefusal.CapacityUnknown).SeatsLeftOut);

    [Fact]
    public void ARunThatAllocatedIsHandedThePlan()
        => Assert.Equal("plan", Assert.Throws<ArgumentNullException>(() => SchedulingRun.Of(null!, 0)).ParamName);

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
