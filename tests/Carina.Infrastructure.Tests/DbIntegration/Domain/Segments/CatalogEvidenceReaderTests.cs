using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Segments;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CatalogEvidenceReaderTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    private static readonly uint[] Fifteen = [.. Enumerable.Range(1, 938).Select(frame => (uint)frame)];

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => ClearAsync();

    public async Task DisposeAsync()
    {
        await ClearAsync();
        await container.DisposeAsync();
    }

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

    [Fact(DisplayName = "BR-CD-008: the catalog gives an evaluation no edge of a pod with an advert's length outside it to the next silent cut the recording's loudness holds, and the edges of a pod with the programme outside it")]
    public async Task TheCatalogLeavesOutAnEdgeBesideAnAdvert()
    {
        RecordingId recording = await RecordAsync(read: true);
        await LoudnessAsync(recording, 285, 300, 315, 330, 345);
        await EntryAsync(CatalogKind.Cm, 0.9, (recording, 300), (RecordingId.New(), 600));
        await EntryAsync(CatalogKind.Cm, 0.9, (recording, 315), (RecordingId.New(), 600));
        await EntryAsync(CatalogKind.Cm, 0.9, (recording, 450), (RecordingId.New(), 600));

        Assert.Equal(new EvaluationEvidence(1, 2, null), await CatalogEvidenceAsync());
    }

    [Fact(DisplayName = "with nothing the catalog is sure of, it gives an evaluation nothing to stand on")]
    public async Task WithNothingSureTheCatalogGivesNothing()
        => Assert.Equal(new EvaluationEvidence(0, 0, null), await CatalogEvidenceAsync());

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

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ILearningExtractionRepository>().AddAsync(record, CancellationToken.None);

        return recording;
    }

    /// <summary>
    /// The loudness of the recording's first chunk, loud but for half a second of silence around each second named.
    /// </summary>
    private async Task LoudnessAsync(RecordingId recording, params double[] silences)
    {
        byte[] loudness = [.. Enumerable.Repeat((byte)40, SoundLoudness.PerChunk)];

        foreach (double at in silences)
        {
            int middle = (int)(at / SegmentEvidence.LoudnessStep.TotalSeconds);

            Array.Fill(loudness, SoundLoudness.Silent, middle - 12, 25);
        }

        LearningDataChunk chunk = LearningDataChunk.Of(0, FrameClock.Of(30000, 1001, TimeSpan.Zero), [], loudness, [], [], []);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ILearningDataRepository>().KeepAsync(
            LearningDataBlock.Of(recording, LearningDataPart.Of(chunk, LearningDataKind.Loudness), ExtractionVersion.Current, Now),
            CancellationToken.None);
    }

    /// <summary>
    /// An entry of fifteen seconds of <paramref name="kind"/> and <paramref name="confidence"/>, heard at each second
    /// named in each recording named.
    /// </summary>
    private async Task EntryAsync(CatalogKind kind, double confidence, params (RecordingId Recording, double At)[] heard)
    {
        CatalogEntryId id = CatalogEntryId.New();
        ProgrammeCopy programme = new(new NetworkId(1), new ServiceId(1), Now.AddMinutes(1), Now.AddMinutes(31), Now, "架空の物語 #1", [], [], 1, AudioMode.Stereo, "架空の物語");

        await using CarinaDbContext context = database.Open();
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
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<ICatalogEvidenceReader>().ReadAsync(CancellationToken.None);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext context = database.Open();
        await context.Database.ExecuteSqlRawAsync(
            """
            DELETE FROM segment_catalog_occurrence;
            DELETE FROM segment_catalog_entry;
            DELETE FROM segment_extraction;
            DELETE FROM segment_learning_data;
            """);
    }
}
