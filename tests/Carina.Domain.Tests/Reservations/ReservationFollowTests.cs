using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationFollowTests
{
    private static readonly DateTime Later = ReservationFactory.Now.AddMinutes(5);

    [Fact]
    public void AReservationMovesOntoTheHourTheBroadcastIsAnnouncedAtNow()
    {
        Reservation booked = ReservationFactory.Planned();
        DateTime moved = booked.ProgrammeStartsAt.AddMinutes(30);

        booked.Follow(moved, moved.AddHours(1), true, Snapshot(), [Slipped(booked)]);

        Assert.Equal(moved, booked.StartAt);
        Assert.Equal(moved.AddHours(1), booked.EndAt);
        Assert.Equal(moved, booked.ProgrammeStartsAt);
        Assert.Equal(moved, booked.Programme.StartsAt);
    }

    [Fact]
    public void TheBroadcastKeepsTheIdentityItWasReservedUnderWhereverItMovesTo()
    {
        Reservation booked = ReservationFactory.Planned();
        ProgrammeRef before = booked.Programme;
        DateTime moved = booked.ProgrammeStartsAt.AddHours(3);

        booked.Follow(moved, moved.AddHours(1), true, Snapshot("その後の題名"), [Slipped(booked)]);

        Assert.Equal(before.NetworkId, booked.NetworkId);
        Assert.Equal(before.ServiceId, booked.ServiceId);
        Assert.Equal(before.EventId, booked.EventId);
        Assert.Equal("その後の題名", booked.SnapshotName);
    }

    [Fact]
    public void FollowingSaysWhatMovedAndPutsTheAcknowledgementBack()
    {
        Reservation booked = ReservationFactory.Planned();
        DateTime moved = booked.ProgrammeStartsAt.AddMinutes(30);
        EpgDivergence said = Slipped(booked);

        booked.Follow(moved, moved.AddHours(1), true, Snapshot(), [said]);
        booked.Acknowledge(Later);
        booked.Follow(moved.AddMinutes(30), moved.AddMinutes(90), true, Snapshot(), [said]);

        Assert.True(booked.EpgDiverged);
        Assert.Equal([said], booked.EpgDivergences);
        Assert.Null(booked.AcknowledgedAt);
    }

    [Fact]
    public void AReservationDoesNotMoveWithoutSayingWhatMoved()
    {
        Reservation booked = ReservationFactory.Planned();

        Assert.Throws<ArgumentException>(() => booked.Follow(
            booked.ProgrammeStartsAt.AddMinutes(30),
            booked.ProgrammeStartsAt.AddMinutes(90),
            true,
            Snapshot(),
            []));
    }

    [Fact]
    public void AReservationAlreadyHoldingATunerIsNotMovedUnderneathTheRecording()
    {
        Reservation recording = ReservationFactory.Claimed();

        Assert.Throws<InvalidOperationException>(() => recording.Follow(
            recording.ProgrammeStartsAt.AddMinutes(30),
            recording.ProgrammeStartsAt.AddMinutes(90),
            true,
            Snapshot(),
            [Slipped(recording)]));
    }

    [Fact]
    public void AReservationSomebodyCancelledIsNotMovedBackIntoTheRunning()
    {
        Reservation cancelled = ReservationFactory.Rehydrated(ReservationState.Cancelled, null, null);

        Assert.Throws<InvalidOperationException>(() => cancelled.Follow(
            cancelled.ProgrammeStartsAt.AddMinutes(30),
            cancelled.ProgrammeStartsAt.AddMinutes(90),
            true,
            Snapshot(),
            [Slipped(cancelled)]));
    }

    [Fact]
    public void ABroadcastThatVanishedIsMarkedAndTheAcknowledgementGoesBack()
    {
        Reservation booked = ReservationFactory.Planned();

        booked.Disappear();

        Assert.True(booked.EpgMissing);
        Assert.Null(booked.AcknowledgedAt);
    }

    private static EpgDivergence Slipped(Reservation reservation)
        => new(
            DivergedField.StartAt,
            reservation.ProgrammeStartsAt.ToString("O"),
            reservation.ProgrammeStartsAt.AddMinutes(30).ToString("O"),
            Later);

    private static ProgrammeSnapshot Snapshot(string name = "A programme")
        => new(name, "What it is about", string.Empty, [new ProgrammeGenre(7, 1)], Later);
}
