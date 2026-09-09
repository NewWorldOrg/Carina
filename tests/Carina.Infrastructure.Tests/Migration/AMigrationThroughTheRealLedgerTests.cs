using Carina.Domain.Encodings;
using Carina.Domain.Migration;
using Carina.Domain.Recordings;
using Carina.Domain.Rules;
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
public sealed class AMigrationThroughTheRealLedgerTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly RepositoryDatabase database;

    private readonly HandTurnedClock clock = new(new DateTimeOffset(2026, 9, 8, 5, 0, 0, TimeSpan.Zero));

    private readonly string from = Directory.CreateTempSubdirectory("carina-migration-ledger-source").FullName;

    private readonly string into = Directory.CreateTempSubdirectory("carina-migration-ledger-root").FullName;

    public AMigrationThroughTheRealLedgerTests(RepositoryDatabase database)
    {
        this.database = database;
        MigrationOnRealFiles.LayDown(from);
    }

    public void Dispose()
    {
        Directory.Delete(from, recursive: true);
        Directory.Delete(into, recursive: true);
    }

    [Fact]
    public async Task WhatARunForRealCarriesIsTakenByEveryLedgerTheNewSystemKeeps()
    {
        MigrationRunId id = await RunAsync();

        await using CarinaDbContext reading = database.Open();

        IReadOnlyList<Recording> held = await reading.Set<Recording>().ToListAsync(Cancel);
        IReadOnlyList<Rule> made = await new RuleRepository(reading).ListAsync(Cancel);
        IReadOnlyList<EncodeJob> queued = await reading.Set<EncodeJob>().ToListAsync(Cancel);
        MigrationReport? written = await new MigrationRecordRepository(reading).ReadAsync(id, Cancel);

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
        await RunAsync();

        await using CarinaDbContext reading = database.Open();

        IReadOnlyList<Recording> held = await reading.Set<Recording>().ToListAsync(Cancel);
        IReadOnlyList<string> arrived = [.. Directory.GetFiles(into).Select(Path.GetFileName)!];

        Assert.Equal(2, held.Count);
        Assert.Equal(2, arrived.Count);
        Assert.DoesNotContain(
            arrived,
            name => name is MigrationOnRealFiles.NotARecording or MigrationOnRealFiles.NothingLandedIn);
        Assert.All(
            held,
            recording => Assert.EndsWith(
                Carina.Contracts.RecordingFile.Extension,
                recording.FileName.Value,
                StringComparison.Ordinal));
        Assert.All(held, recording => Assert.True(recording.FileSizeObserved > 0));
    }

    private async Task<MigrationRunId> RunAsync()
    {
        await ClearAsync();
        await OfferOneDestinationAsync();

        await using CarinaDbContext context = database.Open();
        ASourceOfRealFiles source = new(from);

        return await new MigrationPassage(
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
            clock).RunAsync(MigrationPass.ForReal, Rescanned(), Cancel);
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
