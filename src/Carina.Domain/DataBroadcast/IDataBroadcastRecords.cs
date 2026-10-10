using Carina.Domain.Recordings;

namespace Carina.Domain.DataBroadcast;

public interface IDataBroadcastRecords
{
    bool KeepsAnything { get; }

    /// <summary>
    /// Whether a record is kept for the recording, judged by the head of what is kept alone.
    /// </summary>
    bool Holds(RecordingId id);

    Task<DataBroadcastRecord?> ReadAsync(RecordingId id, CancellationToken cancellationToken);

    /// <summary>
    /// One version of the record kept for the recording, read without the rest of the record, or null when no record
    /// is kept or it does not hold that version.
    /// </summary>
    Task<ModuleVersion?> ModuleAsync(RecordingId id, ModuleVersionKey key, CancellationToken cancellationToken);
}
