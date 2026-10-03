using Carina.Domain.Base;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Captions;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Tests.Integrity;

namespace Carina.Infrastructure.Tests.Captions;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CaptionWorklistTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 10, 3, 6, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task BrPd016ARecordingStillBeingWrittenIsNotWaitingForCaptionsAndSaysSomethingIsBeingRecorded()
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7201);

        Assert.DoesNotContain(await AwaitingAsync(alone), subject => subject.Id.Equals(recording.Id));
        Assert.True(await AnyBeingRecordedAsync());
    }

    [Theory]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public async Task BrPd016ARecordingThatEndedIsWaitingForCaptionsHoweverItEnded(RecordingOutcome outcome)
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7202, new ServiceId(23610));
        await SettleAsync(recording.Id, outcome, Now.AddHours(1));

        CaptionSubject subject = Assert.Single(await AwaitingAsync(alone));

        Assert.Equal(recording.Id, subject.Id);
        Assert.Equal(recording.FileName.Value, subject.FileName.Value);
        Assert.Equal(23610, subject.Service.Value);
    }

    [Fact]
    public async Task BrPd016TheNewestRecordingComesFirstAndAPassTakesNoMoreThanItAsks()
    {
        OutputRoot alone = Alone();
        Recording earlier = await AddAsync(alone, 7203);
        Recording later = await AddAsync(alone, 7204);
        await SettleAsync(earlier.Id, RecordingOutcome.Complete, Now.AddHours(1));
        await SettleAsync(later.Id, RecordingOutcome.Complete, Now.AddHours(2));

        Assert.Equal([later.Id, earlier.Id], (await AwaitingAsync(alone)).Select(subject => subject.Id).ToArray());
        Assert.Equal(later.Id, Assert.Single(await AwaitingAsync(alone, 1)).Id);
    }

    [Theory]
    [InlineData(CaptionState.Ready, 4)]
    [InlineData(CaptionState.Absent, null)]
    public async Task ARecordingWhoseCaptionsHaveTheirAnswerIsNotAskedAboutAgain(CaptionState state, int? pictures)
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7205);
        await SettleAsync(recording.Id, RecordingOutcome.Complete, Now.AddHours(1));

        await CaptionAsync(recording.Id, state, pictures, Now.AddHours(2));

        Assert.Empty(await AwaitingAsync(alone));
    }

    [Fact]
    public async Task BrPd016AFailureIsTriedAgainUntilItHasFailedThreeTimesInARow()
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7206);
        await SettleAsync(recording.Id, RecordingOutcome.Complete, Now.AddHours(1));

        await CaptionAsync(recording.Id, CaptionState.Failed, null, Now.AddHours(2));
        Assert.Single(await AwaitingAsync(alone));

        await CaptionAsync(recording.Id, CaptionState.Failed, null, Now.AddHours(3));
        Assert.Single(await AwaitingAsync(alone));

        await CaptionAsync(recording.Id, CaptionState.Failed, null, Now.AddHours(4));
        Assert.Empty(await AwaitingAsync(alone));
        Assert.Equal(3, (await ReadAsync(recording.Id)).CaptionAttempts);
    }

    [Theory]
    [InlineData(CaptionState.Ready, 4)]
    [InlineData(CaptionState.Absent, null)]
    [InlineData(CaptionState.Failed, null)]
    public async Task BrPd016ARecordingDescrambledAfterItsCaptionsWereTakenIsWaitingForThemAgain(CaptionState state, int? pictures)
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7207);
        await SettleAsync(recording.Id, RecordingOutcome.Truncated, Now.AddHours(1), scrambled: true);
        for (int failure = 0; failure < (state is CaptionState.Failed ? CaptionSettings.TriesAtMost : 1); failure++)
        {
            await CaptionAsync(recording.Id, state, pictures, Now.AddHours(2));
        }

        Assert.Empty(await AwaitingAsync(alone));

        await DescrambleAsync(recording.Id, Now.AddHours(3));

        Assert.Single(await AwaitingAsync(alone));
    }

    [Fact]
    public async Task ARecordingDescrambledBeforeItsCaptionsWereTakenIsNotTakenAgain()
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7208);
        await SettleAsync(recording.Id, RecordingOutcome.Truncated, Now.AddHours(1), scrambled: true);
        await DescrambleAsync(recording.Id, Now.AddHours(2));

        await CaptionAsync(recording.Id, CaptionState.Ready, 9, Now.AddHours(3));

        Assert.Empty(await AwaitingAsync(alone));
    }

    [Fact]
    public async Task ARootThisProcessCannotReachIsNotReadAtAllAndIsCountedInstead()
    {
        OutputRoot alone = Alone();
        OutputRoot unreached = Alone();
        Recording recording = await AddAsync(unreached, 7209);
        await SettleAsync(recording.Id, RecordingOutcome.Complete, Now.AddHours(1));

        Assert.Empty(await AwaitingAsync(alone));
        Assert.Single(await AwaitingAsync(unreached));

        await using CarinaDbContext context = database.Open();
        int outOfReach = await new CaptionWorklist(context, Clock()).WaitingOutOfReachAsync([alone], Cancel);
        int withinReach = await new CaptionWorklist(context, Clock()).WaitingOutOfReachAsync([alone, unreached], Cancel);

        Assert.Equal(1, outOfReach - withinReach);
    }

    [Fact]
    public async Task KeepingTheCaptionsChangesNothingAboutHowTheRecordingEnded()
    {
        OutputRoot alone = Alone();
        Recording recording = await AddAsync(alone, 7210);
        await SettleAsync(recording.Id, RecordingOutcome.Truncated, Now.AddHours(1));

        await CaptionAsync(recording.Id, CaptionState.Ready, 455, Now.AddHours(2));

        Recording read = await ReadAsync(recording.Id);
        Assert.Equal((CaptionState.Ready, 455, Now.AddHours(2), 0), (read.CaptionState, read.CaptionPictures, read.CaptionsMadeAt, read.CaptionAttempts));
        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Equal([RecordingFault.DiskExhausted], read.OutcomeDetail.Select(detail => detail.Fault));
    }

    [Fact]
    public async Task TheRecordingsWhoseCaptionsAreReadyAreNamedAndNoOthers()
    {
        OutputRoot alone = Alone();
        Recording ready = await AddAsync(alone, 7211);
        Recording absent = await AddAsync(alone, 7212);
        await SettleAsync(ready.Id, RecordingOutcome.Complete, Now.AddHours(1));
        await SettleAsync(absent.Id, RecordingOutcome.Complete, Now.AddHours(1));
        await CaptionAsync(ready.Id, CaptionState.Ready, 2, Now.AddHours(2));
        await CaptionAsync(absent.Id, CaptionState.Absent, null, Now.AddHours(2));

        await using CarinaDbContext context = database.Open();
        IReadOnlyList<RecordingId> named = await new CaptionWorklist(context, Clock()).ReadyAsync(Cancel);

        Assert.Contains(ready.Id, named);
        Assert.DoesNotContain(absent.Id, named);
    }

    [Fact]
    public async Task KeepingCaptionsForARecordingNobodyHasHeardOfIsRefused()
    {
        await using CarinaDbContext context = database.Open();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new CaptionWorklist(context, Clock()).CaptionAsync(RecordingId.New(), CaptionState.Absent, null, Cancel));
    }

    [Fact]
    public async Task AskingForNoneAtAllIsRefused()
        => Assert.Equal(
            "atMost",
            (await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AwaitingAsync(Alone(), 0))).ParamName);

    private static OutputRoot Alone() => new("captions" + Guid.NewGuid().ToString("N")[..12]);

    private static StoppedClock Clock(DateTime? at = null) => new(at ?? Now.AddHours(2));

    private async Task<IReadOnlyList<CaptionSubject>> AwaitingAsync(OutputRoot within, int atMost = 64)
    {
        await using CarinaDbContext context = database.Open();

        return await new CaptionWorklist(context, Clock()).AwaitingAsync([within], atMost, Cancel);
    }

    private async Task<bool> AnyBeingRecordedAsync()
    {
        await using CarinaDbContext context = database.Open();

        return await new CaptionWorklist(context, Clock()).AnyBeingRecordedAsync(Cancel);
    }

    private async Task CaptionAsync(RecordingId id, CaptionState state, int? pictures, DateTime at)
    {
        await using CarinaDbContext context = database.Open();

        await new CaptionWorklist(context, Clock(at)).CaptionAsync(id, state, pictures, Cancel);
    }

    private async Task<Recording> ReadAsync(RecordingId id)
    {
        await using CarinaDbContext context = database.Open();

        return await context.FindAsync<Recording>([id], Cancel)
            ?? throw new InvalidOperationException("The recording that was just written is not there.");
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
