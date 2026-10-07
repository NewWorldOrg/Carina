using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class ProgrammeCopyTests
{
    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string New = char.ConvertFromUtf32(0x1F21F);

    private static readonly string Captioned = char.ConvertFromUtf32(0x1F211);

    [Fact(DisplayName = "the copy of the programme is taken from the recording's own row")]
    public void TheCopyIsTakenFromTheRecordingsOwnRow()
    {
        Recording recording = Recorded(
            new ProgrammeSnapshot(
                $"架空の番組 第1話{New}",
                $"架空のあらすじ{Captioned}",
                "架空の詳しい説明",
                [new ProgrammeGenre(7, 0), new ProgrammeGenre(7, 15)],
                Noon,
                AudioMode.DualMono,
                2));

        ProgrammeCopy copy = ProgrammeCopy.Of(recording, Noon.AddMinutes(30));

        Assert.Equal(new NetworkId(40_001), copy.NetworkId);
        Assert.Equal(new ServiceId(60_001), copy.ServiceId);
        Assert.Equal(Noon, copy.ProgrammeStartsAt);
        Assert.Equal(Noon.AddMinutes(30), copy.ProgrammeEndsAt);
        Assert.Equal(Noon.AddMinutes(-1), copy.RecordingStartedAt);
        Assert.Equal($"架空の番組 第1話{New}", copy.Name);
        Assert.Equal([new ProgrammeGenre(7, 0), new ProgrammeGenre(7, 15)], copy.Genres);
        Assert.Equal([ProgrammeMark.Captioned, ProgrammeMark.New], copy.Marks);
        Assert.Null(copy.Episode);
        Assert.Equal(AudioMode.DualMono, copy.Audio);
        Assert.Null(copy.SeriesName);
    }

    [Fact(DisplayName = "a programme whose end is not known is copied without one")]
    public void AProgrammeWhoseEndIsNotKnownIsCopiedWithoutOne()
    {
        ProgrammeCopy copy = ProgrammeCopy.Of(Recorded(Snapshot("架空の番組")), null);

        Assert.Null(copy.ProgrammeEndsAt);
        Assert.Empty(copy.Marks);
    }

    [Fact(DisplayName = "a programme that ends no later than it starts is refused")]
    public void AProgrammeThatEndsNoLaterThanItStartsIsRefused()
    {
        Recording recording = Recorded(Snapshot("架空の番組"));

        Assert.Throws<ArgumentException>(() => ProgrammeCopy.Of(recording, Noon));
    }

    [Fact(DisplayName = "an episode is counted from zero, and a series name is no longer than a programme's name")]
    public void AnEpisodeIsCountedFromZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Copy(episode: -1, seriesName: null));
        Assert.Throws<ArgumentException>(() => Copy(episode: 1, seriesName: new string('x', Reservation.NameMaxLength + 1)));

        ProgrammeCopy copy = Copy(episode: 0, seriesName: "架空のシリーズ");

        Assert.Equal(0, copy.Episode);
        Assert.Equal("架空のシリーズ", copy.SeriesName);
    }

    [Fact(DisplayName = "the times a copy holds are in UTC")]
    public void TheTimesACopyHoldsAreInUtc()
    {
        DateTime local = DateTime.SpecifyKind(Noon, DateTimeKind.Local);

        Assert.Throws<ArgumentException>(
            () => new ProgrammeCopy(
                new NetworkId(40_001),
                new ServiceId(60_001),
                local,
                null,
                Noon,
                "架空の番組",
                [],
                [],
                null,
                AudioMode.Stereo,
                null));
    }

    private static ProgrammeCopy Copy(int? episode, string? seriesName)
        => new(
            new NetworkId(40_001),
            new ServiceId(60_001),
            Noon,
            Noon.AddMinutes(30),
            Noon,
            "架空の番組",
            [],
            [],
            episode,
            AudioMode.Stereo,
            seriesName);

    private static ProgrammeSnapshot Snapshot(string name)
        => new(name, string.Empty, string.Empty, [], Noon, AudioMode.Stereo, ProgrammeSnapshot.SoundsUnannounced);

    private static Recording Recorded(ProgrammeSnapshot snapshot)
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(40_001), new ServiceId(60_001), new EventId(7_001), Noon),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Noon.AddMinutes(-1),
            Noon.AddMinutes(31),
            snapshot,
            null,
            BroadcastGroupRole.Standalone,
            Noon.AddMinutes(-1));
    }
}
