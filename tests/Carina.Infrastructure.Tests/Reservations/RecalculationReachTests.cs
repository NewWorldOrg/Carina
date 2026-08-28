using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;

namespace Carina.Infrastructure.Tests.Reservations;

public sealed class RecalculationReachTests
{
    public static TheoryData<RecalculationTrigger, RecalculationReach> WhatEachTriggerReaches => new()
    {
        { RecalculationTrigger.ReservationChanged, RecalculationReach.Nothing },
        { RecalculationTrigger.TunerFaulted, RecalculationReach.Nothing },
        { RecalculationTrigger.SelectedChannelChanged, RecalculationReach.Settle },
        { RecalculationTrigger.TunerConfigurationChanged, RecalculationReach.Settle },
        { RecalculationTrigger.RecordingStarted, RecalculationReach.Settle },
        { RecalculationTrigger.RecordingExtended, RecalculationReach.Settle },
        { RecalculationTrigger.RecordingEnded, RecalculationReach.Settle },
        { RecalculationTrigger.ProgrammesChanged, RecalculationReach.Increment },
        { RecalculationTrigger.RulesChanged, RecalculationReach.Everything },
        { RecalculationTrigger.PeriodicReconciliation, RecalculationReach.Everything },
        { RecalculationTrigger.AppStarted, RecalculationReach.Everything },
    };

    [Fact]
    public void TheTriggersAreTheOnesTheNormNamesAndTheyAreNamedRatherThanCounted()
    {
        Assert.Equal(
            [
                RecalculationTrigger.ReservationChanged,
                RecalculationTrigger.RulesChanged,
                RecalculationTrigger.ProgrammesChanged,
                RecalculationTrigger.SelectedChannelChanged,
                RecalculationTrigger.TunerConfigurationChanged,
                RecalculationTrigger.TunerFaulted,
                RecalculationTrigger.RecordingStarted,
                RecalculationTrigger.RecordingExtended,
                RecalculationTrigger.RecordingEnded,
                RecalculationTrigger.PeriodicReconciliation,
                RecalculationTrigger.AppStarted,
            ],
            RecalculationReaches.Declared);
    }

    [Fact]
    public void EveryTriggerThatExistsSaysHowFarItReaches()
    {
        Assert.Equal(
            [.. Enum.GetValues<RecalculationTrigger>().Order()],
            RecalculationReaches.Declared);
    }

    [Theory]
    [MemberData(nameof(WhatEachTriggerReaches))]
    public void ATriggerReachesWhereItSays(RecalculationTrigger trigger, RecalculationReach reach)
        => Assert.Equal(reach, RecalculationReaches.Of(trigger));

    [Fact]
    public void ATriggerNothingDeclaredIsRefusedRatherThanReadAsReachingNothing()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => RecalculationReaches.Of((RecalculationTrigger)999));

    [Fact]
    public void APassAnsweringSeveralTriggersReachesAsFarAsTheWidestOfThem()
        => Assert.Equal(
            RecalculationReach.Increment,
            RecalculationReaches.Widest(
                [
                    RecalculationTrigger.ReservationChanged,
                    RecalculationTrigger.SelectedChannelChanged,
                    RecalculationTrigger.ProgrammesChanged,
                ]));

    [Fact]
    public void APassAnsweringOnlyTriggersThatChangeNothingReachesNothing()
        => Assert.Equal(
            RecalculationReach.Nothing,
            RecalculationReaches.Widest(
                [RecalculationTrigger.ReservationChanged, RecalculationTrigger.TunerFaulted]));

    [Fact]
    public void AFaultedSeatIsStillCountedWhichIsWhyTheTriggerThatSaysOneFaultedReachesNothing()
    {
        var capacity = new TunerCapacity(
            [new TunerSeat("first", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: true)],
            []);

        Assert.True(capacity.CanSeat(new Dictionary<TuneSystem, int> { [TuneSystem.IsdbT] = 1 }));
        Assert.Equal(RecalculationReach.Nothing, RecalculationReaches.Of(RecalculationTrigger.TunerFaulted));
    }
}
