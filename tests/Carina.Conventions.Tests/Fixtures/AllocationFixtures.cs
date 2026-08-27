using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Reservations;

namespace Carina.Conventions.Tests.Fixtures;

public static class AllocationFixtures
{
    public static void MovesAReservationWithoutAskingTheScheduler(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        reservation.Secure();
    }

    public static AllocationPlan PlansOnItsOwn(
        IReadOnlyList<AllocationCandidate> candidates,
        TunerCapacity capacity,
        DateTime at)
        => TunerAllocationPlanner.Plan(candidates, capacity, RollingHorizon.Default, at);

    public static string WalksPastWideOperands(int choice)
    {
        const long Far = 9_007_199_254_740_993L;

        return choice switch
        {
            0 => "zero",
            1 => "one",
            2 => "two",
            3 => "three",
            4 => "four",
            5 => "five",
            6 => Far.ToString(CultureInfo.InvariantCulture),
            7 => nameof(WalksPastWideOperands),
            _ => BeyondTheWideOperands(),
        };
    }

    public static string BeyondTheWideOperands() => "the census reaches a call that sits past a jump table";

    public static void MovesAReservationThroughAMethodGroup(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        Action move = reservation.Contend;

        move();
    }

    public static void MovesAReservationThroughReflection(Reservation reservation)
    {
        typeof(Reservation)
            .GetMethod(nameof(Reservation.Secure))!
            .Invoke(reservation, null);
    }

    public static void NothingCallsThis()
    {
    }
}
