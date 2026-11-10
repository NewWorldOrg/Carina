using System.Reflection;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Notifications;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Reservations;

namespace Carina.Conventions.Tests;

public sealed class ProgrammeWatchApartRuleTests
{
    private static readonly IReadOnlyList<Assembly> Production =
    [
        typeof(Program).Assembly,
        typeof(CarinaDbContext).Assembly,
        typeof(CommonValueObject<>).Assembly,
    ];

    private static readonly HashSet<Type> ReservationsAndAllocation =
        [.. TypeMentionCensus.TypesUnder(Production, IsOfReservationsOrAllocation)];

    private static readonly HashSet<Type> ProgrammesAskedToBeToldOf =
        [.. TypeMentionCensus.TypesUnder(Production, IsOfAProgrammeAskedToBeToldOf)];

    [Fact(DisplayName = "BR-ND-009: nothing of the reservations or of the plan that hands out tuners mentions a programme asked to be told of")]
    public void NothingOfTheReservationsOrTheAllocationMentionsAProgrammeAskedToBeToldOf()
    {
        Assert.Empty(TypeMentionCensus.MentionsOf(ReservationsAndAllocation, ProgrammesAskedToBeToldOf));
    }

    [Fact(DisplayName = "BR-ND-009: the rule reads the reservations, the plan and the capacity, and knows the programme asked to be told of by its row, its ledger and its look")]
    public void TheRuleReadsTheTypesItIsAbout()
    {
        Assert.Superset(
            new HashSet<Type>
            {
                typeof(Reservation),
                typeof(TunerAllocationPlanner),
                typeof(AllocationPlan),
                typeof(TunerCapacity),
                typeof(ReservationSchedulingService),
                typeof(ReservationOutcomeService),
                typeof(ReservationRepository),
                typeof(ReservationWrites),
            },
            ReservationsAndAllocation);

        Assert.Superset(
            new HashSet<Type>
            {
                typeof(ProgrammeWatch),
                typeof(ProgrammeWatchId),
                typeof(ProgrammeWatchLook),
                typeof(IProgrammeWatchRepository),
                typeof(ProgrammeWatchRepository),
                typeof(ProgrammeWatchRound),
            },
            ProgrammesAskedToBeToldOf);
    }

    private static bool IsOfReservationsOrAllocation(Type type)
        => type.Namespace?.EndsWith(".Reservations", StringComparison.Ordinal) is true
           || type.Name.StartsWith("Reservation", StringComparison.Ordinal)
           || type.Name.Contains("Allocation", StringComparison.Ordinal)
           || type.Name.StartsWith("TunerCapacity", StringComparison.Ordinal);

    private static bool IsOfAProgrammeAskedToBeToldOf(Type type)
        => type.Name.Contains("ProgrammeWatch", StringComparison.Ordinal)
           || type.Name.Contains("NotificationWatch", StringComparison.Ordinal);
}
