using Carina.Domain.Captions;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Captions;

public sealed class CaptionRequeueDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly ReadyCaptions worklist = new();

    private readonly KeptRecords records = new();

    private CaptionRequeueDomainService Requeue => new(worklist, records);

    [Fact(DisplayName = "BR-PD-016: captions the row says are ready with no record kept are put back to be taken again")]
    public async Task CaptionsReadyWithNoRecordKeptArePutBack()
    {
        RecordingId lost = RecordingId.New();
        RecordingId kept = RecordingId.New();
        worklist.Ready.AddRange([lost, kept]);
        records.Kept.Add(kept);

        CaptionRequeue requeue = await Requeue.RequeueLostAsync(Cancel);

        Assert.Equal(1, requeue.Requeued);
        Assert.Equal([kept], requeue.StillReady);
        Assert.Equal([(lost, CaptionState.Pending)], worklist.Kept);
    }

    [Fact(DisplayName = "BR-PD-016: a recording that went before it was put back is not counted")]
    public async Task ARecordingThatWentIsNotCounted()
    {
        RecordingId gone = RecordingId.New();
        worklist.Ready.Add(gone);
        worklist.Gone.Add(gone);

        CaptionRequeue requeue = await Requeue.RequeueLostAsync(Cancel);

        Assert.Equal(0, requeue.Requeued);
        Assert.Empty(requeue.StillReady);
    }

    [Fact(DisplayName = "BR-PD-016: captions ready with their record kept stay as they are")]
    public async Task CaptionsReadyAndKeptStay()
    {
        RecordingId kept = RecordingId.New();
        worklist.Ready.Add(kept);
        records.Kept.Add(kept);

        CaptionRequeue requeue = await Requeue.RequeueLostAsync(Cancel);

        Assert.Equal(0, requeue.Requeued);
        Assert.Empty(worklist.Kept);
    }

    private sealed class ReadyCaptions : ICaptionWorklist
    {
        public List<RecordingId> Ready { get; } = [];

        public HashSet<RecordingId> Gone { get; } = [];

        public List<(RecordingId Id, CaptionState State)> Kept { get; } = [];

        public Task<IReadOnlyList<CaptionSubject>> AwaitingAsync(
            IReadOnlyList<OutputRoot> withinReach,
            int atMost,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<int> WaitingOutOfReachAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<RecordingId>> ReadyAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RecordingId>>([.. Ready]);

        public Task<IReadOnlyList<CaptionSubject>> ReadyAmongAsync(
            IReadOnlyCollection<RecordingId> among,
            IReadOnlyList<OutputRoot> withinReach,
            int atMost,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> CaptionAsync(RecordingId id, CaptionState state, int? pictures, CancellationToken cancellationToken)
        {
            if (Gone.Contains(id))
            {
                return Task.FromResult(false);
            }

            Kept.Add((id, state));

            return Task.FromResult(true);
        }
    }

    private sealed class KeptRecords : ICaptionRecords
    {
        public HashSet<RecordingId> Kept { get; } = [];

        public Task<CaptionRecord?> ReadAsync(RecordingId id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TimeSpan?> StartsAtAsync(RecordingId id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public bool Holds(RecordingId id) => Kept.Contains(id);
    }
}
