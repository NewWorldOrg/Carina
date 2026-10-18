using Carina.Api.Common;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

public enum SkipEventFailure
{
    NoSuchRecording = 1,

    Unreadable = 2,

    TooMany = 3,
}

/// <summary>
/// One skip event as it was sent: what the viewer did and the kind of the stretch when each names one the event
/// takes, the two moments in seconds of the recording's own time, and the version whose detection offered the
/// button, if one did.
/// </summary>
public sealed record SentSkip(ViewerActionKind? Event, SegmentKind? Kind, double? FromSec, double? ToSec, int? ModelVersion);

/// <summary>
/// Takes what a viewer did with a skip button on a recording whose row is still there, whether or not learning is
/// on and whether or not the recording is still being written, keeping nothing of who did it.
/// </summary>
public sealed class SkipEventService(
    IRecordingDirectory recordings,
    ISegmentModelVersionRepository versions,
    ISkipEventRepository skips,
    TimeProvider clock)
{
    public async Task<ServiceResult<SkipEvent, SkipEventFailure>> TakeAsync(RecordingId id, SentSkip sent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(sent);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return Failure($"There is no recording {id.Wire}.", SkipEventFailure.NoSuchRecording);
        }

        if (Read(sent, recording.Written) is not { } read)
        {
            return Failure(Unread(sent, recording.Written), SkipEventFailure.Unreadable);
        }

        if (sent.ModelVersion is { } number && await versions.FindAsync(number, cancellationToken) is null)
        {
            return Failure("modelVersion: expected the number of a version of the model, or null for a stretch an answer gave.", SkipEventFailure.Unreadable);
        }

        SkipEvent skip = SkipEvent.Of(id, new ViewerAction(clock.GetUtcNow().UtcDateTime, read.Action, read.Kind, read.From, read.To, sent.ModelVersion));

        return await skips.TakeAsync(skip, cancellationToken)
            ? ServiceResult<SkipEvent, SkipEventFailure>.Success(skip)
            : Failure($"Recording {id.Wire} takes at most {SkipEvent.MostAnHour} skip events an hour.", SkipEventFailure.TooMany);
    }

    private static ReadSkip? Read(SentSkip sent, TimeSpan written)
        => sent is { Event: { } action, Kind: { } kind and not SegmentKind.Programme, FromSec: { } from, ToSec: { } to }
           && SkipEvent.Moment(from, written) is { } starts
           && SkipEvent.Moment(to, written) is { } ends
            ? new ReadSkip(action, kind, starts, ends)
            : null;

    private static string Unread(SentSkip sent, TimeSpan written)
    {
        if (sent.Event is null)
        {
            return "event: expected skipped, dismissed or returned.";
        }

        if (sent.Kind is null or SegmentKind.Programme)
        {
            return "kind: expected break, opening or ending.";
        }

        return sent.FromSec is not { } from || SkipEvent.Moment(from, written) is null
            ? "fromSec: expected seconds within the recording, from 0 to its length."
            : "toSec: expected seconds within the recording, from 0 to its length.";
    }

    private static ServiceResult<SkipEvent, SkipEventFailure> Failure(string message, SkipEventFailure failure)
        => ServiceResult<SkipEvent, SkipEventFailure>.Failure(message, failure);

    private sealed record ReadSkip(ViewerActionKind Action, SegmentKind Kind, TimeSpan From, TimeSpan To);
}
