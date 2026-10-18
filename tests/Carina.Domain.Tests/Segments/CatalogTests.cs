using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using static Carina.Domain.Tests.Segments.CatalogSeries;

namespace Carina.Domain.Tests.Segments;

public sealed class CatalogTests
{
    private static readonly DateTime Evening = new(2026, 10, 5, 10, 0, 0, DateTimeKind.Utc);

    private static readonly CatalogSeries Drama = new(1, "架空の物語", Evening);

    private static readonly CatalogSeries Quiz = new(1, "架空のクイズ", Evening.AddHours(3));

    private static readonly RecordingId Heard = RecordingId.New();

    private static readonly RecordingId First = RecordingId.New();

    private static readonly RecordingId Second = RecordingId.New();

    private static readonly SoundTrack Sound = SoundTrack.Of([.. Enumerable.Range(1, 120 * 125 / 2).Select(value => (uint)value)], []);

    private static readonly Dictionary<RecordingId, ProgrammeCopy> Programmes = new()
    {
        [Heard] = Drama.Episode(3),
        [First] = Quiz.Episode(1),
        [Second] = Drama.Episode(2),
    };

    [Fact(DisplayName = "a stretch matched in a recording no entry explains starts an entry with a stretch in each recording")]
    public void AMatchWithARecordingStartsAnEntry()
    {
        SoundMatch match = new(Span(10, 25), new RecordedSound(First), Span(300, 315), 0.08);

        CatalogChange change = Catalog.Absorb(Heard, Sound, [match], Knowing(), Now);

        CatalogEntry entry = Assert.Single(change.Started);
        Assert.Empty(change.Reestimated);
        Assert.Equal(Sound.Slice(Span(10, 25)), entry.Frames());
        Assert.Equal(SegmentMatcher.Frame * entry.Frames().Length, entry.Length);
        Assert.InRange(entry.Length, TimeSpan.FromSeconds(15) - SegmentMatcher.Frame, TimeSpan.FromSeconds(15) + SegmentMatcher.Frame);
        Assert.Equal(CatalogKind.Cm, entry.Kind);
        Assert.Equal(0.9, entry.Confidence);
        Assert.Equal(2, change.Found.Count);
        Assert.All(change.Found, occurrence => Assert.Equal(entry.Id, occurrence.EntryId));
        Assert.Contains(change.Found, occurrence => occurrence.RecordingId.Equals(Heard) && occurrence.StartsAt == Span(10, 25).Starts && occurrence.BitErrorRate == 0);
        Assert.Contains(change.Found, occurrence => occurrence.RecordingId.Equals(First) && occurrence.StartsAt == Span(300, 315).Starts && occurrence.BitErrorRate == 0.08f);
    }

    [Fact(DisplayName = "a stretch matched in a catalog entry adds a stretch to that entry and estimates it again")]
    public void AMatchWithAnEntryAddsToIt()
    {
        (CatalogEntry entry, CatalogOccurrence[] occurrences) = Kept(Drama.Episode(0), Drama.Episode(1));
        SoundMatch match = new(Span(10, 25), new EntrySound(entry.Id), Span(0, 15), 0.04);

        CatalogChange change = Catalog.Absorb(Heard, Sound, [match], Knowing(entry, occurrences), Now.AddDays(1));

        Assert.Empty(change.Started);
        Assert.Empty(change.Overridden);
        Assert.Same(entry, Assert.Single(change.Reestimated));
        CatalogOccurrence found = Assert.Single(change.Found);
        Assert.Equal(entry.Id, found.EntryId);
        Assert.Equal(Heard, found.RecordingId);
        Assert.Equal(3, entry.OccurrenceCount);
        Assert.Equal(Now.AddDays(1), entry.UpdatedAt);
    }

    [Fact(DisplayName = "a stretch already kept that is no longer taken for another kind on its own is handed back to be written again")]
    public void AStretchWhoseOwnKindChangesIsHandedBack()
    {
        (CatalogEntry entry, CatalogOccurrence[] occurrences) = Kept(Drama.Episode(0), Drama.Episode(1));
        occurrences[0].Override(SegmentKind.Break);
        SoundMatch match = new(Span(10, 25), new EntrySound(entry.Id), Span(0, 15), 0.04);

        CatalogChange change = Catalog.Absorb(Heard, Sound, [match], Knowing(entry, occurrences), Now);

        Assert.Same(occurrences[0], Assert.Single(change.Overridden));
        Assert.Null(occurrences[0].KindOverride);
    }

    [Fact(DisplayName = "a recording matched over the same stretch as an entry joins that entry rather than starting another")]
    public void ARecordingMatchedWithAnEntryJoinsIt()
    {
        (CatalogEntry entry, CatalogOccurrence[] occurrences) = Kept(Drama.Episode(0), Drama.Episode(1));
        SoundMatch withEntry = new(Span(10, 25), new EntrySound(entry.Id), Span(0, 15), 0.04);
        SoundMatch withRecording = new(Span(10.1, 25), new RecordedSound(First), Span(300.1, 315), 0.06);

        CatalogChange change = Catalog.Absorb(Heard, Sound, [withRecording, withEntry], Knowing(entry, occurrences), Now);

        Assert.Empty(change.Started);
        Assert.Equal(2, change.Found.Count);
        Assert.All(change.Found, occurrence => Assert.Equal(entry.Id, occurrence.EntryId));
        Assert.Equal(CatalogKind.Cm, entry.Kind);
        Assert.Equal(0.9, entry.Confidence);
    }

    [Fact(DisplayName = "a piece heard twice in the recording itself starts an entry with both of its stretches")]
    public void APieceHeardTwiceStartsAnEntry()
    {
        SoundMatch itself = new(Span(10, 25), new RecordedSound(Heard), Span(70, 85), 0.0);

        CatalogChange change = Catalog.Absorb(Heard, Sound, [itself], Knowing(), Now);

        CatalogEntry entry = Assert.Single(change.Started);
        Assert.Equal(2, change.Found.Count);
        Assert.All(change.Found, occurrence => Assert.Equal(Heard, occurrence.RecordingId));
        Assert.Equal(CatalogKind.Cm, entry.Kind);
        Assert.Equal(0.5, entry.Confidence);
    }

    [Fact(DisplayName = "the same stretch matched in two recordings starts one entry, defined by the longest match")]
    public void TwoRecordingsOverOneStretchStartOneEntry()
    {
        SoundMatch shorter = new(Span(10, 25), new RecordedSound(First), Span(300, 315), 0.05);
        SoundMatch longer = new(Span(10, 40), new RecordedSound(Second), Span(500, 530), 0.05);

        CatalogChange change = Catalog.Absorb(Heard, Sound, [shorter, longer], Knowing(), Now);

        CatalogEntry entry = Assert.Single(change.Started);
        Assert.InRange(entry.Length, TimeSpan.FromSeconds(30) - SegmentMatcher.Frame, TimeSpan.FromSeconds(30) + SegmentMatcher.Frame);
        Assert.Equal(3, change.Found.Count);
        Assert.Equal(3, entry.OccurrenceCount);
    }

    public static TheoryData<int, int, int> EveryOrder => new() { { 0, 1, 2 }, { 0, 2, 1 }, { 1, 0, 2 }, { 1, 2, 0 }, { 2, 0, 1 }, { 2, 1, 0 } };

    [Theory(DisplayName = "a piece heard twice in the recording and once in another starts one entry with three stretches, whatever order the matches come in")]
    [MemberData(nameof(EveryOrder))]
    public void APieceHeardTwiceAndElsewhereStartsOneEntry(int first, int second, int third)
    {
        SoundMatch[] matches =
        [
            new(Span(10, 25), new RecordedSound(First), Span(300, 315), 0.05),
            new(Span(70, 85), new RecordedSound(First), Span(300, 315), 0.06),
            new(Span(10, 25), new RecordedSound(Heard), Span(70, 85), 0.08),
        ];

        CatalogChange change = Catalog.Absorb(Heard, Sound, [matches[first], matches[second], matches[third]], Knowing(), Now);

        CatalogEntry entry = Assert.Single(change.Started);
        Assert.Equal(3, entry.OccurrenceCount);
        Assert.Equal(
            [(Heard, Span(10, 25).Starts), (Heard, Span(70, 85).Starts), (First, Span(300, 315).Starts)],
            change.Found.Select(occurrence => (occurrence.RecordingId, occurrence.StartsAt)).OrderBy(found => found.RecordingId.Equals(First)).ThenBy(found => found.StartsAt));
    }

    [Fact(DisplayName = "separate stretches start separate entries")]
    public void SeparateStretchesStartSeparateEntries()
    {
        SoundMatch early = new(Span(10, 25), new RecordedSound(First), Span(300, 315), 0.05);
        SoundMatch late = new(Span(60, 75), new RecordedSound(First), Span(400, 415), 0.05);

        CatalogChange change = Catalog.Absorb(Heard, Sound, [early, late], Knowing(), Now);

        Assert.Equal(2, change.Started.Count);
        Assert.Equal(4, change.Found.Count);
    }

    [Fact(DisplayName = "a stretch an entry already has in a recording is not added again")]
    public void AStretchAlreadyKeptIsNotAddedAgain()
    {
        (CatalogEntry entry, CatalogOccurrence[] occurrences) = Kept(Drama.Episode(0), Programmes[Heard], Heard);
        SoundMatch match = new(Span(10.2, 25), new EntrySound(entry.Id), Span(0.2, 15), 0.04);

        CatalogChange change = Catalog.Absorb(Heard, Sound, [match], Knowing(entry, occurrences), Now);

        Assert.Empty(change.Found);
        Assert.Empty(change.Started);
        Assert.Empty(change.Reestimated);
        Assert.Equal(2, entry.OccurrenceCount);
    }

    [Fact(DisplayName = "a match in a recording whose programme is not known adds nothing")]
    public void AMatchWithAnUnknownProgrammeAddsNothing()
    {
        SoundMatch match = new(Span(10, 25), new RecordedSound(RecordingId.New()), Span(300, 315), 0.08);

        CatalogChange change = Catalog.Absorb(Heard, Sound, [match], Knowing(), Now);

        Assert.Empty(change.Started);
        Assert.Empty(change.Found);
    }

    [Fact(DisplayName = "a match naming an entry that was not handed over is refused")]
    public void AMatchNamingAnUnknownEntryIsRefused()
    {
        SoundMatch match = new(Span(10, 25), new EntrySound(CatalogEntryId.New()), Span(0, 15), 0.08);

        Assert.Throws<ArgumentException>(() => Catalog.Absorb(Heard, Sound, [match], Knowing(), Now));
    }

    private static SoundSpan Span(double starts, double ends) => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends));

    private static CatalogKnowledge Knowing(CatalogEntry? entry = null, IReadOnlyList<CatalogOccurrence>? occurrences = null)
        => new(entry is null ? [] : [entry], occurrences ?? [], Programmes, SeriesLinks.None);

    private static (CatalogEntry Entry, CatalogOccurrence[] Occurrences) Kept(ProgrammeCopy one, ProgrammeCopy other, RecordingId? otherRecording = null)
    {
        CatalogEntryId id = CatalogEntryId.New();
        PlacedOccurrence[] placed = [At(id, one, 600, 15), PlacedOccurrence.Of(Occurrence(id, other, otherRecording), other, null)];
        CatalogEntry entry = CatalogEntry.Start(id, [.. Enumerable.Range(1, 938).Select(value => (uint)value)], placed, SeriesLinks.None, Now);

        return (entry, [.. placed.Select(place => place.Occurrence)]);
    }

    private static CatalogOccurrence Occurrence(CatalogEntryId id, ProgrammeCopy programme, RecordingId? recording)
        => CatalogOccurrence.Found(id, recording ?? RecordingId.New(), Span(10, 25), programme, 0.05, Now);
}
