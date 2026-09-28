using Carina.Domain.Channels;
using Carina.Domain.Recordings;

namespace Carina.Domain.Thumbnails;

public sealed record ThumbnailSubject
{
    public ThumbnailSubject(
        RecordingId id,
        OutputRoot root,
        RecordingFileName fileName,
        ServiceId service,
        RecordingOutcome outcome,
        TimeSpan written,
        IReadOnlyList<RecordingSeam>? seams = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(service);

        if (!Enum.IsDefined(outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(outcome),
                outcome,
                "A recording ends in one of three ways.");
        }

        if (written < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(written),
                written,
                "A recording is not shorter than nothing.");
        }

        Id = id;
        Root = root;
        FileName = fileName;
        Service = service;
        Outcome = outcome;
        Written = written;
        Seams = seams ?? [];
    }

    public RecordingId Id { get; }

    public OutputRoot Root { get; }

    public RecordingFileName FileName { get; }

    public ServiceId Service { get; }

    public RecordingOutcome Outcome { get; }

    public TimeSpan Written { get; }

    /// <summary>
    /// Where gaps sit in the recording's file, which a picture is not taken from.
    /// </summary>
    public IReadOnlyList<RecordingSeam> Seams { get; }
}
