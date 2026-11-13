using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationAsksForAnEncodeTests
{
    private static readonly DateTime Noon = new(2026, 9, 17, 3, 0, 0, DateTimeKind.Utc);

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
