using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class CatalogMatchingDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private readonly CatalogShelf catalog = new();

    private readonly Neighbours worklist = new();

    private CatalogMatchingDomainService Service => new(worklist, catalog);

    [Fact(DisplayName = "BR-CD-005: a recording is matched against those started around it, passing over the stretches of each an entry already explains")]
    public async Task ARecordingIsMatchedAgainstThoseAroundIt()
    {
        LearningExtraction heard = Record();
        LearningExtraction explained = Record();
        LearningExtraction bare = Record();
        worklist.Around.AddRange([explained, bare]);
        CatalogEntry entry = Entry(seriesCount: 2);
        catalog.Entries.Add(entry);
        catalog.Occurrences.Add(Heard(entry, explained.RecordingId, 600, 615));

        IReadOnlyList<CatalogNeighbour> neighbours = await Service.AroundAsync(heard, TimeSpan.FromDays(7), Cancel);

        Assert.Equal([explained.RecordingId, bare.RecordingId], neighbours.Select(neighbour => neighbour.Recording));
        Assert.Equal([new SoundSpan(TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(615))], neighbours[0].Explained);
        Assert.Empty(neighbours[1].Explained);
        Assert.Equal((heard.RecordingId, TimeSpan.FromDays(7)), worklist.AskedAround);
    }

    [Fact(DisplayName = "BR-CD-005: putting matches into the catalog reads the entries they name with every stretch, the programmes of every recording involved, and the stretches around in the recordings matched, the recording itself and every recording of an entry heard in one series only")]
    public async Task PuttingMatchesInReadsWhatSettlesTheEntries()
    {
        RecordingId matched = RecordingId.New();
        RecordingId other = RecordingId.New();
        RecordingId[] ofOneSeries = [RecordingId.New(), RecordingId.New()];
        RecordingId ofTwoSeries = RecordingId.New();
        CatalogEntry single = Entry(seriesCount: 1);
        CatalogEntry shared = Entry(seriesCount: 2);
        catalog.Entries.AddRange([single, shared, Entry(seriesCount: 1)]);
        catalog.Occurrences.AddRange([Heard(single, ofOneSeries[0], 0, 15), Heard(single, ofOneSeries[1], 30, 45), Heard(shared, ofTwoSeries, 60, 75)]);
        SoundMatch[] matches =
        [
            Match(new EntrySound(single.Id)),
            Match(new EntrySound(shared.Id)),
            Match(new RecordedSound(other)),
            Match(new RecordedSound(other)),
        ];

        CatalogKnowledge known = await Service.KnowledgeOfMatchesAsync(matched, matches, Cancel);

        Assert.Equal([single.Id, shared.Id], known.Entries.Select(entry => entry.Id));
        Assert.Equal(3, known.Occurrences.Count);
        Assert.Equal(Set(matched, other, ofOneSeries[0], ofOneSeries[1], ofTwoSeries), Set([.. worklist.AskedProgrammes]));
        Assert.Equal(Set(other, matched, ofOneSeries[0], ofOneSeries[1]), Set([.. catalog.AskedAround]));
    }

    [Fact(DisplayName = "BR-CD-005: matches that name no entry read no entry, and the stretches around only in the recordings matched")]
    public async Task MatchesNamingNoEntryReadNone()
    {
        RecordingId matched = RecordingId.New();

        CatalogKnowledge known = await Service.KnowledgeOfMatchesAsync(matched, [], Cancel);

        Assert.Empty(known.Entries);
        Assert.Equal(0, catalog.EntriesAsked);
        Assert.Equal(Set(matched), Set([.. catalog.AskedAround]));
    }

    [Theory(DisplayName = "BR-CD-005: estimating an entry again reads every stretch it has, and the stretches around them only when it is heard in one series")]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public async Task EstimatingAgainReadsTheStretchesAroundOneSeries(int seriesCount, bool around)
    {
        CatalogEntry entry = Entry(seriesCount);
        RecordingId[] heardIn = [RecordingId.New(), RecordingId.New()];
        catalog.Entries.Add(entry);
        catalog.Occurrences.AddRange([Heard(entry, heardIn[0], 0, 15), Heard(entry, heardIn[1], 0, 15), Heard(entry, heardIn[1], 300, 315)]);

        CatalogKnowledge known = await Service.KnowledgeOfEntryAsync(entry, Cancel);

        Assert.Equal([entry.Id], known.Entries.Select(kept => kept.Id));
        Assert.Equal(3, known.Occurrences.Count);
        Assert.Equal(Set(heardIn), Set([.. worklist.AskedProgrammes]));
        Assert.Equal(around ? Set(heardIn) : Set(), Set([.. catalog.AskedAround]));
    }

    [Fact(DisplayName = "BR-CD-004: once the catalog changes, every recording hearing an entry recast and every recording with a stretch taken for another kind is detected again, each once")]
    public async Task EveryRecordingHearingAChangeIsDetectedAgain()
    {
        CatalogEntry recast = Entry(seriesCount: 2);
        CatalogEntry left = Entry(seriesCount: 2);
        RecordingId[] hearing = [RecordingId.New(), RecordingId.New()];
        RecordingId overridden = RecordingId.New();
        catalog.Entries.AddRange([recast, left]);
        catalog.Occurrences.AddRange([Heard(recast, hearing[0], 0, 15), Heard(recast, hearing[1], 0, 15), Heard(recast, hearing[1], 30, 45), Heard(left, RecordingId.New(), 0, 15)]);
        CatalogChange change = new([], [], [], [Heard(left, overridden, 60, 75), Heard(left, hearing[0], 90, 105)], [], [recast.Id]);

        IReadOnlyList<RecordingId> again = await Service.HeardAnewAsync(change, Cancel);

        Assert.Equal([hearing[0], hearing[1], overridden], again);
        Assert.Empty(await Service.HeardAnewAsync(new CatalogChange([], [], [], [], [], []), Cancel));
    }

    private static HashSet<RecordingId> Set(params RecordingId[] recordings) => [.. recordings];

    private static LearningExtraction Record()
    {
        LearningExtraction record = LearningExtraction.Waiting(RecordingId.New(), LearningExtractionTests.Programme(), Now);

        record.Read(ExtractionVersion.Current, Now);
        record.Reached(TimeSpan.FromMinutes(30), Now);
        record.Finish(Now);

        return record;
    }

    private static CatalogEntry Entry(int seriesCount)
        => CatalogEntry.Rehydrate(
            CatalogEntryId.New(),
            CatalogKind.Cm,
            0.9,
            CatalogDecision.Pattern,
            TimeSpan.FromSeconds(15),
            CatalogFingerprint.Write([1, 2, 3, 4]),
            2,
            seriesCount,
            1,
            Now.AddDays(-7),
            Now,
            Now,
            CatalogKinds.Version);

    private static CatalogOccurrence Heard(CatalogEntry entry, RecordingId recording, double from, double to)
        => CatalogOccurrence.Found(
            entry.Id,
            recording,
            new SoundSpan(TimeSpan.FromSeconds(from), TimeSpan.FromSeconds(to)),
            LearningExtractionTests.Programme(),
            0.05,
            Now);

    private static SoundMatch Match(SoundOrigin other)
        => new(new SoundSpan(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(115)), other, new SoundSpan(TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(215)), 0.05);

    private sealed class Neighbours : ICatalogWorklist
    {
        public List<LearningExtraction> Around { get; } = [];

        public (RecordingId Heard, TimeSpan Within)? AskedAround { get; private set; }

        public List<RecordingId> AskedProgrammes { get; } = [];

        public Task<LearningExtraction?> NextAsync(CancellationToken cancellationToken) => Task.FromResult<LearningExtraction?>(null);

        public Task<IReadOnlyList<LearningExtraction>> AroundAsync(RecordingId heard, DateTime startedAt, TimeSpan within, CancellationToken cancellationToken)
        {
            AskedAround = (heard, within);

            return Task.FromResult<IReadOnlyList<LearningExtraction>>([.. Around]);
        }

        public Task<IReadOnlyDictionary<RecordingId, ProgrammeCopy>> ProgrammesAsync(IReadOnlyCollection<RecordingId> recordings, CancellationToken cancellationToken)
        {
            AskedProgrammes.AddRange(recordings);

            return Task.FromResult<IReadOnlyDictionary<RecordingId, ProgrammeCopy>>(recordings.ToDictionary(recording => recording, _ => LearningExtractionTests.Programme()));
        }

        public Task<SoundTrack> SoundAsync(RecordingId recording, CancellationToken cancellationToken)
            => throw new NotSupportedException("The domain service reads no sound.");
    }
}
