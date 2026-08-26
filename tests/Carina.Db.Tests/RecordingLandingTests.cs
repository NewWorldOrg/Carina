using System.Text.Json;

using Carina.Api.Common;
using Carina.Api.Responder.Recordings;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingLandingTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const int Network = 39_101;

    private static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EveryClassTheLedgerHoldsIsStillToldApartOnTheWire()
    {
        Recording written = await WrittenAsync(1);

        await using CarinaDbContext context = Context();
        Recording read = await Read(context, written.Id);
        JsonElement wire = Wire(RecordingDetailResponder.Of(read));
        JsonElement recording = wire.GetProperty("recording");
        JsonElement reasons = recording.GetProperty("outcomeDetail");

        Assert.Equal(JsonValueKind.Array, reasons.ValueKind);
        Assert.Equal(2, reasons.GetArrayLength());
        Assert.Equal("tuneFailed", reasons[0].GetProperty("fault").GetString());
        Assert.Equal("incompletePsi", reasons[0].GetProperty("tuneFailure").GetString());
        Assert.Equal("the table never completed", reasons[0].GetProperty("note").GetString());
        Assert.Equal("scramblingUnresolved", reasons[1].GetProperty("fault").GetString());
        Assert.Equal(JsonValueKind.Null, reasons[1].GetProperty("tuneFailure").ValueKind);
        Assert.Equal("the card said no", reasons[1].GetProperty("note").GetString());

        JsonElement drops = recording.GetProperty("drops");

        Assert.False(drops.GetProperty("ccMeasured").GetBoolean());
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("ccDroppedPackets").ValueKind);
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("ccTotalPackets").ValueKind);
        Assert.Equal(JsonValueKind.Null, drops.GetProperty("scrambledPackets").ValueKind);

        JsonElement picture = recording.GetProperty("thumbnail");

        Assert.Equal("failed", picture.GetProperty("state").GetString());
        Assert.Equal("sourceOutOfReach", picture.GetProperty("fault").GetString());

        JsonElement broke = wire.GetProperty("interruptions");

        Assert.Equal(2, broke.GetArrayLength());
        Assert.Equal("driverLost", broke[0].GetProperty("fault").GetString());
        Assert.NotEqual(JsonValueKind.Null, broke[0].GetProperty("resumedAt").ValueKind);
        Assert.Equal("diskExhausted", broke[1].GetProperty("fault").GetString());

        JsonElement weighed = wire.GetProperty("reconciliation");

        Assert.True(weighed.GetProperty("sizeObserved").GetBoolean());
        Assert.Equal(1_234_567, weighed.GetProperty("fileSizeBytes").GetInt64());
        Assert.Equal(0.5, weighed.GetProperty("coverage").GetDouble(), 6);
    }

    [Fact]
    public async Task TheSameClassesReachTheWireThroughTheListAsThroughTheDetail()
    {
        Recording written = await WrittenAsync(2);

        await using CarinaDbContext context = Context();
        PaginatedList<Recording> found = await new RecordingDirectory(context).ListAsync(
            OnlyThisOne(written),
            CancellationToken.None);

        JsonElement wire = Wire(RecordingListResponder.Of(found));
        JsonElement items = wire.GetProperty("items");

        Assert.Equal(1, wire.GetProperty("total").GetInt32());
        Assert.Equal(1, items.GetArrayLength());

        JsonElement row = items[0];

        Assert.Equal(written.Id.Wire, row.GetProperty("id").GetString());
        Assert.Equal("truncated", row.GetProperty("outcome").GetString());
        Assert.Equal("ended", row.GetProperty("standing").GetString());
        Assert.Equal(2, row.GetProperty("outcomeDetail").GetArrayLength());
        Assert.Equal("tuneFailed", row.GetProperty("outcomeDetail")[0].GetProperty("fault").GetString());
        Assert.False(row.GetProperty("drops").GetProperty("ccMeasured").GetBoolean());
        Assert.Equal("failed", row.GetProperty("thumbnail").GetProperty("state").GetString());
        Assert.Equal("sourceOutOfReach", row.GetProperty("thumbnail").GetProperty("fault").GetString());
    }

    [Fact]
    public async Task ARecordingThatWasCountedReachesTheWireWithBothNumbersBesideTheFlag()
    {
        RecordingId id = RecordingId.New();
        Recording recording = Begin(id, 3);

        recording.Measure(
            DropCounters.Counted(7, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 7, 0)], []),
            null,
            2,
            Noon.AddMinutes(1));

        await AddAsync(recording);

        await using CarinaDbContext context = Context();
        JsonElement wire = Wire(RecordingDetailResponder.Of(await Read(context, id)));
        JsonElement drops = wire.GetProperty("recording").GetProperty("drops");

        Assert.True(drops.GetProperty("ccMeasured").GetBoolean());
        Assert.Equal(7, drops.GetProperty("ccDroppedPackets").GetInt64());
        Assert.Equal(1000, drops.GetProperty("ccTotalPackets").GetInt64());
        Assert.Equal(2, drops.GetProperty("eovfCount").GetInt64());

        JsonElement positions = wire.GetProperty("positions");

        Assert.True(positions.GetProperty("located").GetBoolean());
        Assert.Equal(900_000, positions.GetProperty("anchorPcr").GetInt64());
        Assert.Equal(12, positions.GetProperty("buckets")[0].GetProperty("second").GetInt32());
        Assert.Equal(7, positions.GetProperty("buckets")[0].GetProperty("continuity").GetInt64());
    }

    private static RecordingQuery OnlyThisOne(Recording recording)
        => RecordingQuery.For(
               null,
               null,
               conditions: new RecordingConditions
               {
                   Channels = [new ProgrammeService(recording.NetworkId.Value, recording.ServiceId.Value)],
               })
           ?? throw new InvalidOperationException("The query this test asks for is one the guard takes.");

    private static JsonElement Wire<T>(T responder)
        => JsonDocument.Parse(JsonSerializer.Serialize(responder, WireJson.Options)).RootElement.Clone();

    private static Recording Begin(RecordingId id, int eventId)
        => Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(Network), new ServiceId(1024 + eventId), new EventId(eventId), Noon),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Noon,
            Noon.AddHours(1),
            new ProgrammeSnapshot("A programme", "What it is about", string.Empty, [], Noon),
            null,
            BroadcastGroupRole.Standalone,
            Noon,
            new TunerDeviceId("pt3-0"));

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private static async Task<Recording> Read(CarinaDbContext context, RecordingId id)
        => await new RecordingDirectory(context).FindAsync(id, CancellationToken.None)
           ?? throw new InvalidOperationException("The recording this test wrote is not in the ledger.");

    private async Task AddAsync(Recording recording)
    {
        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();
    }

    private async Task<Recording> WrittenAsync(int eventId)
    {
        RecordingId id = RecordingId.New();
        Recording recording = Begin(id, eventId);

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Interrupt(RecordingFault.DriverLost, Noon.AddMinutes(5));
        recording.Resume(Noon.AddMinutes(6));
        recording.Interrupt(RecordingFault.DiskExhausted, Noon.AddMinutes(9));
        recording.Note(new OutcomeDetail(
            RecordingFault.TuneFailed,
            TuneFailureKind.IncompletePsi,
            "the table never completed",
            Noon.AddMinutes(10)));
        recording.Note(new OutcomeDetail(
            RecordingFault.ScramblingUnresolved,
            null,
            "the card said no",
            Noon.AddMinutes(11)));
        recording.Settle(RecordingOutcome.Truncated, 1_234_567, Noon.AddMinutes(30));
        recording.Illustrate(ThumbnailState.Failed, ThumbnailFault.SourceOutOfReach);

        await AddAsync(recording);

        return recording;
    }
}
