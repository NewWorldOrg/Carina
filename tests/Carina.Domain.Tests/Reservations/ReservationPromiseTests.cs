using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationPromiseTests
{
    private static readonly DateTime Airs = new(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Ends = Airs.AddMinutes(200);

    private static readonly Margin Ahead = Margin.OfSeconds(120);

    private static readonly Margin Behind = Margin.OfSeconds(180);

    [Fact]
    public void ABroadcastAskedForASecondBeforeItStartsKeepsItsOwnStartAndLosesOnlyTheMargin()
    {
        DateTime asking = Airs.AddSeconds(-1);
        Reservation asked = Planned(asking);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(TimeSpan.FromSeconds(1), asked.MarginBefore.Value);
        Assert.Equal(asking, asked.EffectiveStartAt);
    }

    [Fact]
    public void ABroadcastAskedForAtTheInstantItStartsKeepsItsOwnStartAndLosesAllOfTheMargin()
    {
        Reservation asked = Planned(Airs);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(TimeSpan.Zero, asked.MarginBefore.Value);
        Assert.Equal(Airs, asked.EffectiveStartAt);
    }

    [Fact]
    public void ABroadcastAskedForASecondAfterItStartsIsPromisedFromTheMomentItWasAskedFor()
    {
        DateTime asking = Airs.AddSeconds(1);
        Reservation asked = Planned(asking);

        Assert.Equal(asking, asked.StartAt);
        Assert.Equal(TimeSpan.Zero, asked.MarginBefore.Value);
        Assert.Equal(asking, asked.EffectiveStartAt);
    }

    [Fact]
    public void ABroadcastFiftyMinutesUnderWayIsPromisedFromTheMomentItWasAskedFor()
    {
        DateTime asking = Airs.AddMinutes(50);
        Reservation asked = Planned(asking);

        Assert.Equal(asking, asked.StartAt);
        Assert.Equal(asking, asked.EffectiveStartAt);
    }

    [Fact]
    public void TheEndOfTheWindowIsWhereItWasWhateverTheClampDidToTheHead()
    {
        Reservation asked = Planned(Airs.AddMinutes(50));

        Assert.Equal(Ends, asked.EndAt);
        Assert.Equal(Behind.Value, asked.MarginAfter.Value);
        Assert.Equal(Ends + Behind.Value, asked.EffectiveEndAt);
    }

    [Fact]
    public void TheBroadcastGoesOnSayingWhenItReallyStarted()
    {
        Reservation asked = Planned(Airs.AddMinutes(50));

        Assert.Equal(Airs, asked.ProgrammeStartsAt);
        Assert.Equal(Airs, asked.Programme.StartsAt);
    }

    [Fact]
    public void ABroadcastAskedForASecondBeforeItEndsIsStillPromisedFromTheMomentItWasAskedFor()
    {
        DateTime asking = Ends.AddSeconds(-1);
        Reservation asked = Planned(asking);

        Assert.Equal(asking, asked.StartAt);
        Assert.Equal(asking, asked.EffectiveStartAt);
    }

    [Fact]
    public void ABroadcastAskedForAtTheInstantItEndsIsLeftAsItWasAsked()
    {
        Reservation asked = Planned(Ends);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Ahead.Value, asked.MarginBefore.Value);
    }

    [Fact]
    public void ABroadcastAskedForASecondAfterItEndsIsLeftAsItWasAsked()
    {
        Reservation asked = Planned(Ends.AddSeconds(1));

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Ahead.Value, asked.MarginBefore.Value);
    }

    [Fact]
    public void AMarginReachingToASecondAfterTheAskingIsKeptWhole()
    {
        Reservation asked = Planned(Airs - Ahead.Value - TimeSpan.FromSeconds(1));

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Ahead.Value, asked.MarginBefore.Value);
        Assert.Equal(Airs - Ahead.Value, asked.EffectiveStartAt);
    }

    [Fact]
    public void AMarginReachingExactlyToTheAskingIsKeptWhole()
    {
        Reservation asked = Planned(Airs - Ahead.Value);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Ahead.Value, asked.MarginBefore.Value);
        Assert.Equal(Airs - Ahead.Value, asked.EffectiveStartAt);
    }

    [Fact]
    public void AMarginReachingToASecondBeforeTheAskingIsTrimmedBackToIt()
    {
        DateTime asking = Airs - Ahead.Value + TimeSpan.FromSeconds(1);
        Reservation asked = Planned(asking);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Ahead.Value - TimeSpan.FromSeconds(1), asked.MarginBefore.Value);
        Assert.Equal(asking, asked.EffectiveStartAt);
    }

    [Fact]
    public void ATrimmedMarginLandsOnTheWholeSecondAfterTheAskingRatherThanBeforeIt()
    {
        DateTime asking = Airs - Ahead.Value + TimeSpan.FromMilliseconds(500);
        Reservation asked = Planned(asking);

        Assert.Equal(Airs, asked.StartAt);
        Assert.Equal(Airs - Ahead.Value + TimeSpan.FromSeconds(1), asked.EffectiveStartAt);
    }

    [Theory]
    [InlineData(-3601)]
    [InlineData(-121)]
    [InlineData(-120)]
    [InlineData(-119)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3000)]
    [InlineData(11999)]
    public void TheHeadOfTheWindowNeverReachesBackBeforeTheAskingWhileTheWindowIsStillOpen(int secondsFromTheStart)
    {
        DateTime asking = Airs.AddSeconds(secondsFromTheStart);

        Assert.True(
            Planned(asking).EffectiveStartAt >= asking,
            "a reservation promised a window that had already begun when it was made");
    }

    [Fact]
    public void AReservationBornOfARuleIsClampedJustAsOneMadeByHandIs()
    {
        DateTime asking = Airs.AddMinutes(50);
        Reservation byHand = Planned(asking);
        Reservation byRule = Planned(asking, RuleId.New());

        Assert.True(byRule.IsRuleBorn);
        Assert.Equal(byHand.StartAt, byRule.StartAt);
        Assert.Equal(byHand.MarginBefore.Value, byRule.MarginBefore.Value);
        Assert.Equal(byHand.EffectiveStartAt, byRule.EffectiveStartAt);
    }

    [Fact]
    public void AReservationReadBackKeepsThePromiseItWasMadeWithRatherThanBeingClampedAgain()
    {
        Reservation stored = Reservation.Rehydrate(
            ReservationId.New(),
            Programme(),
            null,
            Priority.Default,
            Airs,
            Ends,
            true,
            Ahead,
            Behind,
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            ReservationState.Scheduled,
            null,
            null,
            false,
            [],
            false,
            null,
            false,
            null,
            Airs.AddMinutes(50));

        Assert.Equal(Airs, stored.StartAt);
        Assert.Equal(Ahead.Value, stored.MarginBefore.Value);
        Assert.Equal(Airs - Ahead.Value, stored.EffectiveStartAt);
    }

    [Fact]
    public void APlanWithNoMarginAheadOfTheBroadcastIsRefusedEvenWhenTheClampWouldHaveDroppedIt()
    {
        Assert.Throws<ArgumentNullException>(() => Reservation.Plan(
            ReservationId.New(),
            Programme(),
            null,
            Priority.Default,
            Airs,
            Ends,
            true,
            null!,
            Behind,
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Airs.AddMinutes(50)));
    }

    [Theory]
    [InlineData("startAt")]
    [InlineData("endAt")]
    [InlineData("at")]
    public void APlanWhoseInstantsAreNotAllInUtcIsRefusedRatherThanClamped(string parameter)
    {
        DateTime local = DateTime.SpecifyKind(
            parameter switch
            {
                "startAt" => Airs,
                "endAt" => Ends,
                _ => Airs.AddMinutes(50),
            },
            DateTimeKind.Local);

        Assert.Throws<ArgumentException>(() => Planned(
            parameter is "at" ? local : Airs.AddMinutes(50),
            startAt: parameter is "startAt" ? local : Airs,
            endAt: parameter is "endAt" ? local : Ends));
    }

    private static Reservation Planned(
        DateTime at,
        RuleId? ruleId = null,
        DateTime? startAt = null,
        DateTime? endAt = null)
        => Reservation.Plan(
            ReservationId.New(),
            Programme(),
            ruleId,
            Priority.Default,
            startAt ?? Airs,
            endAt ?? Ends,
            true,
            Ahead,
            Behind,
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            at);

    private static ProgrammeRef Programme()
        => new(new NetworkId(32736), new ServiceId(1024), new EventId(4001), Airs);

    private static ProgrammeSnapshot Snapshot()
        => new("A programme", "What it is about", string.Empty, [new ProgrammeGenre(7, 1)], Airs.AddHours(-6));
}
