using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;

namespace Carina.Domain.Tests.Encodings;

public sealed class WhetherAnEncodeWasAskedForTests
{
    private static readonly DateTime Noon = new(2026, 9, 17, 3, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a rule that says nothing about encoding asks for one, which is what every rule did before one could say otherwise")]
    public void ARuleThatSaysNothingAsksForAnEncode() => Assert.True(Drafted().EncodeWhenRecorded);

    [Fact(DisplayName = "a rule can be drafted asking for no encode")]
    public void ARuleCanBeDraftedAskingForNoEncode()
        => Assert.False(Drafted(encodeWhenRecorded: false).EncodeWhenRecorded);

    [Fact(DisplayName = "rewriting a rule keeps whatever the rewrite asked for rather than the priority or the margins deciding it")]
    public void RewritingARuleKeepsWhatTheRewriteAskedFor()
    {
        Rule rule = Drafted();

        rule.Rewrite("Renamed", new RuleQuery("genre=9"), Priority.Default, Margin.None, Margin.None, false);

        Assert.False(rule.EncodeWhenRecorded);

        rule.Rewrite("Renamed", new RuleQuery("genre=9"), Priority.Default, Margin.None, Margin.None, true);

        Assert.True(rule.EncodeWhenRecorded);
    }

    [Fact(DisplayName = "a reservation that says nothing about encoding asks for one")]
    public void AReservationThatSaysNothingAsksForAnEncode() => Assert.True(Planned().EncodeWhenRecorded);

    [Fact(DisplayName = "a reservation can be planned asking for no encode")]
    public void AReservationCanBePlannedAskingForNoEncode()
        => Assert.False(Planned(encodeWhenRecorded: false).EncodeWhenRecorded);

    [Fact(DisplayName = "what a reservation asks for is changed without touching anything else about it")]
    public void WhatAReservationAsksForIsChangedOnItsOwn()
    {
        Reservation reservation = Planned();

        reservation.Rewish(false);

        Assert.False(reservation.EncodeWhenRecorded);
        Assert.Equal(Priority.Default, reservation.Priority);
        Assert.Equal(Margin.None, reservation.MarginBefore);
        Assert.Equal(ReservationState.Scheduled, reservation.State);

        reservation.Rewish(true);

        Assert.True(reservation.EncodeWhenRecorded);
    }

    [Fact(DisplayName = "a recording that says nothing about encoding asks for one")]
    public void ARecordingThatSaysNothingAsksForAnEncode() => Assert.True(Begun().EncodeWhenRecorded);

    [Fact(DisplayName = "a recording carries what the reservation it was started for asked, because nothing ties the two rows together afterwards")]
    public void ARecordingCarriesWhatItWasStartedWith()
        => Assert.False(Begun(encodeWhenRecorded: false).EncodeWhenRecorded);

    private static Rule Drafted(bool encodeWhenRecorded = true)
        => Rule.Draft(
            RuleId.New(),
            "a rule",
            new RuleQuery("keyword=hill"),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Noon,
            encodeWhenRecorded);

    private static Reservation Planned(bool encodeWhenRecorded = true)
        => Reservation.Plan(
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), Noon.AddHours(2)),
            null,
            Priority.Default,
            Noon.AddHours(2),
            Noon.AddHours(3),
            true,
            Margin.None,
            Margin.None,
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Noon,
            encodeWhenRecorded);

    private static Recording Begun(bool encodeWhenRecorded = true)
    {
        var id = RecordingId.New();

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), Noon.AddHours(2)),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".m2ts"),
            Noon.AddHours(2),
            Noon.AddHours(3),
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Noon.AddHours(2),
            null,
            encodeWhenRecorded);
    }

    private static ProgrammeSnapshot Snapshot()
        => new(
            "A programme",
            "What it is about",
            string.Empty,
            [],
            Noon,
            AudioMode.Undetermined,
            ProgrammeSnapshot.SoundsUnannounced);
}
