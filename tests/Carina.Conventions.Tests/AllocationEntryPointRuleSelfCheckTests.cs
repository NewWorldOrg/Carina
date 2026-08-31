using System.Reflection;

using Carina.Conventions.Tests.Fixtures;
using Carina.Domain.Reservations;

namespace Carina.Conventions.Tests;

public sealed class AllocationEntryPointRuleSelfCheckTests
{
    private static readonly IReadOnlyList<Assembly> Fixtures = [typeof(AllocationFixtures).Assembly];

    private const string Fixture = "Carina.Conventions.Tests.Fixtures.AllocationFixtures";

    [Fact]
    public void DetectsSomethingThatMovesAReservationOnItsOwn()
    {
        Assert.Contains(
            $"{Fixture}.{nameof(AllocationFixtures.MovesAReservationWithoutAskingTheScheduler)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(Reservation), nameof(Reservation.Secure)),
            StringComparer.Ordinal);
    }

    [Fact]
    public void DetectsASecondPlaceTakingAReservationOutOfTheRunning()
    {
        Assert.Contains(
            $"{Fixture}.{nameof(AllocationFixtures.TakesAReservationOutOfTheRunningWithoutAskingTheScheduler)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(Reservation), nameof(Reservation.Cancel)),
            StringComparer.Ordinal);
    }

    [Fact]
    public void DetectsASecondPlaceWritingAReservationOff()
    {
        Assert.Contains(
            $"{Fixture}.{nameof(AllocationFixtures.WritesAReservationOffWithoutTheLedger)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(Reservation), nameof(Reservation.Miss)),
            StringComparer.Ordinal);
    }

    [Fact]
    public void DetectsASecondPlaceWorkingOutWhatFitsOnTheTuners()
    {
        Assert.Contains(
            $"{Fixture}.{nameof(AllocationFixtures.PlansOnItsOwn)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(TunerAllocationPlanner), nameof(TunerAllocationPlanner.Plan)),
            StringComparer.Ordinal);
    }

    [Fact]
    public void DetectsAMoveHandedAroundAsAMethodGroupRatherThanCalled()
    {
        Assert.Contains(
            $"{Fixture}.{nameof(AllocationFixtures.MovesAReservationThroughAMethodGroup)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(Reservation), nameof(Reservation.Contend)),
            StringComparer.Ordinal);
    }

    [Fact]
    public void DoesNotSeeAMoveMadeThroughReflection()
    {
        Assert.DoesNotContain(
            $"{Fixture}.{nameof(AllocationFixtures.MovesAReservationThroughReflection)}",
            CallSiteCensus.CallersOf(Fixtures, typeof(Reservation), nameof(Reservation.Secure)),
            StringComparer.Ordinal);
    }

    [Fact]
    public void SaysNothingAboutAMethodNobodyCalls()
    {
        Assert.Empty(CallSiteCensus.CallersOf(
            Fixtures,
            typeof(AllocationFixtures),
            nameof(AllocationFixtures.NothingCallsThis)));
    }

    [Fact]
    public void KeepsItsPlaceInTheInstructionsPastAJumpTableAndAWideConstant()
    {
        Assert.Equal(
            [$"{Fixture}.{nameof(AllocationFixtures.WalksPastWideOperands)}"],
            CallSiteCensus.CallersOf(
                Fixtures,
                typeof(AllocationFixtures),
                nameof(AllocationFixtures.BeyondTheWideOperands)));
    }

    [Fact]
    public void SaysNothingAboutAMethodThatIsNotThere()
    {
        Assert.Empty(CallSiteCensus.CallersOf(Fixtures, typeof(Reservation), "TheresNoSuchMove"));
    }

    [Fact]
    public void ReadsTheBodiesItIsPointedAt()
    {
        Assert.True(CallSiteCensus.MethodsRead(Fixtures) > 0);
    }

    [Fact]
    public void ReadsNothingWhenItIsPointedAtNothing()
    {
        Assert.Equal(0, CallSiteCensus.MethodsRead([]));
        Assert.Empty(CallSiteCensus.CallersOf([], typeof(Reservation), nameof(Reservation.Secure)));
    }
}
