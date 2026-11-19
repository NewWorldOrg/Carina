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

public sealed record SentSkip(ViewerActionKind? Event, SegmentKind? Kind, double? FromSec, double? ToSec, int? ModelVersion);

public sealed class SkipEventService(
    IRecordingDirectory recordings,
    ViewerActionIntakeDomainService intake,
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

        if (!await intake.IsOfferedByAVersionKeptAsync(sent.ModelVersion, cancellationToken))
        {
            return Failure("modelVersion: expected the number of a version of the model, or null for a chapter an encode marked.", SkipEventFailure.Unreadable);
        }

        SkipEvent skip = SkipEvent.Of(id, new ViewerAction(clock.GetUtcNow().UtcDateTime, read.Action, read.Kind, read.From, read.To, sent.ModelVersion));

        return await intake.TakeAsync(skip, cancellationToken)
            ? ServiceResult<SkipEvent, SkipEventFailure>.Success(skip)
            : Failure($"Recording {id.Wire} takes at most {SkipEvent.MostAnHour} skip events an hour.", SkipEventFailure.TooMany);
    }

    private static ReadSkip? Read(SentSkip sent, TimeSpan written)
        => sent is { Event: { } action, Kind: { } kind, FromSec: { } from, ToSec: { } to }
           && SkipEvent.KindsOffered.Contains(kind)
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

        if (sent.Kind is not { } kind || !SkipEvent.KindsOffered.Contains(kind))
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
