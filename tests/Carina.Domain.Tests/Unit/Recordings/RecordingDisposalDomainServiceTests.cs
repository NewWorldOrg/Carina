using Carina.Domain.Encodings;
using Carina.Domain.Recordings;
using Carina.TestSupport;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingDisposalDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly HeldRecordings recordings = new();

    private readonly EncodesUnderWay encodes = new();

    private RecordingDisposalDomainService Disposal => new(recordings, encodes);

    [Fact(DisplayName = "BR-LS-001: a recording that has ended and has no encode under way is thrown away")]
    public async Task ARecordingThatHasEndedIsThrownAway()
    {
        Recording ended = Ended();
        recordings.Recordings.Add(ended);

        RecordingDisposal disposal = await Disposal.WeighAsync(ended.Id, Cancel);

        Assert.Null(disposal.Refusal);
        Assert.Equal(ended.Id, disposal.Recording?.Id);
    }

    [Fact(DisplayName = "BR-LS-001: a recording the ledger does not hold is not thrown away")]
    public async Task ARecordingTheLedgerDoesNotHoldIsNotThrownAway()
        => Assert.Equal(
            RecordingDisposalRefusal.NoSuchRecording,
            (await Disposal.WeighAsync(RecordingId.New(), Cancel)).Refusal);

    [Fact(DisplayName = "BR-LS-001: a recording still being written is stopped before it is thrown away, whatever its encodes")]
    public async Task ARecordingStillBeingWrittenIsNotThrownAway()
    {
        Recording running = RecordingFactory.Started();
        recordings.Recordings.Add(running);
        encodes.UnderWay.Add(running.Id);

        RecordingDisposal disposal = await Disposal.WeighAsync(running.Id, Cancel);

        Assert.Equal(RecordingDisposalRefusal.StillBeingWritten, disposal.Refusal);
        Assert.Null(disposal.Recording);
    }

    [Fact(DisplayName = "BR-LS-001: a recording with an encode waiting or running keeps its files until that encode is called off")]
    public async Task ARecordingWithAnEncodeUnderWayIsNotThrownAway()
    {
        Recording ended = Ended();
        recordings.Recordings.Add(ended);
        encodes.UnderWay.Add(ended.Id);

        Assert.Equal(RecordingDisposalRefusal.BeingEncoded, (await Disposal.WeighAsync(ended.Id, Cancel)).Refusal);
    }

    [Fact(DisplayName = "BR-LS-001: the row of a recording whose files are gone stays while an encode queued meanwhile is under way")]
    public async Task TheRowStaysWhileAnEncodeQueuedMeanwhileIsUnderWay()
    {
        Recording ended = Ended();

        Assert.True(await Disposal.RowMayGoAsync(ended.Id, Cancel));

        encodes.UnderWay.Add(ended.Id);

        Assert.False(await Disposal.RowMayGoAsync(ended.Id, Cancel));
    }

    private static Recording Ended()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, RecordingFactory.Now.AddMinutes(30));

        return recording;
    }

    private sealed class EncodesUnderWay : IRecordingEncodes
    {
        public HashSet<RecordingId> UnderWay { get; } = [];

        public Task<bool> AnyUnderWayAsync(RecordingId recordingId, CancellationToken cancellationToken)
            => Task.FromResult(UnderWay.Contains(recordingId));

        public Task<EncodesErased> EraseAsync(RecordingId recordingId, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
