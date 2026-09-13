using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ProgrammeSnapshotSoundTests
{
    private static readonly DateTime Now = ReservationFactory.Now;

    [Fact]
    public void ASnapshotCarriesTheSoundTheBroadcastAnnounced()
    {
        var snapshot = new ProgrammeSnapshot(
            "A programme",
            "What it is about",
            string.Empty,
            [],
            Now,
            AudioMode.DualMono,
            2);

        Assert.Equal(AudioMode.DualMono, snapshot.Audio);
        Assert.Equal(2, snapshot.Sounds);
    }

    [Fact]
    public void ASnapshotOfABroadcastThatAnnouncedNoSoundSaysSoRatherThanGuessing()
    {
        var snapshot = new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Now);

        Assert.Equal(AudioMode.Undetermined, snapshot.Audio);
        Assert.Equal(ProgrammeSnapshot.SoundsUnannounced, snapshot.Sounds);
        Assert.Equal(0, ProgrammeSnapshot.SoundsUnannounced);
    }

    [Fact]
    public void ACountOfSoundsBelowNothingIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Now, AudioMode.Stereo, -1));

    [Fact]
    public void TheSoundReachesTheSnapshotTakenFromWhatTheGuideSaid()
    {
        ProgrammeSnapshot snapshot = ProgrammeSnapshot.Of(
            "A programme",
            "What it is about",
            [new ProgrammeItem("Cast", "Who is in it")],
            [new ProgrammeGenre(7, 1)],
            Now,
            AudioMode.Surround,
            3);

        Assert.Equal(AudioMode.Surround, snapshot.Audio);
        Assert.Equal(3, snapshot.Sounds);
    }

    [Fact]
    public void AReservationKeepsTheSoundItsSnapshotWasTakenWith()
    {
        Reservation reservation = Reservation.Plan(
            ReservationId.New(),
            ReservationFactory.Programme(),
            null,
            Priority.Default,
            Now.AddHours(2),
            Now.AddHours(3),
            true,
            Margin.None,
            Margin.None,
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Now, AudioMode.DualMono, 2),
            null,
            BroadcastGroupRole.Standalone,
            Now);

        Assert.Equal(AudioMode.DualMono, reservation.SnapshotAudio);
        Assert.Equal(2, reservation.SnapshotSounds);
    }

    [Fact]
    public void AReservationThatFollowsItsProgrammeTakesTheSoundTheGuideNowAnnounces()
    {
        Reservation reservation = ReservationFactory.Planned();

        reservation.Follow(
            Now.AddHours(3),
            Now.AddHours(4),
            true,
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Now, AudioMode.DualMono, 2),
            [
                new EpgDivergence(
                    DivergedField.StartAt,
                    reservation.ProgrammeStartsAt.ToString("O"),
                    Now.AddHours(3).ToString("O"),
                    Now),
            ]);

        Assert.Equal(AudioMode.DualMono, reservation.SnapshotAudio);
        Assert.Equal(2, reservation.SnapshotSounds);
    }
}
