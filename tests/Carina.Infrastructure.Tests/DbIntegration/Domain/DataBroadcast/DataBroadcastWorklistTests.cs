using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Fixtures.Integrity;
using Carina.Infrastructure.Tests.Fixtures.Recordings;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.DataBroadcast;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class DataBroadcastWorklistTests(RepositoryDatabase database, MigratedRepositoryDatabase migrated)
    : IClassFixture<MigratedRepositoryDatabase>
{
    private static readonly DateTime Now = new(2026, 10, 10, 6, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-BS-001: a recording still being written has no record due")]
    public async Task ARecordingStillBeingWrittenHasNoRecordDue()
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7301);

        Assert.DoesNotContain(await AwaitingAsync(alone), subject => subject.Id.Equals(recording.Id));
        Assert.Equal(DataBroadcastState.None, (await ReadAsync(recording.Id)).DataBroadcastState);
    }

    [Theory(DisplayName = "BR-BS-001: a recording that ended has its record coming however it ended, with its file and service")]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public async Task ARecordingThatEndedHasItsRecordComing(RecordingOutcome outcome)
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7302, new ServiceId(23610));
        await SettleAsync(recording.Id, outcome, Now.AddHours(1));

        DataBroadcastSubject subject = Assert.Single(await AwaitingAsync(alone));

        Assert.Equal((recording.Id, recording.FileName.Value, 23610), (subject.Id, subject.FileName.Value, subject.Service.Value));
        Assert.Equal(DataBroadcastState.Coming, (await ReadAsync(recording.Id)).DataBroadcastState);
    }

    [Fact(DisplayName = "BR-BS-001: the newest recording comes first, a pass takes no more than it asks, and one under a root out of reach is counted instead")]
    public async Task TheNewestComesFirstAndARootOutOfReachIsCounted()
    {
        OutputRoot alone = Alone();
        OutputRoot away = Alone();
        Recording earlier = await AddAsync(alone, 7303);
        Recording later = await AddAsync(alone, 7304);
        Recording elsewhere = await AddAsync(away, 7305);
        await SettleAsync(earlier.Id, RecordingOutcome.Complete, Now.AddHours(1));
        await SettleAsync(later.Id, RecordingOutcome.Complete, Now.AddHours(2));
        await SettleAsync(elsewhere.Id, RecordingOutcome.Complete, Now.AddHours(3));

        Assert.Equal([later.Id, earlier.Id], (await AwaitingAsync(alone)).Select(subject => subject.Id));
        Assert.Equal(later.Id, Assert.Single(await AwaitingAsync(alone, 1)).Id);
        Assert.DoesNotContain(await AwaitingAsync(alone), subject => subject.Id.Equals(elsewhere.Id));
        Assert.True(await AskAsync(worklist => worklist.WaitingOutOfReachAsync([alone], Cancel)) >= 1);
    }

    [Fact(DisplayName = "BR-BS-001: a record taken is made with its modules, and one taken with none is missing, neither of them coming")]
    public async Task ARecordTakenIsMadeOrMissing()
    {
        OutputRoot alone = Alone();
        Recording made = await EndedAsync(alone, 7306);
        Recording missing = await EndedAsync(alone, 7307);

        Assert.True(await AskAsync(worklist => worklist.TakenAsync(made.Id, 36, Cancel)));
        Assert.True(await AskAsync(worklist => worklist.TakenAsync(missing.Id, 0, Cancel)));

        Recording readMade = await ReadAsync(made.Id);
        Assert.Equal((DataBroadcastState.Made, (int?)36, (DateTime?)Now.AddHours(3)), (readMade.DataBroadcastState, readMade.DataBroadcastModules, readMade.DataBroadcastMadeAt));
        Assert.Equal(DataBroadcastState.Missing, (await ReadAsync(missing.Id)).DataBroadcastState);
        Assert.Empty(await AwaitingAsync(alone));
        Assert.Contains(made.Id, await AskAsync(worklist => worklist.MadeAsync(Cancel)));
        Assert.DoesNotContain(missing.Id, await AskAsync(worklist => worklist.MadeAsync(Cancel)));
    }

    [Fact(DisplayName = "BR-BS-001: a record that failed is tried again three times in all and then stays failed")]
    public async Task ARecordThatFailedIsTriedThreeTimesInAll()
    {
        OutputRoot alone = Alone();
        Recording recording = await EndedAsync(alone, 7308);

        for (int tried = 1; tried <= DataBroadcastProgress.TriesAtMost; tried++)
        {
            Assert.True(await AskAsync(worklist => worklist.FailedAsync(recording.Id, Cancel)));
            Assert.Empty(await AwaitingAsync(alone));
            await AskAsync(worklist => worklist.RetryFailedAsync(Cancel));
        }

        Recording read = await ReadAsync(recording.Id);
        Assert.Equal((DataBroadcastState.Failed, 3), (read.DataBroadcastState, read.DataBroadcastAttempts));
        Assert.Empty(await AwaitingAsync(alone));
    }

    [Fact(DisplayName = "BR-BS-001: a record that is made and no longer kept comes again")]
    public async Task ARecordMadeAndNoLongerKeptComesAgain()
    {
        OutputRoot alone = Alone();
        Recording recording = await EndedAsync(alone, 7309);
        await AskAsync(worklist => worklist.TakenAsync(recording.Id, 2, Cancel));

        Assert.True(await AskAsync(worklist => worklist.LostAsync(recording.Id, Cancel)));

        Assert.Equal(recording.Id, Assert.Single(await AwaitingAsync(alone)).Id);
    }

    [Fact(DisplayName = "BR-BS-001: a record already taken comes again once the recording is descrambled")]
    public async Task ARecordAlreadyTakenComesAgainOnceDescrambled()
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7310);
        await SettleAsync(recording.Id, RecordingOutcome.Truncated, Now.AddHours(1), scrambled: true);
        await AskAsync(worklist => worklist.TakenAsync(recording.Id, 0, Cancel));

        await DescrambleAsync(recording.Id, Now.AddHours(4));

        Assert.Equal(recording.Id, Assert.Single(await AwaitingAsync(alone)).Id);
    }

    [Fact(DisplayName = "BR-BS-001: a recording that ended with no record due, under a process that did not know the record, is put to coming")]
    public async Task ARecordingThatEndedWithNoRecordDueIsPutToComing()
    {
        OutputRoot alone = Alone();
        Recording ended = await EndedAsync(alone, 7311);
        Recording writing = await AddAsync(alone, 7312);

        await using (CarinaDbContext context = database.Open())
        {
            await context.Database.ExecuteSqlAsync($"UPDATE recording SET data_broadcast_state = 'None' WHERE id = {ended.Id.Value}", Cancel);
        }

        Assert.Empty(await AwaitingAsync(alone));
        Assert.True(await AskAsync(worklist => worklist.CatchUpEndedAsync(Cancel)) >= 1);

        Assert.Equal(ended.Id, Assert.Single(await AwaitingAsync(alone)).Id);
        Assert.Equal(DataBroadcastState.None, (await ReadAsync(writing.Id)).DataBroadcastState);
    }

    [Fact(DisplayName = "BR-BS-001: a recording no longer in the ledger is answered so, and nothing is written")]
    public async Task ARecordingNoLongerInTheLedgerIsAnsweredSo()
    {
        RecordingId gone = RecordingId.New();

        Assert.False(await AskAsync(worklist => worklist.TakenAsync(gone, 1, Cancel)));
        Assert.False(await AskAsync(worklist => worklist.FailedAsync(gone, Cancel)));
        Assert.False(await AskAsync(worklist => worklist.LostAsync(gone, Cancel)));
    }

    [Fact(DisplayName = "BR-BS-001: the recordings the data broadcast pass reads as waiting, puts to coming once ended and tries again are the ones each recording says so of, on the last try and the one before it")]
    public async Task TheRecordingsTheDataBroadcastPassMovesAreTheOnesEachSaysSoOf()
    {
        await using ServiceProvider container = migrated.Infrastructure();

        IReadOnlyList<Recording> laid = await RecordingMatrix.LaidAsync(container, RecordingMatrix.DataBroadcastShapes());

        await using (NpgsqlConnection connection = await migrated.OpenAsync())
        {
            Guid[] behind = [.. laid.Where(recording => recording.AwaitsItsDataBroadcast && recording.DataBroadcastAttempts == 0)
                .Where((_, index) => index % 3 == 0)
                .Select(recording => recording.Id.Value)];
            await using var forgetting = new NpgsqlCommand(
                "UPDATE recording SET data_broadcast_state = 'None' WHERE id = ANY(@ids)",
                connection);
            forgetting.Parameters.AddWithValue("ids", behind);
            await forgetting.ExecuteNonQueryAsync();
        }

        IReadOnlyList<Recording> matrix = await RecordingMatrix.ReadAsync(container);

        Assert.Contains(matrix, recording => recording.AwaitsItsDataBroadcast);
        Assert.Contains(matrix, recording => recording.DataBroadcastNeverCameDue);
        Assert.Contains(matrix, recording => recording.DataBroadcastIsDueAgain);
        Assert.Contains(matrix, recording => recording.DataBroadcastAttempts == DataBroadcastProgress.TriesAtMost);
        Assert.Contains(matrix, recording => recording is { IsInFlight: true, DataBroadcastState: DataBroadcastState.None });

        await using (AsyncServiceScope context = container.CreateAsyncScope())
        {
            IDataBroadcastWorklist worklist = context.ServiceProvider.GetRequiredService<IDataBroadcastWorklist>();

            IReadOnlyList<DataBroadcastSubject> within = await worklist.AwaitingAsync([RecordingMatrix.Bulk], 1_000, CancellationToken.None);
            int beyond = await worklist.WaitingOutOfReachAsync([RecordingMatrix.Bulk], CancellationToken.None);

            Assert.Equal(
                RecordingMatrix.Ids(matrix.Where(recording => recording.AwaitsItsDataBroadcast && recording.OutputRoot.Equals(RecordingMatrix.Bulk))),
                RecordingMatrix.Ids(within.Select(subject => subject.Id)));
            Assert.Equal(matrix.Count(recording => recording.AwaitsItsDataBroadcast && recording.OutputRoot.Equals(RecordingMatrix.Spare)), beyond);
            Assert.Equal(matrix.Count(recording => recording.DataBroadcastNeverCameDue), await worklist.CatchUpEndedAsync(CancellationToken.None));
            Assert.Equal(matrix.Count(recording => recording.DataBroadcastIsDueAgain), await worklist.RetryFailedAsync(CancellationToken.None));
        }

        Guid[] moved = RecordingMatrix.Ids(matrix.Where(recording => recording.DataBroadcastNeverCameDue || recording.DataBroadcastIsDueAgain));
        IReadOnlyList<Recording> after = await RecordingMatrix.ReadAsync(container);

        Assert.Equal(
            RecordingMatrix.Ids(matrix.Where(recording => recording.AwaitsItsDataBroadcast).Select(recording => recording.Id)).Concat(moved).Order().ToArray(),
            RecordingMatrix.Ids(after.Where(recording => recording.AwaitsItsDataBroadcast)));
    }

    private static OutputRoot Alone() => new("databroadcast" + Guid.NewGuid().ToString("N")[..12]);

    private async Task<T> AskAsync<T>(Func<IDataBroadcastWorklist, Task<T>> ask)
    {
        await using ServiceProvider container = database.Infrastructure(replace => replace.AddSingleton<TimeProvider>(new StoppedClock(Now.AddHours(3))));
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await ask(scope.ServiceProvider.GetRequiredService<IDataBroadcastWorklist>());
    }

    private async Task<IReadOnlyList<DataBroadcastSubject>> AwaitingAsync(OutputRoot within, int atMost = 64)
        => await AskAsync(worklist => worklist.AwaitingAsync([within], atMost, Cancel));

    private async Task<Recording> ReadAsync(RecordingId id)
    {
        await using CarinaDbContext context = database.Open();

        return await context.FindAsync<Recording>([id], Cancel)
            ?? throw new InvalidOperationException("The recording that was just written is not there.");
    }

    private async Task<Recording> EndedAsync(OutputRoot root, int eventId)
    {
        Recording recording = await AddAsync(root, eventId);
        await SettleAsync(recording.Id, RecordingOutcome.Complete, Now.AddHours(1));

        return recording;
    }

    private async Task<Recording> AddAsync(OutputRoot root, int eventId, ServiceId? service = null)
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), service ?? new ServiceId(1024), new EventId(eventId), Now),
            root,
            RecordingFileName.For(id, ".m2ts"),
            Now,
            Now.AddHours(1),
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Now,
            new TunerDeviceId("pt3-0"));

        recording.Wrote(TimeSpan.FromMinutes(30));

        await using CarinaDbContext context = database.Open();
        context.Add(recording);
        await context.SaveChangesAsync(Cancel);

        return recording;
    }

    private async Task SettleAsync(RecordingId id, RecordingOutcome outcome, DateTime stopped, bool scrambled = false)
    {
        await using CarinaDbContext context = database.Open();
        Recording loaded = await context.FindAsync<Recording>([id], Cancel)
            ?? throw new InvalidOperationException("The recording that was just written is not there.");

        loaded.Abort(stopped);

        if (outcome is not RecordingOutcome.Complete)
        {
            loaded.Note(new OutcomeDetail(RecordingFault.DiskExhausted, null, "no room left", stopped));
        }

        if (scrambled)
        {
            loaded.Note(new OutcomeDetail(RecordingFault.ScramblingUnresolved, null, "left scrambled", stopped));
        }

        loaded.Settle(outcome, outcome is RecordingOutcome.Failed ? 0 : 1_200_000, stopped);
        await context.SaveChangesAsync(Cancel);
    }

    private async Task DescrambleAsync(RecordingId id, DateTime at)
    {
        await using CarinaDbContext context = database.Open();
        Recording loaded = await context.FindAsync<Recording>([id], Cancel)
            ?? throw new InvalidOperationException("The recording that was just written is not there.");

        loaded.Descrambled(at);
        await context.SaveChangesAsync(Cancel);
    }
}
