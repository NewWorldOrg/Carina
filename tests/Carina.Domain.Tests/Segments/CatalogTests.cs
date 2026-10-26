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

    [Theory(DisplayName = "an advert slot with half a second of silence at either end, heard in two series, starts an advert entry as long as what is heard of it")]
    [InlineData(15)]
    [InlineData(30)]
    public void AnAdvertSlotStartsAnAdvertEntry(int slot)
    {
        int seconds = slot - 1;
        short[] advert = SoundScenes.Joined(SoundScenes.Silence(0.5), SoundScenes.Piece(103, seconds), SoundScenes.Silence(0.5));
        SoundTrack heard = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(1, 90), advert, 10));
        SoundTrack other = SoundScenes.Track(SoundScenes.Laid(SoundScenes.Programme(2, 90), advert, 37));
        IReadOnlyList<SoundMatch> matches = SegmentMatcher.For(heard).Against(new RecordedSound(First), other.Fingerprints, []);

        CatalogChange change = Catalog.Absorb(Heard, heard, matches, Knowing(), Now);

        CatalogEntry entry = Assert.Single(change.Started);
        Assert.InRange(entry.Length, TimeSpan.FromSeconds(seconds - 0.3), TimeSpan.FromSeconds(seconds + 0.3));
        Assert.All(change.Found, occurrence => Assert.InRange(occurrence.Length, TimeSpan.FromSeconds(seconds - 0.3), TimeSpan.FromSeconds(seconds + 0.3)));
        Assert.Equal((CatalogKind.Cm, 0.9), (entry.Kind, entry.Confidence));
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

    [Fact(DisplayName = "a stretch of the recording that joins an entry through the other side of a match within itself is claimed for that entry")]
    public void AStretchJoiningThroughTheOtherSideIsClaimed()
    {
        SoundMatch later = new(Span(70, 86), new RecordedSound(Heard), Span(130, 146), 0.02);
        SoundMatch earlier = new(Span(10, 25), new RecordedSound(Heard), Span(70, 85), 0.02);
        SoundMatch elsewhere = new(Span(10, 25), new RecordedSound(First), Span(300, 315), 0.05);

        CatalogChange change = Catalog.Absorb(Heard, Sound, [elsewhere, earlier, later], Knowing(), Now);

        CatalogEntry entry = Assert.Single(change.Started);
        Assert.Equal(4, entry.OccurrenceCount);
        Assert.Contains(change.Found, occurrence => occurrence.RecordingId.Equals(First));
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

    [Fact(DisplayName = "BR-CD-005: an entry estimated before the rows for openings, heard at the start of each episode, is the opening once it is estimated again")]
    public void AnEntryEstimatedBeforeTheThemesBecomesTheOpening()
    {
        CatalogEntryId id = CatalogEntryId.New();
        CatalogEntry entry = KeptBeforeThemes(id, 3);
        CatalogOccurrence[] stretches = [.. Enumerable.Range(0, 3).Select(week => At(id, Drama.Episode(week), 0, 89).Occurrence)];
        Dictionary<RecordingId, ProgrammeCopy> programmes = stretches.Select((stretch, week) => (stretch.RecordingId, Drama.Episode(week))).ToDictionary();

        Assert.True(entry.AwaitsReestimation);

        CatalogChange change = Catalog.Reestimate(entry, Knowing(entry, stretches, programmes: programmes), Now.AddDays(1));

        Assert.Equal((CatalogKind.Opening, 0.8, CatalogDecision.Pattern), (entry.Kind, entry.Confidence, entry.DecidedBy));
        Assert.Equal(CatalogKinds.Version, entry.KindsVersion);
        Assert.False(entry.AwaitsReestimation);
        Assert.Equal(Now.AddDays(1), entry.UpdatedAt);
        Assert.Empty(change.Started);
        Assert.Empty(change.Found);
        Assert.Same(entry, Assert.Single(change.Reestimated));
        Assert.Empty(change.Overridden);
        Assert.Equal([entry.Id], change.Relinking);
        Assert.Equal([entry.Id], change.Recast);
    }

    [Fact(DisplayName = "BR-CD-004: an opening estimated again as the opening it was, at the confidence it had, is not handed back as recast, so the recordings that hear it are not detected again")]
    public void AnEntryEstimatedAgainAsItWasIsNotRecast()
    {
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence[] stretches = [.. Enumerable.Range(0, 3).Select(week => At(id, Drama.Episode(week), 0, 89).Occurrence)];
        CatalogEntry entry = KeptOpening(id, stretches.Length);
        Dictionary<RecordingId, ProgrammeCopy> programmes = stretches.Select((stretch, week) => (stretch.RecordingId, Drama.Episode(week))).ToDictionary();

        CatalogChange change = Catalog.Reestimate(entry, Knowing(entry, stretches, programmes: programmes), Now.AddDays(1));

        Assert.Equal((CatalogKind.Opening, 0.8), (entry.Kind, entry.Confidence));
        Assert.Same(entry, Assert.Single(change.Reestimated));
        Assert.Empty(change.Recast);
    }

    [Fact(DisplayName = "an entry estimated again hands back the stretches whose own kind changed")]
    public void AnEntryEstimatedAgainHandsBackTheStretchesWhoseKindChanged()
    {
        CatalogEntryId id = CatalogEntryId.New();
        CatalogEntry entry = KeptBeforeThemes(id, 3);
        CatalogOccurrence[] stretches = [.. Enumerable.Range(0, 3).Select(week => At(id, Drama.Episode(week), week is 2 ? 1700 : 0, 89).Occurrence)];
        Dictionary<RecordingId, ProgrammeCopy> programmes = stretches.Select((stretch, week) => (stretch.RecordingId, Drama.Episode(week))).ToDictionary();

        CatalogChange change = Catalog.Reestimate(entry, Knowing(entry, stretches, programmes), Now.AddDays(1));

        Assert.Equal((CatalogKind.Opening, 0.8, CatalogDecision.Pattern), (entry.Kind, entry.Confidence, entry.DecidedBy));
        Assert.Equal(SegmentKind.Ending, stretches[2].KindOverride);
        Assert.Same(stretches[2], Assert.Single(change.Overridden));
    }

    [Fact(DisplayName = "BR-CD-011: an entry started as a programme fixture changes the links and is recast, and an advert that gains a stretch does neither")]
    public void AnEntryThatLinksSeriesChangesTheLinks()
    {
        SoundMatch samePlace = new(Span(70, 85), new RecordedSound(Second), Span(70, 85), 0.05);
        (CatalogEntry advert, CatalogOccurrence[] aired) = Kept(Drama.Episode(0), Quiz.Episode(1));
        SoundMatch heardAgain = new(Span(10, 25), new EntrySound(advert.Id), Span(0, 15), 0.04);

        CatalogChange started = Catalog.Absorb(Heard, Sound, [samePlace], Knowing(), Now);
        CatalogChange grown = Catalog.Absorb(Heard, Sound, [heardAgain], Knowing(advert, aired), Now);

        CatalogEntry fixture = Assert.Single(started.Started);
        Assert.Equal(CatalogKind.ProgrammeFixture, fixture.Kind);
        Assert.Equal([fixture.Id], started.Relinking);
        Assert.Equal([fixture.Id], started.Recast);
        Assert.Equal(CatalogKind.Cm, advert.Kind);
        Assert.Empty(grown.Relinking);
        Assert.Empty(grown.Recast);
    }

    [Fact(DisplayName = "BR-CD-011: a kept opening that gains a stretch, and one estimated again out of its kind, change the links, and only the one out of its kind is recast")]
    public void AnOpeningThatGrowsOrLeavesItsKindChangesTheLinks()
    {
        CatalogEntryId grows = CatalogEntryId.New();
        CatalogOccurrence[] weeks = [At(grows, Drama.Episode(0), 0, 89).Occurrence, At(grows, Drama.Episode(1), 0, 89).Occurrence];
        CatalogEntry growing = KeptOpening(grows, weeks.Length);
        SoundMatch heardAgain = new(Span(60, 149), new EntrySound(grows), Span(0, 89), 0.04);
        CatalogEntryId leaves = CatalogEntryId.New();
        CatalogOccurrence[] apart = [At(leaves, Drama.Episode(0), 600, 89).Occurrence, At(leaves, Quiz.Episode(0), 600, 89).Occurrence];
        CatalogEntry leaving = KeptOpening(leaves, apart.Length);
        Dictionary<RecordingId, ProgrammeCopy> programmes = new(Programmes)
        {
            [weeks[0].RecordingId] = Drama.Episode(0),
            [weeks[1].RecordingId] = Drama.Episode(1),
            [apart[0].RecordingId] = Drama.Episode(0),
            [apart[1].RecordingId] = Quiz.Episode(0),
        };

        CatalogChange grown = Catalog.Absorb(Heard, Sound, [heardAgain], Knowing(growing, weeks, programmes: programmes), Now);
        CatalogChange left = Catalog.Reestimate(leaving, Knowing(leaving, apart, programmes: programmes), Now);

        Assert.Equal(CatalogKind.Opening, growing.Kind);
        Assert.Equal([grows], grown.Relinking);
        Assert.Empty(grown.Recast);
        Assert.Equal(CatalogKind.Cm, leaving.Kind);
        Assert.Equal([leaves], left.Relinking);
        Assert.Equal([leaves], left.Recast);
    }

    [Fact(DisplayName = "BR-CD-005: a programme fixture estimated again beside the opening it follows in two episodes is that opening, and its stretches elsewhere are handed back as the programme")]
    public void AFixtureEstimatedAgainBesideTheOpeningCarriesItOn()
    {
        RecordingId[] weeks = [RecordingId.New(), RecordingId.New()];
        (CatalogEntry head, CatalogOccurrence[] heads) = KeptAt(CatalogKind.Opening, 0.8, 89, weeks, (0, 0), (1, 0));
        (CatalogEntry phrase, CatalogOccurrence[] phrases) = KeptAt(CatalogKind.ProgrammeFixture, 0.7, 13, weeks, (0, 89), (1, 89), (0, 700), (1, 700));
        Dictionary<RecordingId, ProgrammeCopy> programmes = new()
        {
            [weeks[0]] = Drama.Episode(0),
            [weeks[1]] = Drama.Episode(1),
        };

        CatalogChange change = Catalog.Reestimate(phrase, Knowing(phrase, phrases, programmes, Around((head, heads), (phrase, phrases))), Now.AddDays(1));

        Assert.Equal((CatalogKind.Opening, 0.8, CatalogDecision.Continuation), (phrase.Kind, phrase.Confidence, phrase.DecidedBy));
        Assert.Equal([null, null, SegmentKind.Programme, SegmentKind.Programme], phrases.Select(stretch => stretch.KindOverride));
        Assert.Equal([phrases[2], phrases[3]], change.Overridden);
        Assert.Empty(change.Relinking);
    }

    [Fact(DisplayName = "BR-CD-005: a new episode heard to have the opening and the phrase after it carries the phrase on there too")]
    public void ANewEpisodeCarriesThePhraseOnThere()
    {
        RecordingId[] weeks = [RecordingId.New(), RecordingId.New()];
        (CatalogEntry head, CatalogOccurrence[] heads) = KeptAt(CatalogKind.Opening, 0.8, 89, weeks, (0, 0), (1, 0));
        (CatalogEntry phrase, CatalogOccurrence[] phrases) = KeptAt(CatalogKind.ProgrammeFixture, 0.7, 13, weeks, (0, 89), (1, 89));
        Dictionary<RecordingId, ProgrammeCopy> programmes = new(Programmes)
        {
            [weeks[0]] = Drama.Episode(0),
            [weeks[1]] = Drama.Episode(1),
        };
        SoundMatch openingHeard = new(Span(60, 149), new EntrySound(head.Id), Span(0, 89), 0.04);
        SoundMatch phraseHeard = new(Span(149, 162), new EntrySound(phrase.Id), Span(0, 13), 0.04);

        CatalogChange change = Catalog.Absorb(
            Heard,
            Sound,
            [openingHeard, phraseHeard],
            Knowing([head, phrase], [.. heads, .. phrases], programmes, Around((head, heads), (phrase, phrases))),
            Now.AddDays(1));

        CatalogOccurrence there = Assert.Single(change.Found, stretch => stretch.EntryId.Equals(phrase.Id));
        Assert.Equal((CatalogKind.Opening, CatalogDecision.Continuation), (phrase.Kind, phrase.DecidedBy));
        Assert.Equal((CatalogKind.Opening, CatalogDecision.Pattern), (head.Kind, head.DecidedBy));
        Assert.Null(there.KindOverride);
    }

    [Fact(DisplayName = "BR-CD-005: a programme fixture on the grid estimated again beside an advert in one of its recordings is a sponsor's advert, recast, and stops linking series")]
    public void AFixtureOnTheGridBesideAnAdvertIsRecastAsASponsorsAdvert()
    {
        RecordingId[] weeks = [RecordingId.New(), RecordingId.New()];
        (CatalogEntry advert, CatalogOccurrence[] adverts) = KeptAt(CatalogKind.Cm, 0.9, 14, weeks, (0, 855), (1, 300));
        (CatalogEntry sponsor, CatalogOccurrence[] sponsors) = KeptAt(CatalogKind.ProgrammeFixture, 0.7, 14, weeks, (0, 840), (1, 840));
        Dictionary<RecordingId, ProgrammeCopy> programmes = new()
        {
            [weeks[0]] = Drama.Episode(0),
            [weeks[1]] = Drama.Episode(1),
        };

        CatalogChange change = Catalog.Reestimate(sponsor, Knowing(sponsor, sponsors, programmes, Around((advert, adverts), (sponsor, sponsors))), Now.AddDays(1));

        Assert.Equal((CatalogKind.Cm, 0.5, CatalogDecision.Pattern), (sponsor.Kind, sponsor.Confidence, sponsor.DecidedBy));
        Assert.Equal(CatalogKinds.Version, sponsor.KindsVersion);
        Assert.Equal([sponsor.Id], change.Relinking);
        Assert.Equal([sponsor.Id], change.Recast);
        Assert.Empty(change.Overridden);
    }

    [Fact(DisplayName = "BR-CD-005: two sponsor's adverts heard one after the other right after an advert, all three grown by one recording, are both adverts of one series, the second settled beside the first as it is settled")]
    public void TwoSponsorsAdvertsGrownTogetherAreBothAdverts()
    {
        RecordingId[] weeks = [RecordingId.New(), RecordingId.New()];
        (CatalogEntry advert, CatalogOccurrence[] adverts) = KeptAt(CatalogKind.Cm, 0.5, 14, weeks, (0, 300), (1, 1200));
        (CatalogEntry first, CatalogOccurrence[] firsts) = KeptAt(CatalogKind.ProgrammeFixture, 0.7, 14, weeks, (0, 840), (1, 840));
        (CatalogEntry second, CatalogOccurrence[] seconds) = KeptAt(CatalogKind.ProgrammeFixture, 0.7, 14, weeks, (0, 855), (1, 855));
        Dictionary<RecordingId, ProgrammeCopy> programmes = new(Programmes)
        {
            [weeks[0]] = Drama.Episode(0),
            [weeks[1]] = Drama.Episode(1),
        };
        SoundMatch[] heard =
        [
            new(Span(885, 899), new EntrySound(advert.Id), Span(0, 14), 0.04),
            new(Span(900, 914), new EntrySound(first.Id), Span(0, 14), 0.04),
            new(Span(915, 929), new EntrySound(second.Id), Span(0, 14), 0.04),
        ];

        CatalogChange change = Catalog.Absorb(
            Heard,
            Sound,
            heard,
            Knowing([advert, first, second], [.. adverts, .. firsts, .. seconds], programmes, Around((advert, adverts), (first, firsts), (second, seconds))),
            Now.AddDays(1));

        Assert.Equal((CatalogKind.Cm, 0.5), (advert.Kind, advert.Confidence));
        Assert.Equal((CatalogKind.Cm, 0.5, CatalogDecision.Pattern), (first.Kind, first.Confidence, first.DecidedBy));
        Assert.Equal((CatalogKind.Cm, 0.5, CatalogDecision.Pattern), (second.Kind, second.Confidence, second.DecidedBy));
        Assert.True(change.Recast.ToHashSet().SetEquals([first.Id, second.Id]));
    }

    [Fact(DisplayName = "a match naming an entry that was not handed over is refused")]
    public void AMatchNamingAnUnknownEntryIsRefused()
    {
        SoundMatch match = new(Span(10, 25), new EntrySound(CatalogEntryId.New()), Span(0, 15), 0.08);

        Assert.Throws<ArgumentException>(() => Catalog.Absorb(Heard, Sound, [match], Knowing(), Now));
    }

    private static SoundSpan Span(double starts, double ends) => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends));

    private static CatalogKnowledge Knowing(
        CatalogEntry? entry = null,
        IReadOnlyList<CatalogOccurrence>? occurrences = null,
        IReadOnlyDictionary<RecordingId, ProgrammeCopy>? programmes = null,
        CatalogSurroundings? around = null)
        => new(entry is null ? [] : [entry], occurrences ?? [], programmes ?? Programmes, SeriesLinks.None, around);

    private static CatalogKnowledge Knowing(
        IReadOnlyList<CatalogEntry> entries,
        IReadOnlyList<CatalogOccurrence> occurrences,
        IReadOnlyDictionary<RecordingId, ProgrammeCopy> programmes,
        CatalogSurroundings around)
        => new(entries, occurrences, programmes, SeriesLinks.None, around);

    private static CatalogSurroundings Around(params (CatalogEntry Entry, CatalogOccurrence[] Stretches)[] kept)
        => new(
        [
            .. kept.SelectMany(each => each.Stretches.Select(stretch => ThemeStretch.Of(
                each.Entry.Id,
                stretch.RecordingId,
                stretch.Span,
                new CatalogEstimate(each.Entry.Kind, each.Entry.Confidence, each.Entry.DecidedBy),
                each.Entry.SeriesCount,
                stretch.KindOverride))),
        ]);

    /// <summary>
    /// An entry kept as <paramref name="kind"/> with a stretch at each place, a week of the drama and where in its
    /// programme, in the recording <paramref name="weeks"/> names for that week.
    /// </summary>
    private static (CatalogEntry Entry, CatalogOccurrence[] Stretches) KeptAt(
        CatalogKind kind,
        double confidence,
        double seconds,
        RecordingId[] weeks,
        params (int Week, double FromProgrammeStart)[] places)
    {
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence[] stretches =
        [
            .. places.Select(place => CatalogSeries.At(id, Drama.Episode(place.Week), place.FromProgrammeStart, seconds, weeks[place.Week]).Occurrence),
        ];
        CatalogEntry entry = CatalogEntry.Rehydrate(
            id,
            kind,
            confidence,
            CatalogDecision.Pattern,
            TimeSpan.FromSeconds(seconds),
            CatalogFingerprint.Write([.. Enumerable.Range(1, 1000).Select(value => (uint)value)]),
            stretches.Length,
            1,
            1,
            Evening,
            Evening.AddDays(7),
            Now,
            CatalogKinds.Version - 1);

        return (entry, stretches);
    }

    private static CatalogEntry KeptOpening(CatalogEntryId id, int occurrences)
        => CatalogEntry.Rehydrate(
            id,
            CatalogKind.Opening,
            0.8,
            CatalogDecision.Pattern,
            TimeSpan.FromSeconds(89),
            CatalogFingerprint.Write([.. Enumerable.Range(1, 5562).Select(value => (uint)value)]),
            occurrences,
            1,
            1,
            Evening,
            Evening.AddDays(7),
            Now,
            CatalogKinds.Version);

    private static CatalogEntry KeptBeforeThemes(CatalogEntryId id, int occurrences)
        => CatalogEntry.Rehydrate(
            id,
            CatalogKind.Unknown,
            null,
            null,
            TimeSpan.FromSeconds(89),
            CatalogFingerprint.Write([.. Enumerable.Range(1, 5562).Select(value => (uint)value)]),
            occurrences,
            1,
            1,
            Evening,
            Evening.AddDays(7),
            Now,
            CatalogKinds.FirstVersion);

    private static (CatalogEntry Entry, CatalogOccurrence[] Occurrences) Kept(ProgrammeCopy one, ProgrammeCopy other, RecordingId? otherRecording = null)
    {
        CatalogEntryId id = CatalogEntryId.New();
        PlacedOccurrence[] placed = [At(id, one, 600, 15), PlacedOccurrence.Of(Occurrence(id, other, otherRecording), other)];
        CatalogEntry entry = CatalogEntry.Start(id, [.. Enumerable.Range(1, 938).Select(value => (uint)value)], placed, SeriesLinks.None, Now);

        return (entry, [.. placed.Select(place => place.Occurrence)]);
    }

    private static CatalogOccurrence Occurrence(CatalogEntryId id, ProgrammeCopy programme, RecordingId? recording)
        => CatalogOccurrence.Found(id, recording ?? RecordingId.New(), Span(10, 25), programme, 0.05, Now);
}
