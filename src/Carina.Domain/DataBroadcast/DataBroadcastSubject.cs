using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.DataBroadcast;

/// <summary>
/// A recording whose data broadcast is to be taken: where its file is and the service it carries.
/// </summary>
public sealed record DataBroadcastSubject
{
    public DataBroadcastSubject(RecordingId id, OutputRoot root, RecordingFileName fileName, ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(service);

        Id = id;
        Root = root;
        FileName = fileName;
        Service = service;
    }

    public RecordingId Id { get; }

    public OutputRoot Root { get; }

    public RecordingFileName FileName { get; }

    public ServiceId Service { get; }
}
