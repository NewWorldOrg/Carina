using System.Runtime.InteropServices;

using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Segments;
using Carina.TestSupport;

using static Carina.Infrastructure.Tests.Segments.SegmentDetectionHarness;

namespace Carina.Infrastructure.Tests.Segments;

public sealed class SegmentDetectingTests
{
    private static readonly uint[] Fifteen = [.. Enumerable.Range(1, 938).Select(frame => (uint)frame * 2_654_435_761u)];

    [Fact(DisplayName = "a detection reads the recording's readings as far as its data was read")]
    public async Task ADetectionReadsTheRecordingsReadings()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId recording = await harness.ReadAsync(0, "架空の物語", cuts: [100]);

        DetectionInputs inputs = await GatherAsync(harness, recording);

        Assert.Equal(TimeSpan.FromSeconds(Seconds), inputs.Evidence.Length);
        Assert.Equal(Seconds * FramesASecond, inputs.Evidence.Frames.Length);
        Assert.Contains(SoundLoudness.Silent, inputs.Evidence.Loudness.ToArray());
        Assert.Equal(Now, inputs.CatalogUntil);
        Assert.Equal(TimeSpan.Zero, inputs.Context.ProgrammeStarts);
    }

    [Fact(DisplayName = "the catalog's stretches heard in the recording are read with their entry's kind, or the kind the stretch alone is taken for")]
    public async Task TheCatalogsStretchesAreReadWithTheirKinds()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId recording = await harness.ReadAsync(0, "架空の物語");
        RecordingId elsewhere = await harness.ReadAsync(3, "架空のクイズ");
        ProgrammeCopy programme = harness.Records.Row(recording)!.Programme;
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence[] heard =
        [
            CatalogOccurrence.Found(id, recording, new SoundSpan(TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(115)), programme, 0.05, Now),
            CatalogOccurrence.Found(id, recording, new SoundSpan(TimeSpan.FromSeconds(200), TimeSpan.FromSeconds(215)), programme, 0.05, Now),
            CatalogOccurrence.Found(id, elsewhere, new SoundSpan(TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(65)), harness.Records.Row(elsewhere)!.Programme, 0.05, Now),
        ];
        harness.Catalog.Entries.Add(CatalogEntry.Start(id, Fifteen, [.. heard.Select(occurrence => PlacedOccurrence.Of(occurrence, null, null))], SeriesLinks.None, Now));
        harness.Catalog.Occurrences.AddRange([heard[0], Overridden(heard[1], SegmentKind.Programme), heard[2]]);

        DetectionInputs inputs = await GatherAsync(harness, recording);

        Assert.Equal(CatalogKind.Cm, harness.Catalog.Entries[0].Kind);
        Assert.Equal(
            [(100d, SegmentKind.Break), (200d, SegmentKind.Programme)],
            inputs.Context.Catalog.Select(stretch => (stretch.Starts.TotalSeconds, stretch.Kind!.Value)));
    }

    [Fact(DisplayName = "BR-CD-012: the station's logo is learned from its earlier recordings, never from later ones, other stations' or the recording itself")]
    public async Task TheLogoIsLearnedFromTheStationsEarlierRecordings()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId judged = await harness.ReadAsync(3, "架空の物語", logo: true);
        await harness.ReadAsync(1, "架空のクイズ", logo: true);
        await harness.ReadAsync(0, "架空の天気", logo: true);
        await harness.ReadAsync(5, "架空の物語", station: 2, logo: true);
        await harness.ReadAsync(6, "架空の物語", station: 2, logo: true);

        DetectionInputs alone = await GatherAsync(harness, judged);

        await harness.ReadAsync(4, "架空の天気", logo: true);
        await harness.ReadAsync(5, "架空のクイズ", logo: true);
        DetectionInputs learned = await GatherAsync(harness, judged);

        Assert.Empty(alone.Context.Logo.Shapes);
        Assert.Equal(8, Assert.Single(learned.Context.Logo.Shapes).Count);
    }

    [Fact(DisplayName = "the earlier episodes are what the version in use detected of the series' earlier recordings, leaving out what it threw away, other series and later episodes")]
    public async Task TheEarlierEpisodesAreWhatTheVersionInUseDetected()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId judged = await harness.ReadAsync(0, "架空の物語");
        RecordingId lastWeek = await harness.ReadAsync(7, "架空の物語");
        RecordingId twoWeeksAgo = await harness.ReadAsync(14, "架空の物語");
        RecordingId thrownAway = await harness.ReadAsync(21, "架空の物語");
        RecordingId otherSeries = await harness.ReadAsync(7, "架空のクイズ", station: 2);
        RecordingId byAnotherVersion = await harness.ReadAsync(28, "架空の物語");
        await Detected(harness, lastWeek, SegmentModelVersion.FirstNumber, (0, 100, SegmentKind.Programme), (100, 160, SegmentKind.Break), (160, 300, SegmentKind.Programme));
        await Detected(harness, twoWeeksAgo, SegmentModelVersion.FirstNumber, (0, 300, SegmentKind.Programme));
        await Detected(harness, thrownAway, SegmentModelVersion.FirstNumber, (0, 100, SegmentKind.Programme), (100, 300, SegmentKind.Break));
        await Detected(harness, otherSeries, SegmentModelVersion.FirstNumber, (0, 100, SegmentKind.Programme), (100, 160, SegmentKind.Break), (160, 300, SegmentKind.Programme));
        await Detected(harness, byAnotherVersion, 2, (0, 100, SegmentKind.Programme), (100, 160, SegmentKind.Break), (160, 300, SegmentKind.Programme));
        await Detected(harness, judged, SegmentModelVersion.FirstNumber, (0, 300, SegmentKind.Programme));

        DetectionInputs inputs = await GatherAsync(harness, lastWeek);

        PastEpisode episode = Assert.Single(inputs.Context.Past);
        Assert.Equal(TimeSpan.Zero, episode.ProgrammeStarts);
        Assert.Single(episode.Segments);
        Assert.Equal("架空の物語", inputs.Context.Series);
    }

    [Fact(DisplayName = "BR-CD-007: an earlier episode stands on the confirmed answer given for it over what the version in use detected, its opening still read from the detection while the answer could not name one, one answered alone is remembered too, and each keeps where its programme ends")]
    public async Task TheEarlierEpisodesStandOnTheirConfirmedAnswers()
    {
        using SegmentDetectionHarness harness = new();
        RecordingId first = await harness.ReadAsync(28, "架空の物語");
        RecordingId second = await harness.ReadAsync(21, "架空の物語");
        RecordingId unconfirmed = await harness.ReadAsync(14, "架空の物語");
        RecordingId withdrawn = await harness.ReadAsync(7, "架空の物語");
        RecordingId judged = await harness.ReadAsync(0, "架空の物語");
        await Detected(harness, first, SegmentModelVersion.FirstNumber, (0, 150, SegmentKind.Programme), (150, 240, SegmentKind.Opening), (240, 300, SegmentKind.Programme));
        await Detected(harness, unconfirmed, SegmentModelVersion.FirstNumber, (0, 300, SegmentKind.Programme));
        await Detected(harness, withdrawn, SegmentModelVersion.FirstNumber, (0, 300, SegmentKind.Programme));
        await Answered(harness, first, true, null);
        await Answered(harness, second, true, null);
        await Answered(harness, unconfirmed, false, null);
        await Answered(harness, withdrawn, true, Now);

        DetectionInputs inputs = await GatherAsync(harness, judged);
        Dictionary<RecordingId, PastEpisode> past = inputs.Context.Past.ToDictionary(episode => episode.Recording);
        TimeSpan within = TimeSpan.Zero;

        Assert.Equal(new[] { first, second, unconfirmed, withdrawn }.Select(id => id.Value).Order(), past.Keys.Select(id => id.Value).Order());
        Assert.True(past[first].StandsOnTheAnswer);
        Assert.True(past[first].HadABreakNear(TimeSpan.FromSeconds(75), within));
        Assert.True(past[first].HadAnOpeningNear(TimeSpan.FromSeconds(195), within));
        Assert.True(past[second].HadABreakNear(TimeSpan.FromSeconds(75), within));
        Assert.False(past[second].HadAnOpeningNear(TimeSpan.FromSeconds(195), within));
        Assert.False(past[unconfirmed].StandsOnTheAnswer);
        Assert.False(past[withdrawn].StandsOnTheAnswer);
        Assert.All(past.Values, episode => Assert.Equal(TimeSpan.FromSeconds(Seconds), episode.ProgrammeEnds));
        Assert.True(inputs.Context.RemembersTheAnswerOf(first));
    }

    [Theory(DisplayName = "BR-CD-011: an earlier recording on another station sharing the sound of an opening, an ending or a programme fixture with the recording is an earlier episode of its series")]
    [InlineData(CatalogKind.ProgrammeFixture)]
    [InlineData(CatalogKind.Opening)]
    [InlineData(CatalogKind.Ending)]
    public async Task ARecordingSharingAFixtureOnAnotherStationIsAnEarlierEpisode(CatalogKind kind)
    {
        using SegmentDetectionHarness harness = new();
        RecordingId judged = await harness.ReadAsync(0, "架空の物語");
        RecordingId delayed = await harness.ReadAsync(7, "まるで違う架空の名前", station: 2);
        await Detected(harness, delayed, SegmentModelVersion.FirstNumber, (0, 100, SegmentKind.Programme), (100, 160, SegmentKind.Break), (160, 300, SegmentKind.Programme));
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence[] heard =
        [
            CatalogOccurrence.Found(id, judged, new SoundSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40)), harness.Records.Row(judged)!.Programme, 0.05, Now),
            CatalogOccurrence.Found(id, delayed, new SoundSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40)), harness.Records.Row(delayed)!.Programme, 0.05, Now),
        ];
        harness.Catalog.Entries.Add(Fixture(id, heard, kind));
        harness.Catalog.Occurrences.AddRange(heard);

        DetectionInputs inputs = await GatherAsync(harness, judged);

        Assert.Single(inputs.Context.Past);
    }

    [Theory(DisplayName = "BR-CD-011: a recording on another station heard in a stretch of an advert or of part of an opening is not an earlier episode of the series")]
    [InlineData(CatalogKind.Cm, 30)]
    [InlineData(CatalogKind.Opening, 15)]
    public async Task ARecordingSharingAnAdvertOrPartOfAnOpeningIsNoEarlierEpisode(CatalogKind kind, double seconds)
    {
        using SegmentDetectionHarness harness = new();
        RecordingId judged = await harness.ReadAsync(0, "架空の物語");
        RecordingId elsewhere = await harness.ReadAsync(7, "まるで違う架空の名前", station: 2);
        await Detected(harness, elsewhere, SegmentModelVersion.FirstNumber, (0, 100, SegmentKind.Programme), (100, 160, SegmentKind.Break), (160, 300, SegmentKind.Programme));
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence[] heard =
        [
            CatalogOccurrence.Found(id, judged, new SoundSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(40)), harness.Records.Row(judged)!.Programme, 0.05, Now),
            CatalogOccurrence.Found(id, elsewhere, new SoundSpan(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10 + seconds)), harness.Records.Row(elsewhere)!.Programme, 0.05, Now),
        ];
        harness.Catalog.Entries.Add(Fixture(id, heard, kind));
        harness.Catalog.Occurrences.AddRange(heard);

        DetectionInputs inputs = await GatherAsync(harness, judged);

        Assert.Empty(inputs.Context.Past);
    }

    private static async Task<DetectionInputs> GatherAsync(SegmentDetectionHarness harness, RecordingId recording)
        => await harness.Detecting().GatherAsync(harness.Records.Row(recording)!, SegmentModelVersion.FirstNumber, Now, CancellationToken.None);

    private static async Task Detected(SegmentDetectionHarness harness, RecordingId recording, int version, params (double Starts, double Ends, SegmentKind Kind)[] stretches)
    {
        SegmentSpan[] segments = [.. stretches.Select(stretch => new SegmentSpan(TimeSpan.FromSeconds(stretch.Starts), TimeSpan.FromSeconds(stretch.Ends), stretch.Kind, 0.9))];

        await harness.Detections.KeepAsync(
            RecordingDetection.Of(recording, version, SegmentDetection.Judged(segments, TimeSpan.FromSeconds(Seconds), []), 2, Now, Now),
            CancellationToken.None);
    }

    private static async Task Answered(SegmentDetectionHarness harness, RecordingId recording, bool confirmed, DateTime? withdrawnAt)
    {
        SegmentSpan[] segments =
        [
            new(TimeSpan.Zero, TimeSpan.FromSeconds(30), SegmentKind.Programme, null),
            new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120), SegmentKind.Break, null),
            new(TimeSpan.FromSeconds(120), TimeSpan.FromSeconds(Seconds), SegmentKind.Programme, null),
        ];

        await harness.Answers.AnswerAsync(
            RecordingAnswer.Rehydrate(recording, segments, TimeSpan.FromSeconds(Seconds), confirmed, confirmed ? Now : null, Now, 1, null),
            CancellationToken.None);

        if (withdrawnAt is DateTime at)
        {
            await harness.Answers.WithdrawAsync(recording, at, CancellationToken.None);
        }
    }

    private static CatalogEntry Fixture(CatalogEntryId id, CatalogOccurrence[] heard, CatalogKind kind = CatalogKind.ProgrammeFixture)
        => CatalogEntry.Rehydrate(
            id,
            kind,
            0.9,
            CatalogDecision.Pattern,
            TimeSpan.FromSeconds(30),
            MemoryMarshal.AsBytes(Fifteen.AsSpan()).ToArray(),
            heard.Length,
            2,
            2,
            Now,
            Now,
            Now,
            CatalogKinds.Version);

    private static CatalogOccurrence Overridden(CatalogOccurrence found, SegmentKind kind)
        => CatalogOccurrence.Rehydrate(
            1,
            found.EntryId,
            found.RecordingId,
            found.StartsAt,
            found.EndsAt,
            found.NetworkId,
            found.ServiceId,
            found.SeriesName,
            found.RecordingStartedAt,
            found.BitErrorRate,
            kind,
            found.MatchedAt);
}
