using Carina.Domain.Channels;
using Carina.Domain.Integrity;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Integrity;
using Carina.Infrastructure.Persistence;

namespace Carina.Infrastructure.Tests.Integrity;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingLedgerTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 26, 4, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ARecordingStillBeingWrittenComesBackWithNoSizeToCompareAgainst()
    {
        Recording recording = await AddAsync(6101, "primary");

        LedgerFile row = await FindAsync(recording.Id);

        Assert.Null(row.SizeObserved);
        Assert.Equal("primary", row.Root.Value);
        Assert.Equal(recording.FileName.Value, row.FileName.Value);
    }

    [Fact]
    public async Task ARecordingThatEndedComesBackWithTheSizeItWasWeighedAt()
    {
        Recording recording = await AddAsync(6102, "primary");
        await SettleAsync(recording.Id, RecordingOutcome.Complete, 3_400_000_000);

        Assert.Equal(3_400_000_000, (await FindAsync(recording.Id)).SizeObserved);
    }

    [Fact]
    public async Task ARecordingThatEndedEmptyComesBackWithThatSizeRatherThanWithNone()
    {
        Recording recording = await AddAsync(6103, "primary");
        await SettleAsync(recording.Id, RecordingOutcome.Failed, 0);

        Assert.Equal(0, (await FindAsync(recording.Id)).SizeObserved);
    }

    [Fact]
    public async Task TheLedgerComesBackWithTheRootEachRecordingWasWrittenUnder()
    {
        Recording under = await AddAsync(6104, "bulk");

        Assert.Equal("bulk", (await FindAsync(under.Id)).Root.Value);
    }

    [Fact]
    public async Task EveryRecordingInTheLedgerComesBackFromOneRead()
    {
        Recording first = await AddAsync(6105, "primary");
        Recording second = await AddAsync(6106, "primary");

        IReadOnlyList<LedgerFile> rows = await ReadAsync();

        Assert.Contains(rows, row => row.Id.Equals(first.Id));
        Assert.Contains(rows, row => row.Id.Equals(second.Id));
    }

    private async Task<LedgerFile> FindAsync(RecordingId id)
        => Assert.Single(await ReadAsync(), row => row.Id.Equals(id));

    private async Task<IReadOnlyList<LedgerFile>> ReadAsync()
    {
        await using CarinaDbContext context = database.Open();

        return await new RecordingLedger(context).ListAsync(Cancel);
    }

    private async Task<Recording> AddAsync(int eventId, string outputRoot)
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), Now),
            new OutputRoot(outputRoot),
            RecordingFileName.For(id, ".m2ts"),
            Now,
            Now.AddHours(1),
            new ProgrammeSnapshot("A programme", "What it is about", string.Empty, [], Now),
            null,
            BroadcastGroupRole.Standalone,
            Now);

        await using CarinaDbContext context = database.Open();
        context.Add(recording);
        await context.SaveChangesAsync(Cancel);

        return recording;
    }

    private async Task SettleAsync(RecordingId id, RecordingOutcome outcome, long fileSizeObserved)
    {
        await using CarinaDbContext context = database.Open();
        Recording loaded = await context.FindAsync<Recording>([id], Cancel)
            ?? throw new InvalidOperationException("The recording that was just written is not there.");

        loaded.Abort(Now.AddHours(1));

        if (outcome is not RecordingOutcome.Complete)
        {
            loaded.Note(new OutcomeDetail(RecordingFault.DiskExhausted, null, "no room left", Now.AddHours(1)));
        }

        loaded.Settle(outcome, fileSizeObserved, Now.AddHours(1));
        await context.SaveChangesAsync(Cancel);
    }
}
