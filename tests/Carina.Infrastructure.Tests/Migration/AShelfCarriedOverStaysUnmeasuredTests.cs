using System.Globalization;

using Carina.Domain.Base;
using Carina.Domain.Encodings;
using Carina.Domain.Migration;
using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Migration;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using static Carina.Infrastructure.Tests.Migration.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.Migration;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class AShelfCarriedOverStaysUnmeasuredTests : IDisposable
{
    private const int Shelf = 53;

    private const string NotARecording = "stray.sh";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime FirstAired = new(2026, 4, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Now = new(2026, 9, 8, 5, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<ServiceKey> Stations =
        [.. Enumerable.Range(0, 6).Select(at => ServiceKey.Of(7, 1_001 + at))];

    private readonly RepositoryDatabase database;

    private readonly HandTurnedClock clock = new(new DateTimeOffset(Now));

    private readonly string from = Directory.CreateTempSubdirectory("carina-shelf-source").FullName;

    private readonly string into = Directory.CreateTempSubdirectory("carina-shelf-root").FullName;

    public AShelfCarriedOverStaysUnmeasuredTests(RepositoryDatabase database)
    {
        this.database = database;
        AShelfOnDisk.LayDown(from, Shelf, NotARecording);
    }

    public void Dispose()
    {
        Directory.Delete(from, recursive: true);
        Directory.Delete(into, recursive: true);
    }

    [Fact(DisplayName = "BR-LD-007: every recording a migration carried reads unmeasured on the library's list, never good")]
    public async Task EveryRecordingAMigrationCarriedReadsUnmeasuredOnTheLibrarysListNeverGood()
    {
        await RunAsync();

        PaginatedList<Recording> shelf = await ListAsync();
        QualityBands bands = ShippedBands();

        IReadOnlyList<string> arrived = [.. Directory.GetFiles(into).Select(Path.GetFileName)!];

        Assert.Equal(Shelf, shelf.Total);
        Assert.Equal(Shelf, shelf.Items.Count);
        Assert.Equal(Shelf, arrived.Count);
        Assert.DoesNotContain(NotARecording, arrived);
        Assert.Equal(
            shelf.Items.Select(recording => recording.FileName.Value).Order(StringComparer.Ordinal),
            arrived.Order(StringComparer.Ordinal));
        Assert.All(shelf.Items, recording =>
        {
            RecordingQuality quality = RecordingQuality.Of(recording.Counters, recording.ScrambledPackets, bands);

            Assert.False(recording.Counters.Measured);
            Assert.Null(recording.Counters.Dropped);
            Assert.Null(recording.Counters.Total);
            Assert.Null(recording.ScrambledPackets);
            Assert.Null(recording.MeasuredUpdatedAt);
            Assert.Equal(QualityLevel.Unmeasured, quality.Overall);
            Assert.Equal(QualityLevel.Unmeasured, quality.Scrambled);
        });
    }

    [Fact(DisplayName = "BR-LD-007: the drop reading finds the whole carried shelf unmeasured and none of it clean or dropped")]
    public async Task TheDropReadingFindsTheWholeCarriedShelfUnmeasuredAndNoneOfItCleanOrDropped()
    {
        await RunAsync();

        Assert.Equal(Shelf, (await ListAsync(DropReading.Unmeasured)).Total);
        Assert.Equal(0, (await ListAsync(DropReading.Clean)).Total);
        Assert.Equal(0, (await ListAsync(DropReading.Dropped)).Total);
    }

    [Fact(DisplayName = "BR-QD-001: a period holding only what a migration carried reads unmeasured on every measure, with no share at all")]
    public async Task APeriodHoldingOnlyWhatAMigrationCarriedReadsUnmeasuredOnEveryMeasure()
    {
        await RunAsync();

        IReadOnlyList<QualityLedgerRow> rows = await ReadLedgerAsync();
        IReadOnlyList<QualityMeasure> whole = QualityBoard.Whole(rows, QualityMetrics.All, ShippedBands());

        Assert.Equal(Shelf, rows.Count);
        Assert.Equal(QualityMetrics.All, whole.Select(measure => measure.Metric));
        Assert.All(whole, measure =>
        {
            Assert.Equal(QualityState.Unmeasured, measure.Tally.State);
            Assert.Equal(Shelf, measure.Tally.Subjects);
            Assert.Equal(Shelf, measure.Tally.Unmeasured);
            Assert.Equal(0, measure.Tally.Measured);
            Assert.Equal(0, measure.Tally.Good);
            Assert.Null(measure.Tally.Average);
            Assert.Null(measure.Tally.Lowest);
            Assert.Null(measure.Tally.Highest);
        });
    }

    [Fact(DisplayName = "BR-QD-001: nothing a migration carried is listed as beyond a level or given a place when channels are put worst first")]
    public async Task NothingAMigrationCarriedIsListedAsBeyondALevelOrGivenAPlaceWhenChannelsArePutWorstFirst()
    {
        await RunAsync();

        IReadOnlyList<QualityLedgerRow> rows = await ReadLedgerAsync();
        QualityBands bands = ShippedBands();

        IReadOnlyList<QualityRowReading> read = QualitySurvey.Read(rows, QualityMetrics.All, bands);
        IReadOnlyList<QualityGroupReading> channels = QualityBoard.Sorted(
            QualityBoard.Grouped(rows, QualityAxis.Channel, QualityMetrics.All, bands),
            QualityGroupSort.Worst,
            QualityMetric.PacketsLost,
            ThresholdSense.Ceiling);

        Assert.DoesNotContain(read, reading => reading.WentBeyond);
        Assert.All(read, reading => Assert.Equal(QualityStanding.Unmeasured, reading.Standing));
        Assert.Equal(Stations.Count, channels.Count);
        Assert.Equal(Shelf, channels.Sum(channel => channel.Subjects));
        Assert.All(channels, channel => Assert.All(channel.Measures, measure =>
        {
            Assert.Equal(QualityState.Unmeasured, measure.Tally.State);
            Assert.Null(measure.Tally.Worst(ThresholdSense.Ceiling));
            Assert.Null(measure.Tally.Worst(ThresholdSense.Floor));
        }));
    }

    private static QualityBands ShippedBands()
        => QualityThresholdStanding.Bands(QualityThresholdStanding.Over([], Now));

    private async Task<PaginatedList<Recording>> ListAsync(DropReading? drops = null)
    {
        await using CarinaDbContext reading = database.Open();

        return await new RecordingDirectory(reading).ListAsync(
            RecordingQuery.For(
                null,
                null,
                perPage: RecordingQuery.MostPerPage,
                conditions: new RecordingConditions { Drops = drops })!,
            Cancel);
    }

    private async Task<IReadOnlyList<QualityLedgerRow>> ReadLedgerAsync()
    {
        await using CarinaDbContext reading = database.Open();

        return await new QualityLedgerReader(reading, new HeldStreams([]))
            .ReadAsync(QualityPeriod.Of(FirstAired.AddDays(-1), Now, Now)!, Cancel);
    }

    private async Task RunAsync()
    {
        await ClearAsync();
        await OfferOneDestinationAsync();

        await using CarinaDbContext context = database.Open();
        AShelfOnDisk source = new(from, Shelf, Stations, FirstAired);

        MigrationRunId id = await new MigrationPassage(
            source,
            source,
            new MigrationCarriage(
                new HardLinkMigrationCarrier(from, into, Root),
                new RecordingRepository(context),
                new RuleRepository(context),
                new EncodeJobRepository(context),
                new EncodeDestinationRepository(context),
                new EncodeProfileRepository(context),
                clock),
            new MigrationRecordRepository(context),
            new MigrationLease(context),
            clock).RunAsync(
                MigrationPass.ForReal,
                [.. Stations.Select((station, at) => new RescannedService(station, $"station {at + 1}"))],
                Cancel);

        MigrationReport? written = await new MigrationRecordRepository(context).ReadAsync(id, Cancel);
        MigrationTally recordings = written!.Tallies.Single(tally => tally.Population is MigrationPopulation.Recordings);

        Assert.Equal(Shelf, recordings.Carried);
    }

    private async Task OfferOneDestinationAsync()
    {
        await using CarinaDbContext context = database.Open();

        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Viewing"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            Began);

        await new EncodeProfileRepository(context).AddAsync(profile, Cancel);
        await new EncodeDestinationRepository(context).AddAsync(
            EncodeDestination.Define(
                EncodeDestinationId.New(),
                new EncodeLabel("Encodes"),
                new OutputRoot("encodes"),
                profile.Id,
                Began),
            Cancel);
    }

    private async Task ClearAsync()
    {
        await using CarinaDbContext context = database.Open();
        await using NpgsqlConnection connection = new(context.Database.GetConnectionString());
        await connection.OpenAsync(Cancel);

        string[] emptied =
        [
            "encode_job",
            "recording",
            "rule",
            "encode_destination",
            "encode_profile",
            "migration_run",
        ];

        foreach (string table in emptied)
        {
            await using NpgsqlCommand clearing = new($"DELETE FROM {table}", connection);
            await clearing.ExecuteNonQueryAsync(Cancel);
        }
    }
}

internal sealed class AShelfOnDisk(
    string directory,
    int count,
    IReadOnlyList<ServiceKey> stations,
    DateTime firstAired) : IMigrationSourceLedger, IMigrationSourceDirectory
{
    public static void LayDown(string directory, int count, string notARecording)
    {
        foreach (int at in Enumerable.Range(1, count))
        {
            File.WriteAllBytes(Path.Combine(directory, FileOf(at)), new byte[188 * at]);
        }

        File.WriteAllBytes(Path.Combine(directory, notARecording), new byte[64]);
    }

    public Task<SourceLedger> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(Ledger(
            [
                .. Enumerable.Range(1, count).Select(at => new SourceRecording(
                    at,
                    string.Create(CultureInfo.InvariantCulture, $"a carried programme {at}"),
                    firstAired.AddHours(60 * (at - 1)),
                    firstAired.AddHours(60 * (at - 1)).AddMinutes(30),
                    stations[(at - 1) % stations.Count],
                    new EventId(5_000 + at))),
            ],
            [.. Enumerable.Range(1, count).Select(at => AsBroadcast(at, FileOf(at), 188 * at))]));

    public Task<IReadOnlyList<SourceFile>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<SourceFile>>(
            [.. Directory
                .GetFiles(directory)
                .Order(StringComparer.Ordinal)
                .Select(file => OnDisk(Path.GetFileName(file), new FileInfo(file).Length))]);

    private static string FileOf(int at) => string.Create(CultureInfo.InvariantCulture, $"carried-{at:D2}.m2ts");
}
