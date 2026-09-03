using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationOutcomeJudgementTests
{
    private static readonly DateTime Opens = new(2026, 8, 24, 20, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    private static readonly Margin Before = Margin.OfSeconds(60);

    private static readonly Margin After = Margin.OfSeconds(120);

    private static readonly DateTime GraceRunsOut = Opens - Before.Value + Grace;

    private static readonly DateTime LongWindowCloses = Opens + TimeSpan.FromHours(1) + After.Value;

    public static TheoryData<ReservationState, bool, bool, RecordingOutcome?, ReservationOutcomeKind?>
        WhatEachStandingIs =>
        new()
        {
            { ReservationState.Scheduled, false, false, null, ReservationOutcomeKind.Missed },
            { ReservationState.Conflict, false, false, null, ReservationOutcomeKind.Competing },
            { ReservationState.Cancelled, false, false, null, null },
            { ReservationState.Missed, false, false, null, null },
            { ReservationState.Scheduled, true, true, null, null },
            { ReservationState.Scheduled, true, false, null, ReservationOutcomeKind.Missed },
            { ReservationState.Conflict, true, false, null, ReservationOutcomeKind.Competing },
            { ReservationState.Scheduled, true, true, RecordingOutcome.Complete, null },
            { ReservationState.Scheduled, true, false, RecordingOutcome.Complete, null },
            { ReservationState.Scheduled, true, true, RecordingOutcome.Truncated, null },
            { ReservationState.Scheduled, true, false, RecordingOutcome.Truncated, null },
            {
                ReservationState.Scheduled, true, true, RecordingOutcome.Failed,
                ReservationOutcomeKind.RecordingFailure
            },
            {
                ReservationState.Conflict, true, true, RecordingOutcome.Failed,
                ReservationOutcomeKind.RecordingFailure
            },
        };

    public static TheoryData<int, ReservationOutcomeKind?> AroundTheGraceOfAProgrammeShorterThanIt =>
        new()
        {
            { -1, null },
            { 0, ReservationOutcomeKind.Missed },
            { 1, ReservationOutcomeKind.Missed },
        };

    public static TheoryData<int, ReservationOutcomeKind?> AroundTheCloseOfAProgrammeLongerThanTheGrace =>
        new()
        {
            { -1, null },
            { 0, ReservationOutcomeKind.Missed },
            { 1, ReservationOutcomeKind.Missed },
        };

    [Theory]
    [MemberData(nameof(WhatEachStandingIs))]
    public void EveryStandingAReservationCanBeInIsAnswered(
        ReservationState state,
        bool claimed,
        bool recorded,
        RecordingOutcome? outcome,
        ReservationOutcomeKind? expected)
        => Assert.Equal(
            expected,
            ReservationOutcomeJudgement.Of(
                Held(state, claimed ? Opens : null, outcome),
                recorded,
                Grace,
                LongWindowCloses));

    [Theory]
    [MemberData(nameof(AroundTheGraceOfAProgrammeShorterThanIt))]
    public void TheGraceIsWhatDecidesAProgrammeThatEndsBeforeItRunsOut(int seconds, ReservationOutcomeKind? expected)
    {
        Reservation reservation = Held(ReservationState.Scheduled, null, null, TimeSpan.FromMinutes(2));

        Assert.True(reservation.EffectiveEndAt < GraceRunsOut);
        Assert.Equal(
            expected,
            ReservationOutcomeJudgement.Of(reservation, false, Grace, GraceRunsOut.AddSeconds(seconds)));
    }

    [Theory]
    [MemberData(nameof(AroundTheCloseOfAProgrammeLongerThanTheGrace))]
    public void TheCloseOfTheWindowIsWhatDecidesAProgrammeThatOutlastsTheGrace(
        int seconds,
        ReservationOutcomeKind? expected)
    {
        Reservation reservation = Held(ReservationState.Scheduled, null, null);

        Assert.True(GraceRunsOut < reservation.EffectiveEndAt);
        Assert.Equal(
            expected,
            ReservationOutcomeJudgement.Of(reservation, false, Grace, LongWindowCloses.AddSeconds(seconds)));
    }

    [Fact]
    public void AReservationStillInsideItsWindowIsLeftForTheRecorderToTry()
    {
        Reservation reservation = Held(ReservationState.Scheduled, null, null);

        Assert.Null(ReservationOutcomeJudgement.Of(reservation, false, Grace, GraceRunsOut));
        Assert.Null(ReservationOutcomeJudgement.Of(reservation, false, Grace, LongWindowCloses.AddSeconds(-1)));
        Assert.Equal(
            ReservationOutcomeKind.Missed,
            ReservationOutcomeJudgement.Of(reservation, false, Grace, LongWindowCloses));
    }

    [Fact]
    public void AContendedReservationStillInsideItsWindowIsLeftAloneToo()
    {
        Reservation reservation = Held(ReservationState.Conflict, null, null);

        Assert.Null(ReservationOutcomeJudgement.Of(reservation, false, Grace, LongWindowCloses.AddSeconds(-1)));
        Assert.Equal(
            ReservationOutcomeKind.Competing,
            ReservationOutcomeJudgement.Of(reservation, false, Grace, LongWindowCloses));
    }

    [Fact]
    public void AGraceLongEnoughToOutlastTheWindowHoldsTheAnswerBack()
    {
        Reservation reservation = Held(ReservationState.Scheduled, null, null);
        TimeSpan longer = LongWindowCloses - reservation.EffectiveStartAt + TimeSpan.FromMinutes(1);

        Assert.Null(ReservationOutcomeJudgement.Of(reservation, false, longer, LongWindowCloses));
        Assert.Equal(
            ReservationOutcomeKind.Missed,
            ReservationOutcomeJudgement.Of(reservation, false, longer, reservation.EffectiveStartAt + longer));
    }

    [Fact]
    public void AClaimWithNoRecordingBehindItIsSettledOnceTheWindowCloses()
    {
        Reservation reservation = Held(ReservationState.Scheduled, Opens, null);

        Assert.Null(ReservationOutcomeJudgement.Of(reservation, false, Grace, LongWindowCloses.AddSeconds(-1)));
        Assert.Equal(
            ReservationOutcomeKind.Missed,
            ReservationOutcomeJudgement.Of(reservation, false, Grace, LongWindowCloses));
    }

    [Fact]
    public void AClaimWithARecordingBehindItIsLeftToThatRecordingHoweverLongItTakes()
    {
        Reservation reservation = Held(ReservationState.Scheduled, Opens, null);

        Assert.Null(ReservationOutcomeJudgement.Of(reservation, true, Grace, LongWindowCloses));
        Assert.Null(
            ReservationOutcomeJudgement.Of(reservation, true, Grace, LongWindowCloses + TimeSpan.FromDays(30)));
    }

    [Fact]
    public void TheKindsThisJudgementReachesAreNamed()
    {
        List<ReservationOutcomeKind> reached =
        [
            .. WhatEachStandingIs
                .Select(row => (ReservationOutcomeKind?)row[4])
                .Where(kind => kind is not null)
                .Select(kind => kind!.Value)
                .Distinct()
                .Order(),
        ];

        Assert.Equal(
            [
                ReservationOutcomeKind.Competing,
                ReservationOutcomeKind.Missed,
                ReservationOutcomeKind.RecordingFailure,
            ],
            reached);
    }

    [Fact]
    public void TheJudgementIsHandedAReservation()
        => Assert.Throws<ArgumentNullException>(
            () => ReservationOutcomeJudgement.Of(null!, false, Grace, LongWindowCloses));

    private static Reservation Held(
        ReservationState state,
        DateTime? startedAt,
        RecordingOutcome? outcome,
        TimeSpan? length = null)
    {
        var programme = new ProgrammeRef(
            new NetworkId(32736),
            new ServiceId(1024),
            new EventId(4001),
            Opens);

        return Reservation.Rehydrate(
            ReservationId.New(),
            programme,
            null,
            Priority.Default,
            Opens,
            Opens + (length ?? TimeSpan.FromHours(1)),
            true,
            Before,
            length is null ? After : Margin.None,
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Opens),
            null,
            BroadcastGroupRole.Standalone,
            state,
            startedAt,
            outcome,
            false,
            [],
            false,
            null,
            false,
            null,
            Opens);
    }
}
