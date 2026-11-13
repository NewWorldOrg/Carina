using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class ProgrammeSnapshotTests
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
        var snapshot = new ProgrammeSnapshot(
            "A programme",
            string.Empty,
            string.Empty,
            [],
            Now,
            AudioMode.Undetermined,
            ProgrammeSnapshot.SoundsUnannounced);

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
}
