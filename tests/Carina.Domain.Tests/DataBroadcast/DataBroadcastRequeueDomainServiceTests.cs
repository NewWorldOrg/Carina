using Carina.Domain.DataBroadcast;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.DataBroadcast;

public sealed class DataBroadcastRequeueDomainServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly MadeRecords worklist = new();

    private readonly KeptRecords records = new();

    private DataBroadcastRequeueDomainService Requeue => new(worklist, records);

    [Fact(DisplayName = "BR-BS-001: a record the row says is made with none kept is put back to be taken again")]
    public async Task ARecordMadeWithNoneKeptIsPutBack()
    {
        RecordingId lost = RecordingId.New();
        RecordingId kept = RecordingId.New();
        worklist.Made.AddRange([lost, kept]);
        records.Kept.Add(kept);

        Assert.Equal(1, await Requeue.RequeueLostAsync(Cancel));
        Assert.Equal([lost], worklist.Lost);
    }

    [Fact(DisplayName = "BR-BS-001: a recording that went before its record was put back is not counted")]
    public async Task ARecordingThatWentIsNotCounted()
    {
        RecordingId gone = RecordingId.New();
        worklist.Made.Add(gone);
        worklist.Gone.Add(gone);

        Assert.Equal(0, await Requeue.RequeueLostAsync(Cancel));
    }

    [Fact(DisplayName = "BR-BS-001: a record made and kept stays as it is")]
    public async Task ARecordMadeAndKeptStays()
    {
        RecordingId kept = RecordingId.New();
        worklist.Made.Add(kept);
        records.Kept.Add(kept);

        Assert.Equal(0, await Requeue.RequeueLostAsync(Cancel));
        Assert.Empty(worklist.Lost);
    }

    private sealed class MadeRecords : IDataBroadcastWorklist
    {
        public List<RecordingId> Made { get; } = [];

        public HashSet<RecordingId> Gone { get; } = [];

        public List<RecordingId> Lost { get; } = [];

        public Task<IReadOnlyList<DataBroadcastSubject>> AwaitingAsync(
            IReadOnlyList<OutputRoot> withinReach,
            int atMost,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<int> WaitingOutOfReachAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<int> CatchUpEndedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<RecordingId>> MadeAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<RecordingId>>([.. Made]);

        public Task<int> RetryFailedAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> LostAsync(RecordingId id, CancellationToken cancellationToken)
        {
            if (Gone.Contains(id))
            {
                return Task.FromResult(false);
            }

            Lost.Add(id);

            return Task.FromResult(true);
        }

        public Task<bool> TakenAsync(RecordingId id, int modules, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<bool> FailedAsync(RecordingId id, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class KeptRecords : IDataBroadcastRecords
    {
        public HashSet<RecordingId> Kept { get; } = [];

        public bool KeepsAnything => true;

        public bool Holds(RecordingId id) => Kept.Contains(id);

        public long? BytesOf(RecordingId id) => throw new NotSupportedException();

        public Task<DataBroadcastOutline?> OutlineAsync(RecordingId id, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<ModuleVersion?> ModuleAsync(RecordingId id, ModuleVersionKey key, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
