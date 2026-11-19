using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Encodings;
using Carina.Domain.Migration;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Rules;
using Carina.Infrastructure.Migration;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Migration;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using static Carina.TestSupport.CarriedMigrationFixtures;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Migration;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class MigrationPassageTests : IDisposable
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

    private readonly string from = Directory.CreateTempSubdirectory("carina-migration-ledger-source").FullName;

    private readonly string into = Directory.CreateTempSubdirectory("carina-migration-ledger-root").FullName;

    public MigrationPassageTests(RepositoryDatabase database) => this.database = database;

    public void Dispose() => MigrationOnRealFiles.RemoveAll(from, into);

    [Fact]
    public async Task WhatARunForRealCarriesIsTakenByEveryLedgerTheNewSystemKeeps()
    {
        MigrationRunId id = await CarryRealFilesAsync();

        await using ServiceProvider container = database.Infrastructure();
        await using AsyncServiceScope reading = container.CreateAsyncScope();
        CarinaDbContext context = reading.ServiceProvider.GetRequiredService<CarinaDbContext>();

        IReadOnlyList<Recording> held = await context.Set<Recording>().ToListAsync(Cancel);
        IReadOnlyList<Rule> made = await reading.ServiceProvider.GetRequiredService<IRuleRepository>().ListAsync(Cancel);
        IReadOnlyList<EncodeJob> queued = await context.Set<EncodeJob>().ToListAsync(Cancel);
        MigrationReport? written = await reading.ServiceProvider.GetRequiredService<IMigrationRecordRepository>().ReadAsync(id, Cancel);

        Assert.Equal(2, held.Count);
        Assert.All(held, recording => Assert.Equal(RecordingOutcome.Complete, recording.Outcome));
        Assert.All(held, recording => Assert.Equal(Root, recording.OutputRoot));
        Assert.Equal(
            held.Select(recording => recording.FileName.Value).Order(StringComparer.Ordinal),
            Directory.GetFiles(into).Select(Path.GetFileName).Order(StringComparer.Ordinal));

        Assert.Equal(
            held.Select(recording => recording.Id.Wire).Order(StringComparer.Ordinal),
            queued.Select(job => job.RecordingId.Wire).Order(StringComparer.Ordinal));

        Assert.False(Assert.Single(made).Enabled);

        Assert.NotNull(written);
        Assert.Equal(MigrationPass.ForReal, written.Run.Pass);

        MigrationTally recordings = written.Tallies.Single(
            tally => tally.Population is MigrationPopulation.Recordings);

        Assert.Equal(3, recordings.Offered);
        Assert.Equal(2, recordings.Carried);
        Assert.Equal(1, recordings.NotCarried);
        Assert.Equal(0, recordings.Unclassified);
    }

    [Fact]
    public async Task NothingThatWasNotARecordingBecomesOneInTheLedger()
    {
        await CarryRealFilesAsync();

        await using ServiceProvider container = database.Infrastructure();
        await using AsyncServiceScope reading = container.CreateAsyncScope();
        CarinaDbContext context = reading.ServiceProvider.GetRequiredService<CarinaDbContext>();

        IReadOnlyList<Recording> held = await context.Set<Recording>().ToListAsync(Cancel);
        IReadOnlyList<string> arrived = [.. Directory.GetFiles(into).Select(Path.GetFileName)!];

        Assert.Equal(2, held.Count);
        Assert.Equal(2, arrived.Count);
        Assert.DoesNotContain(
            arrived,
            name => name is MigrationOnRealFiles.NotARecording or MigrationOnRealFiles.NothingLandedIn);
        Assert.All(
            held,
            recording => Assert.EndsWith(
                Puppis.Contracts.RecordingFile.Extension,
                recording.FileName.Value,
                StringComparison.Ordinal));
        Assert.All(held, recording => Assert.True(recording.FileSizeObserved > 0));
    }

    [Fact(DisplayName = "every recording a migration carried reads unmeasured on the library's list, never good")]
    public async Task EveryRecordingAMigrationCarriedReadsUnmeasuredOnTheLibrarysListNeverGood()
    {
        await CarryTheShelfAsync();

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
            RecordingQuality quality = RecordingQuality.Of(recording, bands);

            Assert.False(recording.Counters.Measured);
            Assert.Null(recording.Counters.Dropped);
            Assert.Null(recording.Counters.Total);
            Assert.Null(recording.ScrambledPackets);
            Assert.Null(recording.MeasuredUpdatedAt);
            Assert.Equal(QualityLevel.Unmeasured, quality.Overall);
            Assert.Equal(QualityLevel.Unmeasured, quality.Scrambled);
        });
    }

    [Fact(DisplayName = "the drop reading finds the whole carried shelf unmeasured and none of it clean or dropped")]
    public async Task TheDropReadingFindsTheWholeCarriedShelfUnmeasuredAndNoneOfItCleanOrDropped()
    {
        await CarryTheShelfAsync();

        Assert.Equal(Shelf, (await ListAsync(DropReading.Unmeasured)).Total);
        Assert.Equal(0, (await ListAsync(DropReading.Clean)).Total);
        Assert.Equal(0, (await ListAsync(DropReading.Dropped)).Total);
    }

    [Fact(DisplayName = "a period holding only what a migration carried reads unmeasured on every measure, with no share at all")]
    public async Task APeriodHoldingOnlyWhatAMigrationCarriedReadsUnmeasuredOnEveryMeasure()
    {
        await CarryTheShelfAsync();

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

    [Fact(DisplayName = "nothing a migration carried is listed as beyond a level or given a place when channels are put worst first")]
    public async Task NothingAMigrationCarriedIsListedAsBeyondALevelOrGivenAPlaceWhenChannelsArePutWorstFirst()
    {
        await CarryTheShelfAsync();

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
        await using ServiceProvider container = database.Infrastructure();
        await using AsyncServiceScope reading = container.CreateAsyncScope();

        return (await reading.ServiceProvider.GetRequiredService<IRecordingDirectory>().ListAsync(
            RecordingQuery.For(
                null,
                null,
                perPage: RecordingQuery.MostPerPage,
                conditions: new RecordingConditions { Drops = drops })!,
            ShippedBands(),
            Cancel)).Found;
    }

    private async Task<IReadOnlyList<QualityLedgerRow>> ReadLedgerAsync()
    {
        await using ServiceProvider container = database.Infrastructure(
            replace => replace.AddScoped<IBroadcastStreamDirectory>(_ => new HeldStreams([])));
        await using AsyncServiceScope reading = container.CreateAsyncScope();

        return await reading.ServiceProvider.GetRequiredService<IQualityLedgerReader>()
            .ReadAsync(QualityPeriod.Of(FirstAired.AddDays(-1), Now, Now)!, Cancel);
    }

    private Task<MigrationRunId> CarryRealFilesAsync()
    {
        MigrationOnRealFiles.LayDown(from);

        return CarryAsync(new ASourceOfRealFiles(from), Rescanned());
    }

    private async Task CarryTheShelfAsync()
    {
        AShelfOnDisk.LayDown(from, Shelf, NotARecording);

        MigrationRunId id = await CarryAsync(
            new AShelfOnDisk(from, Shelf, Stations, FirstAired),
            [.. Stations.Select((station, at) => new RescannedService(station, $"station {at + 1}"))]);

        await using ServiceProvider container = database.Infrastructure();
        await using AsyncServiceScope reading = container.CreateAsyncScope();

        MigrationReport? written = await reading.ServiceProvider.GetRequiredService<IMigrationRecordRepository>().ReadAsync(id, Cancel);
        MigrationTally recordings = written!.Tallies.Single(tally => tally.Population is MigrationPopulation.Recordings);

        Assert.Equal(Shelf, recordings.Carried);
    }

    private async Task<MigrationRunId> CarryAsync<TSource>(TSource source, IReadOnlyList<RescannedService> rescanned)
        where TSource : class, IMigrationSourceLedger, IMigrationSourceDirectory
    {
        await ClearAsync();
        await OfferOneDestinationAsync();

        await using ServiceProvider container = database.Infrastructure(replace =>
        {
            replace.AddSingleton<IMigrationSourceLedger>(source);
            replace.AddSingleton<IMigrationSourceDirectory>(source);
            replace.AddSingleton<IMigrationCarrier>(new HardLinkMigrationCarrier(from, into, Root));
            replace.AddScoped<MigrationCarriageDomainService>();
            replace.AddScoped<IMigrationLease, MigrationLease>();
            replace.AddSingleton<TimeProvider>(clock);
        });
        await using AsyncServiceScope carrying = container.CreateAsyncScope();

        return await ActivatorUtilities.CreateInstance<MigrationPassage>(carrying.ServiceProvider)
            .RunAsync(MigrationPass.ForReal, rescanned, Cancel);
    }

    private async Task OfferOneDestinationAsync()
    {
        await using ServiceProvider container = database.Infrastructure();
        await using AsyncServiceScope offering = container.CreateAsyncScope();

        EncodeProfile profile = EncodeProfile.Define(
            EncodeProfileId.New(),
            new EncodeLabel("Viewing"),
            EncodeCodec.H264,
            EncodeResolution.AsSource,
            Deinterlace.EveryFrame,
            new ConstantRateFactor(22),
            new ConstantQuantiser(24),
            Began);

        await offering.ServiceProvider.GetRequiredService<IEncodeProfileRepository>().AddAsync(profile, Cancel);
        await offering.ServiceProvider.GetRequiredService<IEncodeDestinationRepository>().AddAsync(
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
