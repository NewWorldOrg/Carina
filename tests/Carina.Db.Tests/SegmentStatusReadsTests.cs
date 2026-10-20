using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentStatusReadsTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>, IAsyncLifetime
{
    private const int Other = 52;

    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime WeekAgo = Now - SegmentLearning.ReturnsOver;

    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    private static readonly uint[] Fifteen = [.. Enumerable.Range(1, 938).Select(frame => (uint)frame)];

    public async Task InitializeAsync()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await ExecuteAsync(
            connection,
            $$"""
            DELETE FROM segment_skip_event;
            DELETE FROM segment_catalog_occurrence;
            DELETE FROM segment_catalog_entry;
            DELETE FROM segment_extraction;
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at)
            VALUES ({{Other}}, 'Candidate', false, 2, '{}', '{}', '{}', '{}', '{}', now())
            ON CONFLICT DO NOTHING;
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact(DisplayName = "BR-CD-008: the catalog gives an evaluation the recordings read that it is sure of an advert in and the outer edges of the pods of those adverts, two adverts with no gap between them one pod; an advert of one series alone, an opening and a recording not read are left out")]
    public async Task TheCatalogGivesTheRecordingsItIsSureOfAnAdvertIn()
    {
        RecordingId pods = await RecordAsync(read: true);
        RecordingId single = await RecordAsync(read: true);
        RecordingId unsure = await RecordAsync(read: true);
        RecordingId unread = await RecordAsync(read: false);
        await EntryAsync(CatalogKind.Cm, 0.9, (pods, 300), (single, 100), (unread, 100));
        await EntryAsync(CatalogKind.Cm, 0.9, (pods, 315), (RecordingId.New(), 600));
        await EntryAsync(CatalogKind.Cm, 0.9, (pods, 900), (RecordingId.New(), 600));
        await EntryAsync(CatalogKind.Cm, 0.5, (unsure, 100), (unsure, 600));
        await EntryAsync(CatalogKind.Opening, 0.8, (single, 60), (RecordingId.New(), 60));

        EvaluationEvidence evidence = await CatalogEvidenceAsync();

        Assert.Equal(new EvaluationEvidence(2, 6, null), evidence);
    }

    [Fact(DisplayName = "with nothing the catalog is sure of, it gives an evaluation nothing to stand on")]
    public async Task WithNothingSureTheCatalogGivesNothing()
        => Assert.Equal(new EvaluationEvidence(0, 0, null), await CatalogEvidenceAsync());

    [Fact(DisplayName = "BR-CD-008: what viewers did with one version's buttons over the last seven days counts its skips and returns, and nothing older, of another version, of no version or closing a button")]
    public async Task OneVersionsSkipsAndReturnsOverTheLastSevenDaysAreCounted()
    {
        await SkipAsync(ViewerActionKind.Skipped, 8, Now.AddDays(-1), SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Skipped, 1, WeekAgo, SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Skipped, 1, Now, SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Returned, 3, Now.AddDays(-2), SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Dismissed, 4, Now.AddDays(-2), SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Skipped, 5, WeekAgo.AddTicks(-TimeSpan.TicksPerSecond), SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Returned, 5, Now.AddDays(-8), SegmentModelVersion.FirstNumber);
        await SkipAsync(ViewerActionKind.Skipped, 6, Now.AddDays(-1), Other);
        await SkipAsync(ViewerActionKind.Returned, 6, Now.AddDays(-1), Other);
        await SkipAsync(ViewerActionKind.Skipped, 7, Now.AddDays(-1), null);

        SkipReturns returns = await ReturnsAsync(SegmentModelVersion.FirstNumber);
        SkipReturns other = await ReturnsAsync(Other);

        Assert.Equal((10, 3, 0.3), (returns.Skips, returns.Returned, returns.Share!.Value));
        Assert.Equal((6, 6, 1.0), (other.Skips, other.Returned, other.Share!.Value));
    }

    [Fact(DisplayName = "a version whose buttons nobody pressed lately has no share that came back")]
    public async Task AVersionNobodySkippedWithHasNoShare()
    {
        await SkipAsync(ViewerActionKind.Returned, 2, Now.AddDays(-1), Other);

        SkipReturns returns = await ReturnsAsync(Other);

        Assert.Equal((0, 2), (returns.Skips, returns.Returned));
        Assert.Null(returns.Share);
        Assert.Equal(SkipReturns.None.Skips, (await ReturnsAsync(SegmentModelVersion.FirstNumber)).Skips);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string statement)
    {
        await using var command = new NpgsqlCommand(statement, connection);

        await command.ExecuteNonQueryAsync();
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task<RecordingId> RecordAsync(bool read)
    {
        RecordingId recording = RecordingId.New();
        ProgrammeCopy programme = new(new NetworkId(1), new ServiceId(1), Now.AddMinutes(1), Now.AddMinutes(31), Now, "架空の物語 #1", [], [], 1, AudioMode.Stereo, "架空の物語");
        LearningExtraction record = LearningExtraction.Waiting(recording, programme, Now);

        if (read)
        {
            record.Read(ExtractionVersion.Current, Now);
            record.Reached(HalfAnHour, Now.AddMinutes(30));
            record.Finish(Now.AddMinutes(31));
        }

        await using CarinaDbContext context = Context();
        await new LearningExtractionRepository(context).AddAsync(record, CancellationToken.None);

        return recording;
    }

    /// <summary>
    /// An entry of fifteen seconds of <paramref name="kind"/> and <paramref name="confidence"/>, heard at each second
    /// named in each recording named.
    /// </summary>
    private async Task EntryAsync(CatalogKind kind, double confidence, params (RecordingId Recording, double At)[] heard)
    {
        CatalogEntryId id = CatalogEntryId.New();
        ProgrammeCopy programme = new(new NetworkId(1), new ServiceId(1), Now.AddMinutes(1), Now.AddMinutes(31), Now, "架空の物語 #1", [], [], 1, AudioMode.Stereo, "架空の物語");

        await using CarinaDbContext context = Context();
        context.Add(CatalogEntry.Rehydrate(
            id,
            kind,
            confidence,
            CatalogDecision.Pattern,
            TimeSpan.FromSeconds(15),
            CatalogFingerprint.Write(Fifteen),
            heard.Length,
            2,
            2,
            Now,
            Now,
            Now,
            CatalogKinds.Version));
        context.AddRange(heard.Select(stretch => CatalogOccurrence.Found(
            id,
            stretch.Recording,
            new SoundSpan(TimeSpan.FromSeconds(stretch.At), TimeSpan.FromSeconds(stretch.At + 15)),
            programme,
            0.05,
            Now)));
        await context.SaveChangesAsync(CancellationToken.None);
    }

    private async Task<EvaluationEvidence> CatalogEvidenceAsync()
    {
        await using CarinaDbContext context = Context();

        return await new CatalogEvidenceReader(context).ReadAsync(CancellationToken.None);
    }

    private async Task SkipAsync(ViewerActionKind action, int count, DateTime at, int? version)
    {
        await using CarinaDbContext context = Context();
        SkipEventRepository skips = new(context);

        foreach (int _ in Enumerable.Range(0, count))
        {
            ViewerAction done = new(at, action, SegmentKind.Break, TimeSpan.FromSeconds(300), TimeSpan.FromSeconds(360), version);

            Assert.True(await skips.TakeAsync(SkipEvent.Of(RecordingId.New(), done), CancellationToken.None));
        }
    }

    private async Task<SkipReturns> ReturnsAsync(int version)
    {
        await using CarinaDbContext context = Context();

        return await new SkipEventRepository(context).ReturnsAsync(version, WeekAgo, Now, CancellationToken.None);
    }
}
