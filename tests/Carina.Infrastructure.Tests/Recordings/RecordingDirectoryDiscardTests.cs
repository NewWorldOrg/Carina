using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Recordings;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingDirectoryDiscardTests(RepositoryDatabase database)
{
    private static readonly DateTime Airs = new(2026, 8, 27, 20, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ARecordingThatHasEndedLeavesTheTableAltogether()
    {
        Recording ended = await WrittenAsync(5001, settled: true);
        Recording beside = await WrittenAsync(5002, settled: true);

        await using (CarinaDbContext discarding = database.Open())
        {
            Assert.Equal(
                RecordingDiscard.Discarded,
                await new RecordingDirectory(discarding).DiscardAsync(ended.Id, Cancel));
        }

        await using CarinaDbContext reading = database.Open();

        Assert.Null(await new RecordingDirectory(reading).FindAsync(ended.Id, Cancel));
        Assert.NotNull(await new RecordingDirectory(reading).FindAsync(beside.Id, Cancel));
        Assert.Equal(0, await Rows(ended.Id));
        Assert.Equal(1, await Rows(beside.Id));
    }

    [Fact]
    public async Task ARecordingStillBeingWrittenKeepsItsRow()
    {
        Recording writing = await WrittenAsync(5011, settled: false);

        await using (CarinaDbContext discarding = database.Open())
        {
            Assert.Equal(
                RecordingDiscard.StillRecording,
                await new RecordingDirectory(discarding).DiscardAsync(writing.Id, Cancel));
        }

        Assert.Equal(1, await Rows(writing.Id));
    }

    [Fact]
    public async Task ARecordingNothingEverWroteDownIsSaidToBeMissingRatherThanDiscarded()
    {
        await using CarinaDbContext discarding = database.Open();

        Assert.Equal(
            RecordingDiscard.NoSuchRecording,
            await new RecordingDirectory(discarding).DiscardAsync(RecordingId.New(), Cancel));
    }

    [Fact]
    public async Task DiscardingTheSameRecordingTwiceSaysItIsGoneTheSecondTime()
    {
        Recording ended = await WrittenAsync(5021, settled: true);

        await using CarinaDbContext discarding = database.Open();
        var directory = new RecordingDirectory(discarding);

        Assert.Equal(RecordingDiscard.Discarded, await directory.DiscardAsync(ended.Id, Cancel));
        Assert.Equal(RecordingDiscard.NoSuchRecording, await directory.DiscardAsync(ended.Id, Cancel));
    }

    private async Task<int> Rows(RecordingId id)
    {
        await using CarinaDbContext counting = database.Open();

        return await counting.Set<Recording>().CountAsync(recording => recording.Id == id, Cancel);
    }

    private async Task<Recording> WrittenAsync(int eventId, bool settled)
    {
        RecordingId id = RecordingId.New();
        Recording begun = Recording.Begin(
            id,
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), Airs),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            Airs,
            Airs.AddMinutes(30),
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Airs.AddHours(-6)),
            null,
            BroadcastGroupRole.Standalone,
            Airs,
            new TunerDeviceId("adapter0"));

        await using CarinaDbContext writing = database.Open();
        var repository = new RecordingRepository(writing);
        await repository.AddAsync(begun, Cancel);

        if (settled)
        {
            begun.Abort(Airs.AddMinutes(30));
            begun.Settle(RecordingOutcome.Complete, 1_000_000, Airs.AddMinutes(30));
            await repository.SaveAsync(begun, Cancel);
        }

        return begun;
    }
}
