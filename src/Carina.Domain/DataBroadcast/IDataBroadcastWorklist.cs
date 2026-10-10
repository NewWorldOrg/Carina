using Carina.Domain.Recordings;

namespace Carina.Domain.DataBroadcast;

public interface IDataBroadcastWorklist
{
    /// <summary>
    /// The ended recordings under a root within reach whose record of the data broadcast is coming, newest first.
    /// </summary>
    Task<IReadOnlyList<DataBroadcastSubject>> AwaitingAsync(
        IReadOnlyList<OutputRoot> withinReach,
        int atMost,
        CancellationToken cancellationToken);

    Task<int> WaitingOutOfReachAsync(IReadOnlyList<OutputRoot> withinReach, CancellationToken cancellationToken);

    /// <summary>
    /// Puts every recording that has ended with its record not yet due — one that ended while a process that did
    /// not know the record was running — to coming, and answers how many there were.
    /// </summary>
    Task<int> CatchUpEndedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// The recordings whose row says their record is made.
    /// </summary>
    Task<IReadOnlyList<RecordingId>> MadeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Puts every record that failed with tries left back to coming, and answers how many there were.
    /// </summary>
    Task<int> RetryFailedAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Puts the record of a recording that is made and no longer kept back to coming, and answers false when the
    /// recording is no longer in the ledger.
    /// </summary>
    Task<bool> LostAsync(RecordingId id, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps that the record of a recording was taken with <paramref name="modules"/> modules, made or missing,
    /// and answers false when the recording is no longer in the ledger.
    /// </summary>
    Task<bool> TakenAsync(RecordingId id, int modules, CancellationToken cancellationToken);

    /// <summary>
    /// Keeps that taking the record of a recording failed once more, and answers false when the recording is no
    /// longer in the ledger.
    /// </summary>
    Task<bool> FailedAsync(RecordingId id, CancellationToken cancellationToken);
}
