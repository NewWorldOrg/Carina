using Carina.Domain.Migration;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Npgsql;

namespace Carina.Db.Tests;

/// <summary>
/// The span a recording carried over has to last, held three ways: the classifier of the migration, the recording's
/// own way of being carried over, and the recording ledger's check on its window. A recording one microsecond long,
/// the shortest the ledger keeps, is carried and written; one that ends on the instant it begins is refused by all
/// three.
/// </summary>
[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class MigrationArrivalArmsTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Began = new(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime CarriedAt = new(2026, 9, 8, 5, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Microsecond = TimeSpan.FromTicks(10);

    private static readonly ServiceKey Service = ServiceKey.Of(32736, 1024);

    [Fact(DisplayName = "BR-MS-001: a recording one microsecond long is carried by the classifier, made by the recording and kept by the ledger, recorded in full")]
    public async Task ARecordingOneMicrosecondLongIsCarriedAndKept()
    {
        await ClearedAsync();
        SourceRecording source = new(7, "a programme", Began, Began + Microsecond, Service, new EventId(4321));

        Assert.True(Judged(source).Carried);

        Recording carried = Carried(source);

        await using (CarinaDbContext writing = Context())
        {
            await new RecordingRepository(writing).AddAsync(carried, CancellationToken.None);
        }

        await using CarinaDbContext reading = Context();
        Recording? kept = await new RecordingRepository(reading).FindAsync(carried.Id, CancellationToken.None);

        Assert.NotNull(kept);

        Assert.Equal(RecordingOutcome.Complete, kept.Outcome);
        Assert.Equal(Began, kept.ExpectedWindowStart);
        Assert.Equal(Began + Microsecond, kept.ExpectedWindowEnd);
    }

    [Fact(DisplayName = "BR-MS-001: a recording that ends on the instant it begins is refused by the classifier, by the recording and by the ledger's check on its window")]
    public async Task ARecordingThatEndsAsItBeginsIsRefusedThreeWays()
    {
        await ClearedAsync();
        SourceRecording source = new(7, "a programme", Began, Began, Service, new EventId(4321));

        Assert.Equal(MigrationRefusal.Inexpressible, Judged(source).Refusal);
        Assert.Throws<ArgumentException>(() => Carried(source));

        Recording lasting = Carried(new SourceRecording(
            8,
            "a programme",
            Began,
            Began + Microsecond,
            Service,
            new EventId(4322)));

        await using (CarinaDbContext writing = Context())
        {
            await new RecordingRepository(writing).AddAsync(lasting, CancellationToken.None);
        }

        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var shortening = new NpgsqlCommand(
            "UPDATE recording SET expected_window_end = expected_window_start WHERE id = @id",
            connection);
        shortening.Parameters.AddWithValue("id", lasting.Id.Value);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => shortening.ExecuteNonQueryAsync());

        Assert.Equal("ck_recording_window", refusal.ConstraintName);
    }

    private static MigrationVerdict Judged(SourceRecording source)
        => MigrationClassifier.OnARecording(
            source,
            new SourceRecordingFile(source.Id, "one.m2ts", SourceFileKind.AsBroadcast, 100),
            new SourceFile("one.m2ts", 100));

    private static Recording Carried(SourceRecording source)
    {
        ProgrammeRef programme = source.Programmed
            ?? throw new ArgumentException("A recording carried over names its programme.", nameof(source));
        RecordingId id = RecordingId.New();

        return Recording.CarriedOver(
            id,
            programme,
            source.Name,
            new OutputRoot("carried"),
            RecordingFileName.For(id, ".m2ts"),
            100,
            source.EndAt,
            CarriedAt);
    }

    private async Task ClearedAsync()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var clearing = new NpgsqlCommand("TRUNCATE recording CASCADE", connection);
        await clearing.ExecuteNonQueryAsync();
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);
}
