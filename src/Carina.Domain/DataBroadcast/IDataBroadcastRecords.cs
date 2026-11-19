using Carina.Domain.Recordings;

namespace Carina.Domain.DataBroadcast;

public interface IDataBroadcastRecords
{
    bool KeepsAnything { get; }

    bool Holds(RecordingId id);

    long? BytesOf(RecordingId id);

    Task<DataBroadcastOutline?> OutlineAsync(RecordingId id, CancellationToken cancellationToken);

    Task<ModuleVersion?> ModuleAsync(RecordingId id, ModuleVersionKey key, CancellationToken cancellationToken);
}
