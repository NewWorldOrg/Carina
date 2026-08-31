using System.Reflection;

using Carina.Domain.Base;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Reservations;

namespace Carina.Conventions.Tests;

public sealed class AllocationEntryPointRuleTests
{
    private const string WhereItIsComputed =
        "Carina.Infrastructure.Reservations.ReservationSchedulingService.Weigh";

    private const string WhereItIsApplied =
        "Carina.Infrastructure.Reservations.ReservationSchedulingService.Apply";

    private const string WhereItIsRevised =
        "Carina.Infrastructure.Reservations.ReservationSchedulingService.Applied";

    private const string WhereItIsWrittenOff =
        "Carina.Infrastructure.Reservations.ReservationOutcomeService.RecordAsync";

    private static readonly IReadOnlyList<Assembly> Production =
    [
        typeof(Program).Assembly,
        typeof(CarinaDbContext).Assembly,
        typeof(CommonValueObject<>).Assembly,
    ];

    public static TheoryData<string> WhatMovesAReservationBetweenSecuredAndContended =>
    [
        nameof(Reservation.Secure),
        nameof(Reservation.Contend),
        nameof(Reservation.LoseReception),
        nameof(Reservation.RegainReception),
    ];

    public static TheoryData<string> WhatTakesAReservationOutOfTheRunningOrPutsItBack =>
    [
        nameof(Reservation.Cancel),
        nameof(Reservation.Restore),
        nameof(Reservation.Reprioritise),
        nameof(Reservation.Remargin),
    ];

    [Theory]
    [MemberData(nameof(WhatTakesAReservationOutOfTheRunningOrPutsItBack))]
    public void OnlyOnePlaceInTheApplicationRevisesAReservation(string move)
    {
        Assert.Equal([WhereItIsRevised], CallSiteCensus.CallersOf(Production, typeof(Reservation), move));
    }

    [Fact]
    public void OnlyOnePlaceInTheApplicationWritesAReservationOffAsNotRecorded()
        => Assert.Equal(
            [WhereItIsWrittenOff],
            CallSiteCensus.CallersOf(Production, typeof(Reservation), nameof(Reservation.Miss)));

    [Fact]
    public void OnlyOnePlaceInTheApplicationWorksOutWhatFitsOnTheTuners()
    {
        Assert.Equal(
            [WhereItIsComputed],
            CallSiteCensus.CallersOf(Production, typeof(TunerAllocationPlanner), nameof(TunerAllocationPlanner.Plan)));
    }

    [Theory]
    [MemberData(nameof(WhatMovesAReservationBetweenSecuredAndContended))]
    public void OnlyOnePlaceInTheApplicationMovesAReservationOnWhatItWorkedOut(string move)
    {
        Assert.Equal([WhereItIsApplied], CallSiteCensus.CallersOf(Production, typeof(Reservation), move));
    }

    [Fact]
    public void EveryWayInReachesThatOnePlace()
    {
        IReadOnlyList<string> intoTheCalculation =
            CallSiteCensus.CallersOf(Production, typeof(ReservationSchedulingService), "Weigh");

        Assert.Equal(
            [
                "Carina.Infrastructure.Reservations.ReservationSchedulingService.PreviewAsync",
                "Carina.Infrastructure.Reservations.ReservationSchedulingService.SettleAsync",
            ],
            intoTheCalculation);

        Assert.Equal(
            [
                "Carina.Infrastructure.Reservations.ReservationSchedulingService.PreviewAsync",
                "Carina.Infrastructure.Reservations.ReservationSchedulingService.SettleAsync",
            ],
            CallSiteCensus.CallersOf(Production, typeof(ReservationSchedulingService), "ResolveAsync"));

        Assert.Equal(
            ["Carina.Infrastructure.Reservations.ReservationSchedulingService.SettleAsync"],
            CallSiteCensus.CallersOf(Production, typeof(ReservationSchedulingService), "Apply"));

        Assert.Equal(
            ["Carina.Infrastructure.Reservations.ReservationSchedulingService.SettleAsync"],
            CallSiteCensus.CallersOf(Production, typeof(ReservationSchedulingService), "Applied"));

        Assert.Equal(
            [
                "Carina.Infrastructure.Reservations.ReservationSchedulingService.CreateAsync",
                "Carina.Infrastructure.Reservations.ReservationSchedulingService.RecalculateAsync",
                "Carina.Infrastructure.Reservations.ReservationSchedulingService.ReviseAsync",
            ],
            CallSiteCensus.CallersOf(Production, typeof(ReservationSchedulingService), "SettleAsync"));
    }

    [Fact]
    public void TheCensusReadsTheAssembliesTheApplicationIsMadeOf()
    {
        Assert.Equal(
            ["Carina.Api", "Carina.Domain", "Carina.Infrastructure"],
            Production.Select(assembly => assembly.GetName().Name!).Order(StringComparer.Ordinal));

        Assert.True(CallSiteCensus.MethodsRead(Production) > 0);
    }
}
