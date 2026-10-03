using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Captions;

public sealed record CaptionSubject
{
    public CaptionSubject(RecordingId id, OutputRoot root, RecordingFileName fileName, ServiceId service)
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
