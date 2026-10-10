using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Integrity;
using Carina.Infrastructure.Tests.Reservations;

namespace Carina.Infrastructure.Tests.DataBroadcast;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class DataBroadcastWorklistTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 10, 10, 6, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-BS-001: a recording still being written has no record due and says something is being recorded")]
    public async Task ARecordingStillBeingWrittenHasNoRecordDue()
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7301);

        Assert.DoesNotContain(await AwaitingAsync(alone), subject => subject.Id.Equals(recording.Id));
        Assert.Equal(DataBroadcastState.None, (await ReadAsync(recording.Id)).DataBroadcastState);
        Assert.True(await AskAsync(worklist => worklist.AnyBeingRecordedAsync(Cancel)));
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

    [Fact(DisplayName = "BR-BS-001: a recording no longer in the ledger is answered so, and nothing is written")]
    public async Task ARecordingNoLongerInTheLedgerIsAnsweredSo()
    {
        RecordingId gone = RecordingId.New();

        Assert.False(await AskAsync(worklist => worklist.TakenAsync(gone, 1, Cancel)));
        Assert.False(await AskAsync(worklist => worklist.FailedAsync(gone, Cancel)));
        Assert.False(await AskAsync(worklist => worklist.LostAsync(gone, Cancel)));
    }

    [Fact(DisplayName = "BR-BS-001: a reservation still to be recorded is found when it starts, its margin included, within the moments asked about")]
    public async Task AReservationStillToBeRecordedIsFoundWithinTheMomentsAskedAbout()
    {
        DateTime starts = new(2031, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        await PlanAsync(starts, Margin.OfSeconds(600));

        Assert.True(await AskAsync(worklist => worklist.AnyReservationStartingAsync(starts.AddMinutes(-35), starts.AddMinutes(-5), Cancel)));
        Assert.True(await AskAsync(worklist => worklist.AnyReservationStartingAsync(starts.AddMinutes(-40), starts.AddMinutes(-10), Cancel)));
        Assert.False(await AskAsync(worklist => worklist.AnyReservationStartingAsync(starts.AddMinutes(-45), starts.AddMinutes(-15), Cancel)));
        Assert.False(await AskAsync(worklist => worklist.AnyReservationStartingAsync(starts.AddHours(2), starts.AddHours(2).AddMinutes(30), Cancel)));
    }

    private static OutputRoot Alone() => new("databroadcast" + Guid.NewGuid().ToString("N")[..12]);

    private async Task<T> AskAsync<T>(Func<DataBroadcastWorklist, Task<T>> ask)
    {
        await using CarinaDbContext context = database.Open();

        return await ask(new DataBroadcastWorklist(context, new StoppedClock(Now.AddHours(3))));
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

    private async Task PlanAsync(DateTime starts, Margin before)
    {
        Reservation reservation = ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), startsAt: starts),
            marginBefore: before);

        await using CarinaDbContext context = database.Open();
        context.Add(reservation);
        await context.SaveChangesAsync(Cancel);
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
