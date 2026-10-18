using Carina.Api.Common;
using Carina.Domain.Recordings;
using Carina.Domain.Segments;

namespace Carina.Api.Services;

public enum RecordingSegmentFailure
{
    NoSuchRecording = 1,

    Unreadable = 2,

    StillBeingWritten = 3,

    NoAnswerStanding = 4,
}

/// <summary>
/// The stretches of one recording as its detail shows them, in the recording's own time: how long the recording
/// was written for, and how its stretches stand by the answer kept for it and the version of the model in use.
/// </summary>
public sealed record RecordingSegments(TimeSpan Length, SegmentStanding Standing);

/// <summary>
/// Reads where the stretches of a recording stand, keeps the answer a person gives for them and withdraws it.
/// A recording still being written is read as any other, but takes no answer; one whose row is gone is neither
/// read nor answered, whatever is kept of it. A refused answer is named by what is wrong with it alone.
/// </summary>
public sealed class RecordingSegmentService(
    IRecordingDirectory recordings,
    ISegmentModelVersionRepository versions,
    IRecordingDetectionRepository detections,
    ISegmentAnswerRepository answers,
    TimeProvider clock)
{
    public const string ConfirmedUnread = "confirmed: expected true to confirm the stretches or false to leave them unconfirmed.";

    public async Task<ServiceResult<RecordingSegments, RecordingSegmentFailure>> ReadAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        return await recordings.FindAsync(id, cancellationToken) is { } recording
            ? Read(await StandingAsync(recording, cancellationToken))
            : NoSuchRecording(id);
    }

    public async Task<ServiceResult<RecordingSegments, RecordingSegmentFailure>> AnswerAsync(
        RecordingId id,
        IReadOnlyList<SentStretch?>? sent,
        bool? confirmed,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return NoSuchRecording(id);
        }

        if (recording.IsInFlight)
        {
            return Failure($"Recording {id.Wire} is still being written; its stretches are answered once it ends.", RecordingSegmentFailure.StillBeingWritten);
        }

        if (confirmed is not { } confirming)
        {
            return Failure(ConfirmedUnread, RecordingSegmentFailure.Unreadable);
        }

        AnswerReading read = AnsweredStretches.Read(sent ?? [], recording.Written);

        if (read.Stretches is not { } stretches)
        {
            return Failure(Said(read.Fault!.Value), RecordingSegmentFailure.Unreadable);
        }

        await answers.AnswerAsync(RecordingAnswer.Given(id, stretches, confirming, clock.GetUtcNow().UtcDateTime), cancellationToken);

        return Read(await StandingAsync(recording, cancellationToken));
    }

    public async Task<ServiceResult<RecordingSegments, RecordingSegmentFailure>> WithdrawAsync(RecordingId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return NoSuchRecording(id);
        }

        if (!await answers.WithdrawAsync(id, clock.GetUtcNow().UtcDateTime, cancellationToken))
        {
            return Failure($"No answer stands for recording {id.Wire}.", RecordingSegmentFailure.NoAnswerStanding);
        }

        return Read(await StandingAsync(recording, cancellationToken));
    }

    private static string Said(AnswerFault fault) => fault switch
    {
        AnswerFault.Gap => "segments: a gap between segments",
        AnswerFault.Overlap => "segments: segments overlap",
        AnswerFault.OutsideTheRecording => "segments: outside the recording",
        AnswerFault.UnknownKind => "segments: unknown kind",
        AnswerFault.TooMany => "segments: too many segments",
        _ => "segments: a segment ends before it starts",
    };

    private async Task<RecordingSegments> StandingAsync(Recording recording, CancellationToken cancellationToken)
    {
        SegmentModelVersion version = await versions.ActiveAsync(cancellationToken);
        RecordingDetection? detection = await detections.FindAsync(recording.Id, version.Number, cancellationToken);
        RecordingAnswer? answer = await answers.FindAsync(recording.Id, cancellationToken);

        return new RecordingSegments(recording.Written, SegmentStanding.Of(answer, detection));
    }

    private static ServiceResult<RecordingSegments, RecordingSegmentFailure> Read(RecordingSegments segments)
        => ServiceResult<RecordingSegments, RecordingSegmentFailure>.Success(segments);

    private static ServiceResult<RecordingSegments, RecordingSegmentFailure> NoSuchRecording(RecordingId id)
        => Failure($"There is no recording {id.Wire}.", RecordingSegmentFailure.NoSuchRecording);

    private static ServiceResult<RecordingSegments, RecordingSegmentFailure> Failure(string message, RecordingSegmentFailure failure)
        => ServiceResult<RecordingSegments, RecordingSegmentFailure>.Failure(message, failure);
}
