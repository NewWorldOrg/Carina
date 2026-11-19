using Carina.Domain.Recordings;

namespace Carina.Domain.DataBroadcast;

public interface IDataBroadcastWorklist
{
    Task<IReadOnlyList<DataBroadcastSubject>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken);

    Task<int> WaitingOutOfReachAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken);

    Task<int> CatchUpEndedAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<RecordingId>> MadeAsync(CancellationToken cancellationToken);

    Task<int> RetryFailedAsync(CancellationToken cancellationToken);

    Task<bool> LostAsync(RecordingId id, CancellationToken cancellationToken);

    Task<bool> TakenAsync(RecordingId id, int modules, CancellationToken cancellationToken);

    Task<bool> FailedAsync(RecordingId id, CancellationToken cancellationToken);
}
