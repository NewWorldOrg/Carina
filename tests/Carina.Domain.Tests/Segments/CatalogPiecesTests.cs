using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// A synthetic weekly series of half an hour whose opening is matched in pieces: a long head, a short phrase that
/// the programme plays again in its middle, and a slot of 30 seconds on the grid. The first episode is recorded
/// twice, the second recording starting a few seconds after the first.
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

    private Episode[] Weeks => [first, again, second];

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

    [Fact(DisplayName = "BR-CD-005: the pieces an opening is matched in, heard right after its head at the same place in two episodes, are that opening, 0.8")]
    public void ThePiecesOfAnOpeningAreThatOpening()
    {
        Sound head = Heard(50, Every(0));
        Sound phrase = Heard(13, [.. Every(50), .. Every(600)]);
        Sound slot = Heard(30, Every(63));
        Sound once = Heard(20, (first, 93), (again, 93), (second, 400));
        CatalogSurroundings around = Around(head, phrase, slot, once);

        CatalogPattern phrased = Carried(phrase, around);
        CatalogPattern slotted = Carried(slot, around);

        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Pattern), Own(head).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.ProgrammeFixture, 0.7, CatalogDecision.Pattern), Own(phrase).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.5, CatalogDecision.Pattern), Own(slot).Estimate);
        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Continuation), phrased.Estimate);
        Assert.Equal([null, null, null, SegmentKind.Programme, SegmentKind.Programme, SegmentKind.Programme], phrased.Overrides);
        Assert.Equal(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Continuation), slotted.Estimate);
        Assert.All(slotted.Overrides, Assert.Null);
        Assert.Equal(Own(once).Estimate, Carried(once, around).Estimate);
    }

    [Fact(DisplayName = "BR-CD-005: a piece is carried on only up to three minutes from the far end of the run, and an advert of two series ends the run")]
    public void ARunEndsAtThreeMinutesAndAtAnAdvertOfTwoSeries()
    {
        Episode quiz = new(Programme(1, "架空のクイズ", Evening.AddHours(3), Lead), RecordingId.New());
        Sound head = Heard(89, Every(0));
        Sound[] pieces = [.. Enumerable.Range(0, 4).Select(at => Heard(25, Every(89 + (25 * at))))];
        Sound advert = Heard(15, [.. Every(1500), (quiz, 600)]);
        Sound afterTheAdvert = Heard(20, Every(1515));
        Sound ending = Heard(89, Every(1640));
        Sound leadingUp = Heard(20, Every(1619));
        CatalogSurroundings around = Around([head, .. pieces, advert, afterTheAdvert, ending, leadingUp]);

        Assert.Equal(
            [CatalogKind.Opening, CatalogKind.Opening, CatalogKind.Opening, CatalogKind.ProgrammeFixture],
            pieces.Select(piece => Carried(piece, around).Estimate.Kind));
        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.9, CatalogDecision.Pattern), Carried(advert, around).Estimate);
        Assert.Equal(CatalogKind.ProgrammeFixture, Carried(afterTheAdvert, around).Estimate.Kind);
        Assert.Equal(new CatalogEstimate(CatalogKind.Ending, 0.8, CatalogDecision.Continuation), Carried(leadingUp, around).Estimate);
    }

    [Fact(DisplayName = "BR-CD-005: a piece heard next to the opening in a broadcast recorded twice, and elsewhere in the other episode, is not carried on")]
    public void APieceNextToTheOpeningInOneEpisodeIsNotCarriedOn()
    {
        Sound head = Heard(50, Every(0));
        Sound slot = Heard(30, (first, 50), (again, 50), (second, 900));
        CatalogSurroundings around = Around(head, slot);

        Assert.Equal(new CatalogEstimate(CatalogKind.Cm, 0.5, CatalogDecision.Pattern), Carried(slot, around).Estimate);
        Assert.All(Carried(slot, around).Overrides, Assert.Null);
    }

    [Fact(DisplayName = "BR-CD-005: a piece carried on is a piece of the run for the next one, and an entry carried on is never the head of a run")]
    public void AnEntryCarriedOnIsAPieceAndNeverAHead()
    {
        Sound slot = Heard(30, Every(63));
        Sound next = Heard(13, Every(93));
        CatalogPattern carried = new(new CatalogEstimate(CatalogKind.Opening, 0.8, CatalogDecision.Continuation), 1, 1, [null, null, null]);
        CatalogSurroundings around = new([.. CatalogKinds.Stretches(slot.Placed, carried), .. CatalogKinds.Stretches(next.Placed, Own(next))]);

        Assert.All(CatalogKinds.Stretches(slot.Placed, carried), stretch => Assert.Equal(ThemePart.Piece, stretch.Part));
        Assert.Equal(CatalogKind.ProgrammeFixture, Carried(next, around).Estimate.Kind);
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

    private static CatalogPattern Carried(Sound sound, CatalogSurroundings around)
        => CatalogKinds.Estimate(sound.Length, sound.Placed, SeriesLinks.None, around);

    private static CatalogSurroundings Around(params Sound[] sounds)
        => new([.. sounds.SelectMany(sound => CatalogKinds.Stretches(sound.Placed, Own(sound)))]);

    private (Episode Episode, double FromProgrammeStart)[] Every(double fromProgrammeStart)
        => [.. Weeks.Select(episode => (episode, fromProgrammeStart))];

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
