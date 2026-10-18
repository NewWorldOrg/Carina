using System.Globalization;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CatalogSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Noon = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly uint[] Fifteen = [.. Enumerable.Range(1, 938).Select(frame => (uint)frame * 2_654_435_761u)];

    [Fact(DisplayName = "BR-CD-002: the catalog's entries and stretches outlive the recording they were heard in")]
    public async Task TheCatalogOutlivesTheRecording()
    {
        Recording recording = await RecordedAsync(7_101);
        (CatalogEntry entry, CatalogOccurrence[] occurrences) = Started(recording.Id, RecordingId.New());

        await KeepAsync(new CatalogChange([entry], [], occurrences, [], []));

        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await using var erasing = new NpgsqlCommand("DELETE FROM recording WHERE id = @recording", connection);
            erasing.Parameters.AddWithValue("recording", recording.Id.Value);
            Assert.Equal(1, await erasing.ExecuteNonQueryAsync());
        }

        await using CarinaDbContext context = Context();
        CatalogRepository catalog = new(context);

        Assert.Contains(await catalog.ListEntriesAsync(CancellationToken.None), kept => kept.Id.Equals(entry.Id));
        Assert.Equal(2, (await catalog.OccurrencesOfAsync([entry.Id], CancellationToken.None)).Count);
        Assert.Single(await catalog.OccurrencesInAsync([recording.Id], CancellationToken.None));
    }

    [Fact(DisplayName = "an entry and its stretches read back as they were kept")]
    public async Task AnEntryReadsBackAsKept()
    {
        RecordingId first = RecordingId.New();
        (CatalogEntry entry, CatalogOccurrence[] occurrences) = Started(first, RecordingId.New());

        await KeepAsync(new CatalogChange([entry], [], occurrences, [], []));

        await using CarinaDbContext context = Context();
        CatalogRepository catalog = new(context);
        CatalogEntry kept = (await catalog.ListEntriesAsync(CancellationToken.None)).Single(read => read.Id.Equals(entry.Id));
        CatalogOccurrence stretch = Assert.Single(await catalog.OccurrencesInAsync([first], CancellationToken.None));

        Assert.Equal(CatalogKind.Cm, kept.Kind);
        Assert.Equal(0.9, kept.Confidence);
        Assert.Equal(CatalogDecision.Pattern, kept.DecidedBy);
        Assert.Equal(entry.Length, kept.Length);
        Assert.Equal(Fifteen, kept.Frames());
        Assert.Equal((2, 2, 1), (kept.OccurrenceCount, kept.SeriesCount, kept.StationCount));
        Assert.Equal((entry.FirstSeenAt, entry.LastSeenAt, entry.UpdatedAt), (kept.FirstSeenAt, kept.LastSeenAt, kept.UpdatedAt));
        Assert.Equal(CatalogKinds.Version, kept.KindsVersion);
        Assert.True(stretch.Id > 0);
        Assert.Equal(entry.Id, stretch.EntryId);
        Assert.Equal((TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(615)), (stretch.StartsAt, stretch.EndsAt));
        Assert.Equal((new NetworkId(1), new ServiceId(1)), (stretch.NetworkId, stretch.ServiceId));
        Assert.Equal("架空の物語", stretch.SeriesName);
        Assert.Equal(0.05f, stretch.BitErrorRate);
        Assert.Null(stretch.KindOverride);
        Assert.Equal(Noon, stretch.MatchedAt);
    }

    [Fact(DisplayName = "the entries named are read with their fingerprints, and no other")]
    public async Task TheEntriesNamedAreRead()
    {
        (CatalogEntry named, CatalogOccurrence[] heard) = Started(RecordingId.New(), RecordingId.New());
        (CatalogEntry unnamed, CatalogOccurrence[] elsewhere) = Started(RecordingId.New(), RecordingId.New());

        await KeepAsync(new CatalogChange([named, unnamed], [], [.. heard, .. elsewhere], [], []));

        await using CarinaDbContext context = Context();
        IReadOnlyList<CatalogEntry> read = await new CatalogRepository(context).EntriesAsync([named.Id, CatalogEntryId.New()], CancellationToken.None);

        CatalogEntry entry = Assert.Single(read);
        Assert.Equal(named.Id, entry.Id);
        Assert.Equal(Fifteen, entry.Frames());
        Assert.Empty(await new CatalogRepository(context).EntriesAsync([], CancellationToken.None));
    }

    [Fact(DisplayName = "an entry estimated again and a stretch taken for another kind are written as they now stand")]
    public async Task AChangeToKeptRowsIsWritten()
    {
        RecordingId first = RecordingId.New();
        (CatalogEntry entry, CatalogOccurrence[] occurrences) = Started(first, RecordingId.New());

        await KeepAsync(new CatalogChange([entry], [], occurrences, [], []));

        CatalogEntry kept;
        CatalogOccurrence[] stretches;

        await using (CarinaDbContext reading = Context())
        {
            CatalogRepository catalog = new(reading);
            kept = (await catalog.ListEntriesAsync(CancellationToken.None)).Single(read => read.Id.Equals(entry.Id));
            stretches = [.. await catalog.OccurrencesOfAsync([entry.Id], CancellationToken.None)];
        }

        CatalogOccurrence third = CatalogOccurrence.Found(entry.Id, RecordingId.New(), Span(100, 115), Copy("架空の天気", Noon.AddDays(9)), 0.1, Noon.AddDays(9));
        kept.Reestimate(
            [.. stretches.Append(third).Select(stretch => PlacedOccurrence.Of(stretch, null, stretch == stretches[0] ? SegmentKind.Break : null))],
            SeriesLinks.None,
            Noon.AddDays(9));

        await KeepAsync(new CatalogChange([], [kept], [third], [stretches[0]], []));

        await using CarinaDbContext context = Context();
        CatalogRepository again = new(context);
        CatalogEntry written = (await again.ListEntriesAsync(CancellationToken.None)).Single(read => read.Id.Equals(entry.Id));
        IReadOnlyList<CatalogOccurrence> written3 = await again.OccurrencesOfAsync([entry.Id], CancellationToken.None);

        Assert.Equal(SegmentKind.Break, stretches[0].KindOverride);
        Assert.Equal((CatalogKind.Cm, 1.0, CatalogDecision.Answer), (written.Kind, written.Confidence, written.DecidedBy));
        Assert.Equal(3, written.OccurrenceCount);
        Assert.Equal(Noon.AddDays(9), written.UpdatedAt);
        Assert.Equal(3, written3.Count);
        Assert.Equal(SegmentKind.Break, written3.Single(stretch => stretch.Id == stretches[0].Id).KindOverride);
    }

    [Fact(DisplayName = "the catalog is counted by kind")]
    public async Task TheCatalogIsCountedByKind()
    {
        CatalogCounts before = await CountsAsync();
        (CatalogEntry advert, CatalogOccurrence[] heard) = Started(RecordingId.New(), RecordingId.New());

        await KeepAsync(new CatalogChange([advert], [], heard, [], []));

        CatalogCounts after = await CountsAsync();

        Assert.Equal(before.Cm + 1, after.Cm);
        Assert.Equal(before with { Cm = before.Cm + 1 }, after);
    }

    [Fact(DisplayName = "BR-CD-011: the openings, endings and programme fixtures are read as the recordings their whole stretches are heard in, leaving out a stretch taken for an advert and every advert")]
    public async Task TheSoundsThatLinkSeriesAreReadAsTheirRecordings()
    {
        CatalogEntryId id = CatalogEntryId.New();
        RecordingId first = RecordingId.New();
        RecordingId second = RecordingId.New();
        PlacedOccurrence[] placed =
        [
            Placed(id, first, Copy("架空の物語", Noon), 60, 20),
            Placed(id, second, Copy("架空の物語", Noon.AddDays(7)), 70, 20),
        ];
        CatalogEntry fixture = CatalogEntry.Start(id, Fifteen[..625], placed, SeriesLinks.None, Noon);
        RecordingId[] themed = [RecordingId.New(), RecordingId.New(), RecordingId.New(), RecordingId.New()];
        (CatalogEntry opening, CatalogOccurrence[] heard) = Kept(CatalogKind.Opening, 89, themed, [89, 89, 15, 89]);
        heard[3].Override(SegmentKind.Break);
        RecordingId[] advertised = [RecordingId.New(), RecordingId.New()];
        (CatalogEntry advert, CatalogOccurrence[] aired) = Kept(CatalogKind.Cm, 89, advertised, [89, 89]);

        await KeepAsync(new CatalogChange([fixture, opening, advert], [], [.. placed.Select(place => place.Occurrence), .. heard, .. aired], [], []));

        await using CarinaDbContext context = Context();
        IReadOnlyList<IReadOnlyList<RecordingId>> sharers = await new CatalogRepository(context).SoundSharersAsync(CancellationToken.None);

        Assert.Equal(CatalogKind.ProgrammeFixture, fixture.Kind);
        Assert.Contains(sharers, group => group.ToHashSet().SetEquals([first, second]));
        Assert.Contains(sharers, group => group.ToHashSet().SetEquals(themed[..2]));
        Assert.DoesNotContain(sharers, group => group.Intersect([themed[2], themed[3], .. advertised]).Any());
    }

    [Fact(DisplayName = "BR-CD-005: the entry first seen earliest of those estimated with an earlier version is the next to estimate again, until it is written with this one")]
    public async Task TheEarliestEntryEstimatedWithAnEarlierVersionIsNext()
    {
        Guid current = Guid.NewGuid();
        Guid earlier = Guid.NewGuid();
        Guid later = Guid.NewGuid();
        await InsertAsync("segment_catalog_entry", Seen(current, "2001-01-01", CatalogKinds.Version));
        await InsertAsync("segment_catalog_entry", Seen(earlier, "2001-01-02", null));
        await InsertAsync("segment_catalog_entry", Seen(later, "2001-01-03", CatalogKinds.FirstVersion));
        await InsertAsync("segment_catalog_occurrence", Stretch(earlier, Guid.NewGuid()));
        await InsertAsync("segment_catalog_occurrence", Stretch(earlier, Guid.NewGuid()));

        CatalogEntry next;
        IReadOnlyList<CatalogOccurrence> stretches;

        await using (CarinaDbContext reading = Context())
        {
            CatalogRepository catalog = new(reading);
            next = (await catalog.NextToReestimateAsync(CancellationToken.None))!;
            stretches = await catalog.OccurrencesOfAsync([next.Id], CancellationToken.None);
        }

        Assert.Equal(earlier, next.Id.Value);
        Assert.Equal(CatalogKinds.FirstVersion, next.KindsVersion);
        Assert.Equal(4, next.Fingerprint.Length);

        next.Reestimate([.. stretches.Select(stretch => PlacedOccurrence.Of(stretch, null, null))], SeriesLinks.None, Noon);
        await KeepAsync(new CatalogChange([], [next], [], [], []));

        await using CarinaDbContext context = Context();
        Assert.Equal(later, (await new CatalogRepository(context).NextToReestimateAsync(CancellationToken.None))?.Id.Value);
        Assert.Equal(CatalogKinds.Version, (await new CatalogRepository(context).EntriesAsync([next.Id], CancellationToken.None)).Single().KindsVersion);
    }

    [Fact(DisplayName = "BR-CD-005: an answer saved or withdrawn for a recording puts every entry heard in it back to be estimated again, and no other")]
    public async Task AnAnswerPutsTheEntriesHeardInItsRecordingBack()
    {
        Guid recording = Guid.NewGuid();
        Guid heard = Guid.NewGuid();
        Guid elsewhere = Guid.NewGuid();
        await InsertAsync("segment_catalog_entry", Seen(heard, "2026-10-01", CatalogKinds.Version));
        await InsertAsync("segment_catalog_entry", Seen(elsewhere, "2026-10-01", CatalogKinds.Version));
        await InsertAsync("segment_catalog_occurrence", Stretch(heard, recording));
        await InsertAsync("segment_catalog_occurrence", Stretch(heard, Guid.NewGuid()));
        await InsertAsync("segment_catalog_occurrence", Stretch(elsewhere, Guid.NewGuid()));
        RecordingId id = new(recording);
        RecordingAnswer answer = RecordingAnswer.Given(id, AnsweredStretches.Read([new SentStretch(0, 1800, SegmentKind.Programme)], TimeSpan.FromMinutes(30)).Stretches!, true, Noon);

        await AnswerAsync(answers => answers.AnswerAsync(answer, CancellationToken.None));
        int[] answered = [await KindsVersionAsync(heard), await KindsVersionAsync(elsewhere)];
        await ExecuteAsync($"UPDATE segment_catalog_entry SET kinds_version = {CatalogKinds.Version} WHERE id = '{heard}'");
        bool withdrew = await AnswerAsync(answers => answers.WithdrawAsync(id, Noon.AddHours(1), CancellationToken.None));
        int withdrawn = await KindsVersionAsync(heard);
        await ExecuteAsync($"UPDATE segment_catalog_entry SET kinds_version = {CatalogKinds.Version} WHERE id = '{heard}'");
        bool again = await AnswerAsync(answers => answers.WithdrawAsync(id, Noon.AddHours(2), CancellationToken.None));

        Assert.Equal([CatalogKinds.Unsettled, CatalogKinds.Version], answered);
        Assert.True(withdrew);
        Assert.Equal(CatalogKinds.Unsettled, withdrawn);
        Assert.False(again);
        Assert.Equal(CatalogKinds.Version, await KindsVersionAsync(heard));
    }

    [Fact(DisplayName = "BR-CD-011: a change that relinks series puts every other entry heard in the relinking entry's recordings back, and leaves the relinking entry and the rest")]
    public async Task AChangeThatRelinksSeriesPutsTheEntriesHeardWithItBack()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        Guid linking = Guid.NewGuid();
        Guid alongside = Guid.NewGuid();
        Guid apart = Guid.NewGuid();

        foreach (Guid entry in new[] { linking, alongside, apart })
        {
            await InsertAsync("segment_catalog_entry", Seen(entry, "2026-10-01", CatalogKinds.Version));
        }

        await InsertAsync("segment_catalog_occurrence", Stretch(linking, first));
        await InsertAsync("segment_catalog_occurrence", Stretch(linking, second));
        await InsertAsync("segment_catalog_occurrence", Stretch(alongside, second));
        await InsertAsync("segment_catalog_occurrence", Stretch(alongside, Guid.NewGuid()));
        await InsertAsync("segment_catalog_occurrence", Stretch(apart, Guid.NewGuid()));
        await InsertAsync("segment_catalog_occurrence", Stretch(apart, Guid.NewGuid()));

        await KeepAsync(new CatalogChange([], [], [], [], [new CatalogEntryId(linking)]));
        int[] versions = [await KindsVersionAsync(linking), await KindsVersionAsync(alongside), await KindsVersionAsync(apart)];

        Assert.Equal([CatalogKinds.Version, CatalogKinds.Unsettled, CatalogKinds.Version], versions);
    }

    [Theory(DisplayName = "the entry table refuses a kind, confidence, reason, length, fingerprint or count that does not hold")]
    [InlineData("kind", "'Jingle'", "ck_segment_catalog_entry_kind")]
    [InlineData("confidence", "NULL", "ck_segment_catalog_entry_confidence")]
    [InlineData("confidence", "1.5", "ck_segment_catalog_entry_confidence")]
    [InlineData("decided_by", "NULL", "ck_segment_catalog_entry_decided_by")]
    [InlineData("length", "interval '0'", "ck_segment_catalog_entry_length")]
    [InlineData("fingerprint", "'\\x010203'::bytea", "ck_segment_catalog_entry_fingerprint")]
    [InlineData("occurrences", "1", "ck_segment_catalog_entry_counts")]
    [InlineData("last_seen_at", "timestamptz '2026-09-01T00:00:00Z'", "ck_segment_catalog_entry_seen")]
    [InlineData("kinds_version", "-1", "ck_segment_catalog_entry_kinds_version")]
    public async Task TheEntryTableRefusesWhatDoesNotHold(string column, string value, string constraint)
    {
        Dictionary<string, string> row = new(StringComparer.Ordinal)
        {
            ["id"] = $"'{Guid.NewGuid()}'",
            ["kind"] = "'Cm'",
            ["confidence"] = "0.9",
            ["decided_by"] = "'Pattern'",
            ["length"] = "interval '15 seconds'",
            ["fingerprint"] = "'\\x01020304'::bytea",
            ["occurrences"] = "2",
            ["series_count"] = "1",
            ["station_count"] = "1",
            ["first_seen_at"] = "timestamptz '2026-10-01T00:00:00Z'",
            ["last_seen_at"] = "timestamptz '2026-10-02T00:00:00Z'",
            ["updated_at"] = "timestamptz '2026-10-02T00:00:00Z'",
        };
        row[column] = value;

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync("segment_catalog_entry", row));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal(constraint, refused.ConstraintName);
    }

    [Fact(DisplayName = "an entry not yet known is kept with neither a confidence nor a reason")]
    public async Task AnUnknownEntryIsKeptWithoutAConfidence()
    {
        Dictionary<string, string> row = Entry(Guid.NewGuid());
        row["kind"] = "'Unknown'";
        row["confidence"] = "NULL";
        row["decided_by"] = "NULL";

        await InsertAsync("segment_catalog_entry", row);
    }

    [Theory(DisplayName = "the stretch table refuses a stretch out of order, a disagreement beyond three tenths or a kind it does not know")]
    [InlineData("ends_at", "interval '10 seconds'", "ck_segment_catalog_occurrence_span")]
    [InlineData("starts_at", "interval '-1 second'", "ck_segment_catalog_occurrence_span")]
    [InlineData("bit_error_rate", "0.31", "ck_segment_catalog_occurrence_bit_error_rate")]
    [InlineData("kind_override", "'Jingle'", "ck_segment_catalog_occurrence_kind_override")]
    public async Task TheStretchTableRefusesWhatDoesNotHold(string column, string value, string constraint)
    {
        Guid entry = Guid.NewGuid();
        await InsertAsync("segment_catalog_entry", Entry(entry));
        Dictionary<string, string> row = Stretch(entry, Guid.NewGuid());
        row[column] = value;

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync("segment_catalog_occurrence", row));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal(constraint, refused.ConstraintName);
    }

    [Fact(DisplayName = "a stretch that disagrees in exactly three tenths of its bits is kept")]
    public async Task AStretchAtTheLimitIsKept()
    {
        Guid entry = Guid.NewGuid();
        await InsertAsync("segment_catalog_entry", Entry(entry));
        Dictionary<string, string> row = Stretch(entry, Guid.NewGuid());
        row["bit_error_rate"] = $"{(float)SegmentMatcher.MostBitErrors}::real";

        await InsertAsync("segment_catalog_occurrence", row);
    }

    [Fact(DisplayName = "one entry keeps one stretch per start in a recording")]
    public async Task OneStretchPerStart()
    {
        Guid entry = Guid.NewGuid();
        Guid recording = Guid.NewGuid();
        await InsertAsync("segment_catalog_entry", Entry(entry));
        await InsertAsync("segment_catalog_occurrence", Stretch(entry, recording));

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync("segment_catalog_occurrence", Stretch(entry, recording)));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refused.SqlState);
    }

    [Fact(DisplayName = "a stretch names an entry the catalog keeps, and an entry with stretches is not taken out")]
    public async Task AStretchNamesAKeptEntry()
    {
        Guid entry = Guid.NewGuid();
        await InsertAsync("segment_catalog_entry", Entry(entry));
        await InsertAsync("segment_catalog_occurrence", Stretch(entry, Guid.NewGuid()));

        PostgresException orphan = await Assert.ThrowsAsync<PostgresException>(() => InsertAsync("segment_catalog_occurrence", Stretch(Guid.NewGuid(), Guid.NewGuid())));
        PostgresException removal = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"DELETE FROM segment_catalog_entry WHERE id = '{entry}'"));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, orphan.SqlState);
        Assert.Equal(PostgresErrorCodes.RestrictViolation, removal.SqlState);
    }

    [Fact(DisplayName = "the record of the most recently started recording waiting to be matched comes first, and a matched one waits again once it changes")]
    public async Task TheNewestRecordWaitingToBeMatchedComesFirst()
    {
        DateTime far = new(2027, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        LearningExtraction older = await ReadAsync(far.AddHours(1));
        LearningExtraction newer = await ReadAsync(far.AddHours(2));

        Assert.Equal(newer.RecordingId, (await NextAsync())?.RecordingId);

        await ChangeAsync(newer.RecordingId, record => record.Matched(far.AddHours(3)));

        Assert.Equal(older.RecordingId, (await NextAsync())?.RecordingId);

        await ChangeAsync(newer.RecordingId, record =>
        {
            record.Outdate(new ExtractionVersion(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile), far.AddHours(4));
            record.Read(new ExtractionVersion(LearningData.ExtractionVersion + 1, ExtractionOrigin.RecordingFile), far.AddHours(4));
            record.Finish(far.AddHours(5));
        });

        Assert.Equal(newer.RecordingId, (await NextAsync())?.RecordingId);
        Assert.Equal(far.AddHours(3), (await FoundAsync(newer.RecordingId))?.MatchedAt);
    }

    [Fact(DisplayName = "the records around a recording are those read, started within the window, other than its own")]
    public async Task TheRecordsAroundARecordingAreThoseWithinTheWindow()
    {
        DateTime middle = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        LearningExtraction heard = await ReadAsync(middle);
        LearningExtraction before = await ReadAsync(middle.AddDays(-59));
        LearningExtraction after = await ReadAsync(middle.AddDays(59));
        await ReadAsync(middle.AddDays(-61));
        RecordingId waiting = RecordingId.New();
        await AddAsync(LearningExtraction.Waiting(waiting, Copy("架空の物語", middle.AddDays(1)), middle.AddDays(1)));

        await using CarinaDbContext context = Context();
        IReadOnlyList<LearningExtraction> around = await new CatalogWorklist(context).AroundAsync(heard.RecordingId, middle, TimeSpan.FromDays(60), CancellationToken.None);
        IReadOnlyDictionary<RecordingId, ProgrammeCopy> programmes = await new CatalogWorklist(context).ProgrammesAsync([heard.RecordingId, waiting, RecordingId.New()], CancellationToken.None);

        Assert.Equal([before.RecordingId, after.RecordingId], around.Select(record => record.RecordingId));
        Assert.Equal([heard.RecordingId, waiting], programmes.Keys.OrderBy(id => id.Equals(waiting)));
        Assert.Equal(heard.Programme, programmes[heard.RecordingId]);
    }

    [Fact(DisplayName = "the sound of a recording is read from its fingerprints and loudness, chunk by chunk")]
    public async Task TheSoundIsReadFromItsData()
    {
        RecordingId recording = RecordingId.New();
        LearningDataChunk chunk = LearningDataChunk.Of(
            1,
            FrameClock.Of(30000, 1001, LearningData.ChunkStarts(1)),
            Fifteen,
            [.. Enumerable.Repeat((byte)40, 750)],
            [],
            [],
            []);

        await using (CarinaDbContext writing = Context())
        {
            LearningDataRepository data = new(writing);

            foreach (LearningDataKind kind in new[] { LearningDataKind.SoundFingerprints, LearningDataKind.Loudness })
            {
                await data.KeepAsync(LearningDataBlock.Of(recording, LearningDataPart.Of(chunk, kind), ExtractionVersion.Current, Noon), CancellationToken.None);
            }
        }

        await using CarinaDbContext context = Context();
        SoundTrack sound = await new CatalogWorklist(context).SoundAsync(recording, CancellationToken.None);

        Assert.Equal(SoundFingerprint.PerChunk + Fifteen.Length, sound.Fingerprints.Length);
        Assert.Equal(Fifteen, sound.Fingerprints[SoundFingerprint.PerChunk..].ToArray());
        Assert.Equal(0u, sound.Fingerprints[0]);
        Assert.Equal(40, sound.Loudness[SoundLoudness.PerChunk]);
    }

    [Fact(DisplayName = "the record refuses to have been matched before it was made")]
    public async Task TheRecordRefusesAMatchBeforeItWasMade()
    {
        LearningExtraction record = await ReadAsync(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync($"UPDATE segment_extraction SET matched_at = timestamptz '2023-12-31T00:00:00Z' WHERE recording_id = '{record.RecordingId.Value}'"));

        Assert.Equal("ck_segment_extraction_matched", refused.ConstraintName);
    }

    private static SoundSpan Span(double starts, double ends) => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends));

    private static ProgrammeCopy Copy(string series, DateTime startedAt)
        => new(
            new NetworkId(1),
            new ServiceId(1),
            startedAt.AddMinutes(1),
            startedAt.AddMinutes(31),
            startedAt,
            $"{series} #1",
            [new ProgrammeGenre(7, 0)],
            [],
            1,
            AudioMode.Stereo,
            series);

    private static PlacedOccurrence Placed(CatalogEntryId entry, RecordingId recording, ProgrammeCopy programme, double fromProgrammeStart, double seconds)
    {
        TimeSpan starts = TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(fromProgrammeStart);
        CatalogOccurrence occurrence = CatalogOccurrence.Found(entry, recording, new SoundSpan(starts, starts + TimeSpan.FromSeconds(seconds)), programme, 0.05, Noon);

        return PlacedOccurrence.Of(occurrence, programme, null);
    }

    private static (CatalogEntry Entry, CatalogOccurrence[] Occurrences) Started(RecordingId first, RecordingId second)
    {
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence[] occurrences =
        [
            CatalogOccurrence.Found(id, first, Span(600, 615), Copy("架空の物語", Noon), 0.05, Noon),
            CatalogOccurrence.Found(id, second, Span(300, 315), Copy("架空のクイズ", Noon.AddHours(4)), 0.08, Noon),
        ];
        CatalogEntry entry = CatalogEntry.Start(id, Fifteen, [.. occurrences.Select(occurrence => PlacedOccurrence.Of(occurrence, null, null))], SeriesLinks.None, Noon);

        return (entry, occurrences);
    }

    private static Dictionary<string, string> Entry(Guid id)
        => new(StringComparer.Ordinal)
        {
            ["id"] = $"'{id}'",
            ["kind"] = "'Cm'",
            ["confidence"] = "0.9",
            ["decided_by"] = "'Pattern'",
            ["length"] = "interval '15 seconds'",
            ["fingerprint"] = "'\\x01020304'::bytea",
            ["occurrences"] = "2",
            ["series_count"] = "1",
            ["station_count"] = "1",
            ["first_seen_at"] = "timestamptz '2026-10-01T00:00:00Z'",
            ["last_seen_at"] = "timestamptz '2026-10-02T00:00:00Z'",
            ["updated_at"] = "timestamptz '2026-10-02T00:00:00Z'",
        };

    private static Dictionary<string, string> Seen(Guid id, string firstSeen, int? kindsVersion)
    {
        Dictionary<string, string> row = Entry(id);
        row["first_seen_at"] = $"timestamptz '{firstSeen}T00:00:00Z'";
        row["last_seen_at"] = $"timestamptz '{firstSeen}T00:00:00Z'";

        if (kindsVersion is int version)
        {
            row["kinds_version"] = version.ToString(CultureInfo.InvariantCulture);
        }

        return row;
    }

    /// <summary>
    /// An entry of <paramref name="kind"/> and <paramref name="seconds"/>, heard from the start of each
    /// recording named for as long as the length beside it.
    /// </summary>
    private static (CatalogEntry Entry, CatalogOccurrence[] Occurrences) Kept(CatalogKind kind, double seconds, RecordingId[] recordings, double[] lengths)
    {
        CatalogEntryId id = CatalogEntryId.New();
        CatalogOccurrence[] occurrences =
        [
            .. recordings.Select((recording, at) => CatalogOccurrence.Found(id, recording, Span(60, 60 + lengths[at]), Copy("架空の物語", Noon.AddDays(7 * at)), 0.05, Noon)),
        ];
        uint[] frames = [.. Enumerable.Range(1, (int)(TimeSpan.FromSeconds(seconds) / SegmentMatcher.Frame)).Select(frame => (uint)frame)];
        CatalogEntry entry = CatalogEntry.Rehydrate(
            id,
            kind,
            0.8,
            CatalogDecision.Pattern,
            TimeSpan.FromSeconds(seconds),
            CatalogFingerprint.Write(frames),
            occurrences.Length,
            1,
            1,
            Noon,
            Noon.AddDays(7 * (recordings.Length - 1)),
            Noon,
            CatalogKinds.Version);

        return (entry, occurrences);
    }

    private static Dictionary<string, string> Stretch(Guid entry, Guid recording)
        => new(StringComparer.Ordinal)
        {
            ["entry_id"] = $"'{entry}'",
            ["recording_id"] = $"'{recording}'",
            ["starts_at"] = "interval '600 seconds'",
            ["ends_at"] = "interval '615 seconds'",
            ["network_id"] = "1",
            ["service_id"] = "1",
            ["series_name"] = "NULL",
            ["recording_started_at"] = "timestamptz '2026-10-01T00:00:00Z'",
            ["bit_error_rate"] = "0.1",
            ["kind_override"] = "NULL",
            ["matched_at"] = "timestamptz '2026-10-02T00:00:00Z'",
        };

    private async Task InsertAsync(string table, Dictionary<string, string> row)
        => await ExecuteAsync($"INSERT INTO {table} ({string.Join(", ", row.Keys)}) VALUES ({string.Join(", ", row.Values)})");

    private async Task ExecuteAsync(string statement)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(statement, connection);

        await command.ExecuteNonQueryAsync();
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task<int> KindsVersionAsync(Guid entry)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using NpgsqlCommand reading = new($"SELECT kinds_version FROM segment_catalog_entry WHERE id = '{entry}'", connection);

        return Convert.ToInt32(await reading.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private async Task<T> AnswerAsync<T>(Func<SegmentAnswerRepository, Task<T>> answering)
    {
        await using CarinaDbContext context = Context();

        return await answering(new SegmentAnswerRepository(context));
    }

    private async Task AnswerAsync(Func<SegmentAnswerRepository, Task> answering)
    {
        await using CarinaDbContext context = Context();

        await answering(new SegmentAnswerRepository(context));
    }

    private async Task KeepAsync(CatalogChange change)
    {
        await using CarinaDbContext context = Context();

        await new CatalogRepository(context).KeepAsync(change, CancellationToken.None);
    }

    private async Task<CatalogCounts> CountsAsync()
    {
        await using CarinaDbContext context = Context();

        return await new CatalogCountsReader(context).ReadAsync(CancellationToken.None);
    }

    private async Task<LearningExtraction?> NextAsync()
    {
        await using CarinaDbContext context = Context();

        return await new CatalogWorklist(context).NextAsync(CancellationToken.None);
    }

    private async Task AddAsync(LearningExtraction extraction)
    {
        await using CarinaDbContext context = Context();

        await new LearningExtractionRepository(context).AddAsync(extraction, CancellationToken.None);
    }

    private async Task<LearningExtraction> ReadAsync(DateTime startedAt)
    {
        LearningExtraction record = LearningExtraction.Waiting(RecordingId.New(), Copy("架空の物語", startedAt), startedAt);

        record.Read(ExtractionVersion.Current, startedAt);
        record.Finish(startedAt.AddMinutes(31));
        await AddAsync(record);

        return record;
    }

    private async Task ChangeAsync(RecordingId recording, Action<LearningExtraction> change)
    {
        await using CarinaDbContext context = Context();
        LearningExtractionRepository records = new(context);
        LearningExtraction record = (await records.FindAsync(recording, CancellationToken.None))!;

        change(record);

        Assert.Equal(LearningExtractionWrite.Written, await records.SaveAsync(record, CancellationToken.None));
    }

    private async Task<LearningExtraction?> FoundAsync(RecordingId recording)
    {
        await using CarinaDbContext context = Context();

        return await new LearningExtractionRepository(context).FindAsync(recording, CancellationToken.None);
    }

    private async Task<Recording> RecordedAsync(int eventId)
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(40_001), new ServiceId(60_001), new EventId(eventId), Noon),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Noon,
            Noon.AddHours(1),
            new ProgrammeSnapshot("架空の番組", "架空のあらすじ", string.Empty, [new ProgrammeGenre(7, 0)], Noon, AudioMode.Stereo, ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Noon);

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 3_400_000, Noon.AddMinutes(30));

        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();

        return recording;
    }
}
