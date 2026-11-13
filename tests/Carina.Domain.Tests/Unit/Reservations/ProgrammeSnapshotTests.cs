using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Tests.Unit.Recordings;

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

    [Fact(DisplayName = "a recording keeps the sound the guide announces when it begins, and what the reservation kept where the guide is still silent")]
    public void ARecordingKeepsTheSoundTheGuideAnnounces()
    {
        ProgrammeSnapshot kept = RecordingFactory.Snapshot(AudioMode.Stereo, 1);

        ProgrammeSnapshot both = kept.HeardFrom(Announced(AudioMode.DualMono, 2));
        ProgrammeSnapshot silent = kept.HeardFrom(Announced(AudioMode.Undetermined, ProgrammeSnapshot.SoundsUnannounced));
        ProgrammeSnapshot audioOnly = kept.HeardFrom(Announced(AudioMode.Surround, ProgrammeSnapshot.SoundsUnannounced));

        Assert.Equal((AudioMode.DualMono, 2), (both.Audio, both.Sounds));
        Assert.Same(kept, silent);
        Assert.Equal((AudioMode.Surround, 1), (audioOnly.Audio, audioOnly.Sounds));
        Assert.Equal(
            (kept.Name, kept.Summary, kept.Extended, kept.Genres, kept.CapturedAt),
            (both.Name, both.Summary, both.Extended, both.Genres, both.CapturedAt));
    }

    [Fact(DisplayName = "a guide that announces the sound the reservation already kept leaves the snapshot as it was")]
    public void AGuideThatSaysTheSameLeavesTheSnapshotAsItWas()
    {
        ProgrammeSnapshot kept = RecordingFactory.Snapshot(AudioMode.Stereo, 1);

        Assert.Same(kept, kept.HeardFrom(Announced(AudioMode.Stereo, 1)));
    }

    private static Programme Announced(AudioMode audio, int sounds)
    {
        ProgrammeRef programme = RecordingFactory.Programme();

        return Programme.Rehydrate(
            programme.Id,
            new TransportStreamId(32736),
            programme.StartsAt,
            Now.AddMinutes(30),
            "A programme",
            string.Empty,
            false,
            Now,
            audio: audio,
            sounds: sounds);
    }
}
