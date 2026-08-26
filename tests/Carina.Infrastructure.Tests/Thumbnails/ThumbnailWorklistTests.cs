using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Thumbnails;

namespace Carina.Infrastructure.Tests.Thumbnails;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ThumbnailWorklistTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = new(2026, 8, 26, 6, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ARecordingStillBeingWrittenIsNotWaitingForAPicture()
    {
        Recording recording = await AddAsync(7101);

        Assert.DoesNotContain(await AwaitingAsync(), subject => subject.Id.Equals(recording.Id));
    }

    [Theory]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public async Task ARecordingThatEndedIsWaitingForOneHoweverItEnded(RecordingOutcome outcome)
    {
        Recording recording = await AddAsync(7102);
        await SettleAsync(recording.Id, outcome, outcome is RecordingOutcome.Failed ? 0 : 1_200_000);

        ThumbnailSubject subject = Assert.Single(
            await AwaitingAsync(),
            waiting => waiting.Id.Equals(recording.Id));

        Assert.Equal(outcome, subject.Outcome);
        Assert.Equal(recording.FileName.Value, subject.FileName.Value);
        Assert.Equal("bulk", subject.Root.Value);
        Assert.Equal(TimeSpan.FromMinutes(30), subject.Written);
    }

    [Fact]
    public async Task ARecordingThatAlreadyHasItsAnswerIsNotAskedAboutAgain()
    {
        Recording recording = await AddAsync(7103);
        await SettleAsync(recording.Id, RecordingOutcome.Truncated, 1_200_000);
        await IllustrateAsync(recording.Id, ThumbnailState.Ready, null);

        Assert.DoesNotContain(await AwaitingAsync(), subject => subject.Id.Equals(recording.Id));
    }

    [Fact]
    public async Task APassTakesNoMoreThanItAsksFor()
    {
        Recording first = await AddAsync(7104);
        Recording second = await AddAsync(7105);
        await SettleAsync(first.Id, RecordingOutcome.Complete, 1_200_000);
        await SettleAsync(second.Id, RecordingOutcome.Complete, 1_200_000);

        Assert.Single(await AwaitingAsync(1));
    }

    [Fact]
    public async Task AskingForNoneAtAllIsRefused()
        => Assert.Equal(
            "atMost",
            (await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => AwaitingAsync(0))).ParamName);

    [Fact]
    public async Task WritingThePictureStateChangesNothingAboutHowTheRecordingEnded()
    {
        Recording recording = await AddAsync(7106);
        await SettleAsync(recording.Id, RecordingOutcome.Truncated, 1_200_000);

        await IllustrateAsync(recording.Id, ThumbnailState.Ready, null);

        Recording read = await ReadAsync(recording.Id);
        Assert.Equal(ThumbnailState.Ready, read.ThumbnailState);
        Assert.Equal(RecordingOutcome.Truncated, read.Outcome);
        Assert.Equal(1_200_000, read.FileSizeObserved);
        Assert.Equal([RecordingFault.DiskExhausted], read.OutcomeDetail.Select(detail => detail.Fault));
        Assert.True(read.ThumbnailShowsAnUnfinishedRecording);
    }

    [Fact]
    public async Task FailingToDrawOneLeavesTheClassItFailedWithOnTheRow()
    {
        Recording recording = await AddAsync(7107);
        await SettleAsync(recording.Id, RecordingOutcome.Complete, 1_200_000);

        await IllustrateAsync(recording.Id, ThumbnailState.Failed, ThumbnailFault.ProgrammeMissing);

        Recording read = await ReadAsync(recording.Id);
        Assert.Equal(ThumbnailState.Failed, read.ThumbnailState);
        Assert.Equal(ThumbnailFault.ProgrammeMissing, read.ThumbnailFault);
        Assert.Equal(RecordingOutcome.Complete, read.Outcome);
        Assert.Empty(read.OutcomeDetail);
    }

    [Fact]
    public async Task AskingAgainPutsTheRecordingBackInTheQueueAndForgetsTheOldFault()
    {
        Recording recording = await AddAsync(7108);
        await SettleAsync(recording.Id, RecordingOutcome.Complete, 1_200_000);
        await IllustrateAsync(recording.Id, ThumbnailState.Failed, ThumbnailFault.TimedOut);

        ThumbnailSubject? asked = await AskAgainAsync(recording.Id);

        Assert.NotNull(asked);
        Assert.Equal(RecordingOutcome.Complete, asked!.Outcome);

        Recording read = await ReadAsync(recording.Id);
        Assert.Equal(ThumbnailState.Pending, read.ThumbnailState);
        Assert.Null(read.ThumbnailFault);
        Assert.Contains(await AwaitingAsync(), subject => subject.Id.Equals(recording.Id));
    }

    [Fact]
    public async Task AskingAgainForARecordingStillBeingWrittenAnswersNothing()
    {
        Recording recording = await AddAsync(7109);

        Assert.Null(await AskAgainAsync(recording.Id));
    }

    [Fact]
    public async Task AskingAgainForARecordingNobodyHasHeardOfAnswersNothing()
        => Assert.Null(await AskAgainAsync(RecordingId.New()));

    [Fact]
    public async Task IllustratingARecordingNobodyHasHeardOfIsRefused()
        => await Assert.ThrowsAsync<InvalidOperationException>(
            () => IllustrateAsync(RecordingId.New(), ThumbnailState.Ready, null));

    private async Task<IReadOnlyList<ThumbnailSubject>> AwaitingAsync(int atMost = 64)
    {
        await using CarinaDbContext context = database.Open();

        return await new ThumbnailWorklist(context).AwaitingAsync(atMost, Cancel);
    }

    private async Task IllustrateAsync(RecordingId id, ThumbnailState state, ThumbnailFault? fault)
    {
        await using CarinaDbContext context = database.Open();

        await new ThumbnailWorklist(context).IllustrateAsync(id, state, fault, Cancel);
    }

    private async Task<ThumbnailSubject?> AskAgainAsync(RecordingId id)
    {
        await using CarinaDbContext context = database.Open();

        return await new ThumbnailWorklist(context).AskAgainAsync(id, Cancel);
    }

    private async Task<Recording> ReadAsync(RecordingId id)
    {
        await using CarinaDbContext context = database.Open();

        return await context.FindAsync<Recording>([id], Cancel)
            ?? throw new InvalidOperationException("The recording that was just written is not there.");
    }

    private async Task<Recording> AddAsync(int eventId)
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(eventId), Now),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Now,
            Now.AddHours(1),
            new ProgrammeSnapshot("A programme", "What it is about", string.Empty, [], Now),
            null,
            BroadcastGroupRole.Standalone,
            Now);

        recording.Wrote(TimeSpan.FromMinutes(30));

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
