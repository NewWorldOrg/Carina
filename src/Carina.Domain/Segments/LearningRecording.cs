using Carina.Domain.Base;
using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// One recording learned from: its id and when it started, its readings, what is known of it besides, what
/// viewers did with its skip buttons, and the seeks they made in it. What its context heard of the
/// catalog is read afresh from the catalog learning is handed, as the recording would have heard it.
/// </summary>
public sealed record LearningRecording
{
    public LearningRecording(
        RecordingId id,
        DateTime startedAt,
        SegmentEvidence evidence,
        SegmentContext context,
        IReadOnlyList<ViewerAction> actions,
        IReadOnlyList<ViewerSeek>? seeks = null)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);

        if (actions.Any(action => action is null))
        {
            throw new ArgumentException("Every action is handed over.", nameof(actions));
        }

        if (seeks?.Any(seek => seek is null) is true)
        {
            throw new ArgumentException("Every seek is handed over.", nameof(seeks));
        }

        Id = id;
        StartedAt = UtcTimes.Required(startedAt, nameof(startedAt));
        Evidence = evidence;
        Context = context;
        Actions = [.. actions];
        Seeks = [.. seeks ?? []];
    }

    public RecordingId Id { get; }

    public DateTime StartedAt { get; }

    public SegmentEvidence Evidence { get; }

    public SegmentContext Context { get; }

    public IReadOnlyList<ViewerAction> Actions { get; }

    public IReadOnlyList<ViewerSeek> Seeks { get; }
}
