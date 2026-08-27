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

    public static void TakesAReservationOutOfTheRunningWithoutAskingTheScheduler(Reservation reservation)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        reservation.Cancel();
    }

    public static AllocationPlan PlansOnItsOwn(
        IReadOnlyList<AllocationCandidate> candidates,
        TunerCapacity capacity,
        DateTime at)
        => TunerAllocationPlanner.Plan(candidates, capacity, RollingHorizon.Default, at);

    public static string WalksPastWideOperands(int choice)
    {
        const long Far = 9_007_199_254_740_993L;
        string carried;

        switch (choice)
        {
            case 0:
                carried = "zero";

                break;

            case 1:
                carried = "one";

                break;

            case 2:
                carried = "two";

                break;

            case 3:
                carried = "three";

                break;

            case 4:
                carried = "four";

                break;

            case 5:
                carried = "five";

                break;

            case 6:
                carried = "six";

                break;

            case 7:
                carried = "seven";

                break;

            case 8:
                carried = "eight";

                break;

            case 9:
                carried = "nine";

                break;

            case 10:
                carried = "ten";

                break;

            case 11:
                carried = Far.ToString(CultureInfo.InvariantCulture);

                break;

            default:
                carried = BeyondTheWideOperands();

                break;
        }

        return carried;
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
