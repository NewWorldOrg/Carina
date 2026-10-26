using Carina.Api.Common;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

public enum SeekEventFailure
{
    NoSuchRecording = 1,

    Unreadable = 2,

    TooMany = 3,
}

/// <summary>
/// One seek as it was sent: whether it went forward or came back and how it was made when each names one the seek
/// takes, and the two moments in seconds of the recording's own time.
/// </summary>
public sealed record SentSeek(SeekAction? Event, SeekMeans? Means, double? FromSec, double? ToSec);

/// <summary>
/// Takes a seek a viewer made on a recording whose row is still there, whether or not the recording is still being
/// written, keeping nothing of who made it.
/// </summary>
public sealed class SeekEventService(
    IRecordingDirectory recordings,
    ISeekEventRepository seeks,
    TimeProvider clock)
{
    public async Task<ServiceResult<SeekEvent, SeekEventFailure>> TakeAsync(RecordingId id, SentSeek sent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(sent);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return Failure($"There is no recording {id.Wire}.", SeekEventFailure.NoSuchRecording);
        }

        if (Read(sent, recording.Written) is not { } read)
        {
            return Failure(Unread(sent, recording.Written), SeekEventFailure.Unreadable);
        }

        SeekEvent seek = SeekEvent.Of(id, new ViewerSeek(clock.GetUtcNow().UtcDateTime, read.Action, read.Means, read.From, read.To));

        return await seeks.TakeAsync(seek, cancellationToken)
            ? ServiceResult<SeekEvent, SeekEventFailure>.Success(seek)
            : Failure($"Recording {id.Wire} takes at most {SeekEvent.MostAnHour} seeks an hour.", SeekEventFailure.TooMany);
    }

    private static ReadSeek? Read(SentSeek sent, TimeSpan written)
        => sent is { Event: { } action, Means: { } means, FromSec: { } from, ToSec: { } to }
           && SkipEvent.Moment(from, written) is { } starts
           && SkipEvent.Moment(to, written) is { } ends
           && GoesItsWay(action, starts, ends)
            ? new ReadSeek(action, means, starts, ends)
            : null;

    private static bool GoesItsWay(SeekAction action, TimeSpan from, TimeSpan to) => action is SeekAction.Forward ? to > from : to < from;

    private static string Unread(SentSeek sent, TimeSpan written)
    {
        if (sent.Event is null)
        {
            return "event: expected forward or returned.";
        }

        if (sent.Means is null)
        {
            return "means: expected bar, step, chapter or key.";
        }

        if (sent.FromSec is not { } from || SkipEvent.Moment(from, written) is null)
        {
            return "fromSec: expected seconds within the recording, from 0 to its length.";
        }

        return sent.ToSec is not { } to || SkipEvent.Moment(to, written) is null
            ? "toSec: expected seconds within the recording, from 0 to its length."
            : "toSec: expected a moment later than fromSec going forward, and earlier coming back.";
    }

    private static ServiceResult<SeekEvent, SeekEventFailure> Failure(string message, SeekEventFailure failure)
        => ServiceResult<SeekEvent, SeekEventFailure>.Failure(message, failure);

    private sealed record ReadSeek(SeekAction Action, SeekMeans Means, TimeSpan From, TimeSpan To);
}
