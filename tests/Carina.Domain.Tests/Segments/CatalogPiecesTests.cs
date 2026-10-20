using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// A synthetic weekly series of half an hour whose first episode is recorded twice, the second recording starting a
/// few seconds after the first.
/// </summary>
public sealed class CatalogPiecesTests
{
    private static readonly DateTime Evening = new(2026, 10, 5, 13, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Lead = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Runs = TimeSpan.FromMinutes(30);

    private static readonly DateTime Now = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly Episode first = new(Programme(1, "架空の物語", Evening, Lead), RecordingId.New());

    private readonly Episode again = new(Programme(1, null, Evening, Lead - TimeSpan.FromSeconds(4)), RecordingId.New());

    private readonly Episode second = new(Programme(1, "架空の物語", Evening.AddDays(7), Lead), RecordingId.New());

    [Fact(DisplayName = "BR-CD-005: a sound heard only in a broadcast recorded twice is heard in one episode, so it is neither an ending nor a programme fixture")]
    public void ASoundOfABroadcastRecordedTwiceIsHeardInOneEpisode()
    {
        Sound preview = Heard(34, (first, 1700), (again, 1700));
        Sound fixture = Heard(12, (first, 300), (again, 300));
        Sound ending = Heard(34, (first, 1700), (again, 1700), (second, 1700));

        Assert.Equal(CatalogEstimate.Unknown, Own(preview).Estimate);
        Assert.Equal(CatalogEstimate.Unknown, Own(fixture).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Ending, 0.8, CatalogDecision.Pattern), Own(ending).Estimate);
        Assert.Equal(1, Own(ending).SeriesCount);
    }

    private static ProgrammeCopy Programme(int station, string? series, DateTime starts, TimeSpan lead)
        => new(
            new NetworkId(station),
            new ServiceId(station),
            starts,
            starts + Runs,
            starts - lead,
            series ?? "架空の物語🈑",
            [],
            [],
            null,
            AudioMode.Stereo,
            series);

    private static PlacedOccurrence Stretch(CatalogEntryId entry, ProgrammeCopy programme, RecordingId recording, double fromProgrammeStart, double seconds)
    {
        TimeSpan starts = programme.ProgrammeStartsAt - programme.RecordingStartedAt + TimeSpan.FromSeconds(fromProgrammeStart);
        SoundSpan span = new(starts, starts + TimeSpan.FromSeconds(seconds));

        return PlacedOccurrence.Of(CatalogOccurrence.Found(entry, recording, span, programme, 0.05, Now), programme);
    }

    private static CatalogPattern Own(Sound sound) => CatalogKinds.Estimate(sound.Length, sound.Placed, SeriesLinks.None);

    private static Sound Heard(double seconds, params (Episode Episode, double FromProgrammeStart)[] places)
    {
        CatalogEntryId entry = CatalogEntryId.New();

        return new Sound(
            TimeSpan.FromSeconds(seconds),
            [.. places.Select(place => Stretch(entry, place.Episode.Programme, place.Episode.Recording, place.FromProgrammeStart, seconds))]);
    }

    private sealed record Episode(ProgrammeCopy Programme, RecordingId Recording);

    private sealed record Sound(TimeSpan Length, PlacedOccurrence[] Placed);
}
