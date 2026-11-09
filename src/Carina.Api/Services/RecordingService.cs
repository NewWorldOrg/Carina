using Carina.Api.Common;
using Carina.Domain.Auth;
using Carina.Domain.Base;
using Carina.Domain.Driver;
using Carina.Domain.Encodings;
using Carina.Domain.Events;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Viewing;
using Carina.Infrastructure.Thumbnails;

using Puppis.Contracts;

namespace Carina.Api.Services;

public enum RecordingFailure
{
    NoSuchRecording = 1,

    AlreadyEnded = 2,

    NotBeingWritten = 3,

    StillRecording = 4,

    DriverUnreachable = 5,

    DriverRefused = 6,

    NowhereToPutPictures = 7,

    FileOutOfReach = 8,

    RootOutOfReach = 9,

    FilesLeftBehind = 10,

    OneIsAlreadyBeingDiscarded = 11,

    TookTooLong = 12,

    BeingEncoded = 13,
}

public sealed record ThumbnailRemade(Recording Recording, ThumbnailRemake Remake);

/// <summary>
/// What became of one recording handed in to have its picture drawn again: it is back in the queue the
/// thumbnail pass reads, or it is not, for the reason named.
/// </summary>
public enum ThumbnailRemakeOutcome
{
    Queued = 1,

    NoSuchRecording = 2,

    StillRecording = 3,

    Refused = 4,
}

/// <summary>
/// One row of the answer to queueing pictures to be drawn again: the recording as it was asked, what became
/// of it, and why when it was not queued.
/// </summary>
public sealed record ThumbnailRemakeRow(RecordingId Id, ThumbnailRemakeOutcome Outcome, string Message)
{
    public static ThumbnailRemakeRow Queued(RecordingId id)
        => new(id, ThumbnailRemakeOutcome.Queued, string.Empty);

    public static ThumbnailRemakeRow NotQueued(RecordingId id, string message, RecordingFailure failure)
        => new(id, ThumbnailRemakes.Outcome(failure), ThumbnailRemakes.Said(failure, message));

    public static ThumbnailRemakeRow Threw(RecordingId id)
        => new(id, ThumbnailRemakeOutcome.Refused, ThumbnailRemakes.SaidOfOneThatThrew);
}

/// <summary>
/// The rules of queueing pictures to be drawn again for several recordings: how many are taken in one
/// request, and how the single recording's refusals read as the outcome of a row. They sit beside the
/// service because a file of their own named for thumbnails would count as part of the feature.
/// </summary>
public static class ThumbnailRemakes
{
    public const int MostAtOnce = 100;

    public const string SaidOfOneThatThrew = "Queueing this recording failed on this side; the rest were taken, and the log says what happened.";

    public static readonly string Description = $"between one and {MostAtOnce} recording ids.";

    public static ThumbnailRemakeOutcome Outcome(RecordingFailure failure) => failure switch
    {
        RecordingFailure.NoSuchRecording => ThumbnailRemakeOutcome.NoSuchRecording,
        RecordingFailure.StillRecording => ThumbnailRemakeOutcome.StillRecording,
        _ => ThumbnailRemakeOutcome.Refused,
    };

    internal static string Said(RecordingFailure failure, string message) => failure switch
    {
        RecordingFailure.NoSuchRecording => "The ledger holds no such recording.",
        RecordingFailure.StillRecording => "The recording is still being written, and a picture is taken of a recording that has ended.",
        _ => message,
    };
}

public sealed record RecordingSeen(
    Recording Recording,
    EncodeStanding Encode,
    QualityBands Quality,
    PlaybackPosition? Watched = null);

public sealed record RecordingPage(
    PaginatedList<Recording> Found,
    EncodeStandingBoard Encoding,
    QualityBands Quality,
    RecordingCursor? Next = null,
    IReadOnlyDictionary<RecordingId, PlaybackPosition>? Watched = null);

public sealed record RecordingStopAsked(RecordingSeen Seen, RecordingStopReason Reason, DateTime AskedAt);

public sealed record RecordingDiscarded(RecordingId Id, int FilesRemoved);

public sealed class RecordingService(
    IRecordingDirectory recordings,
    IEncodeStandingReader encoding,
    IQualityThresholdRepository thresholds,
    IDriverClient driver,
    IThumbnailRemaker thumbnails,
    IRecordingFileEraser eraser,
    IRecordingEncodes encodes,
    IPlaybackPositionRepository positions,
    RecordingDeletions deletions,
    IAppEventPublisher events,
    TimeProvider clock,
    ILogger<RecordingService> logger)
{
    public async Task<ServiceResult<RecordingPage>> ListAsync(
        RecordingQuery query,
        Subject? viewer,
        CancellationToken cancellationToken)
    {
        QualityBands bands = await BandsAsync(cancellationToken);
        RecordingListing listed = await recordings.ListAsync(query, bands, cancellationToken);
        IReadOnlyCollection<RecordingId> ids = [.. listed.Found.Items.Select(recording => recording.Id)];
        EncodeStandingBoard standings = await encoding.ReadAsync(ids, cancellationToken);
        IReadOnlyDictionary<RecordingId, PlaybackPosition> watched = viewer is null || ids.Count == 0
            ? new Dictionary<RecordingId, PlaybackPosition>()
            : await positions.FindEachAsync(ids, viewer, cancellationToken);

        return ServiceResult<RecordingPage>.Success(
            new RecordingPage(listed.Found, standings, bands, listed.Next, watched));
    }

    public async Task<ServiceResult<RecordingSeen, RecordingFailure>> DetailAsync(
        RecordingId id,
        Subject? viewer,
        CancellationToken cancellationToken)
        => await recordings.FindAsync(id, cancellationToken) is { } recording
            ? ServiceResult<RecordingSeen, RecordingFailure>.Success(await SeenAsync(recording, viewer, cancellationToken))
            : Missing<RecordingSeen>(id);

    public async Task<ServiceResult<RecordingStopAsked, RecordingFailure>> StopAsync(
        RecordingId id,
        RecordingStopReason reason,
        Subject? viewer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(reason);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return Missing<RecordingStopAsked>(id);
        }

        if (!recording.IsInFlight)
        {
            return ServiceResult<RecordingStopAsked, RecordingFailure>.Failure(
                $"Recording {id.Wire} already ended {recording.Outcome}, so there is nothing left to stop.",
                RecordingFailure.AlreadyEnded);
        }

        DriverCall<IReadOnlyList<SessionSnapshot>> live = await driver.GetActiveSessionsAsync(cancellationToken);

        if (!live.TryGetValue(out IReadOnlyList<SessionSnapshot>? sessions))
        {
            return Unanswered<RecordingStopAsked, IReadOnlyList<SessionSnapshot>>(live);
        }

        SessionSnapshot? writing = sessions.FirstOrDefault(session =>
            string.Equals(session.RecordingId, id.Wire, StringComparison.Ordinal));

        if (writing is null)
        {
            return ServiceResult<RecordingStopAsked, RecordingFailure>.Failure(
                $"The ledger says recording {id.Wire} is still being written and the driver is writing no such "
                + "session, so this is a recording to recover rather than one to stop.",
                RecordingFailure.NotBeingWritten);
        }

        DriverCall<SessionSnapshot> stopped = await driver.StopSessionAsync(
            writing.SessionId,
            reason.Value,
            cancellationToken);

        if (stopped.Outcome is not DriverCallOutcome.Reached)
        {
            return Unanswered<RecordingStopAsked, SessionSnapshot>(stopped);
        }

        DateTime asked = clock.GetUtcNow().UtcDateTime;
        RecordingHalt halt = await recordings.HaltAsync(id, reason, asked, cancellationToken);

        if (halt is RecordingHalt.Written)
        {
            events.Signal(AppEventName.Recordings);
        }

        if (halt is RecordingHalt.AlreadyEnded)
        {
            return ServiceResult<RecordingStopAsked, RecordingFailure>.Failure(
                $"Recording {id.Wire} ended while it was being stopped, so the driver was asked to stop and the "
                + "reason for asking is not on the recording.",
                RecordingFailure.AlreadyEnded);
        }

        return await recordings.FindAsync(id, cancellationToken) is { } asking
            ? ServiceResult<RecordingStopAsked, RecordingFailure>.Success(
                new RecordingStopAsked(await SeenAsync(asking, viewer, cancellationToken), reason, asked))
            : Missing<RecordingStopAsked>(id);
    }

    public async Task<ServiceResult<ThumbnailRemade, RecordingFailure>> RemakeThumbnailAsync(
        RecordingId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return Missing<ThumbnailRemade>(id);
        }

        if (recording.IsInFlight)
        {
            return ServiceResult<ThumbnailRemade, RecordingFailure>.Failure(
                $"Recording {id.Wire} is still being written, and a picture is taken of a recording that has ended.",
                RecordingFailure.StillRecording);
        }

        ThumbnailRemake remake = await thumbnails.RemakeAsync(id, cancellationToken);

        if (remake is ThumbnailRemake.NothingToAskAbout)
        {
            return Missing<ThumbnailRemade>(id);
        }

        if (remake is ThumbnailRemake.NowhereToPutThem)
        {
            return ServiceResult<ThumbnailRemade, RecordingFailure>.Failure(
                "Nothing tells this process where to put thumbnails, so none can be drawn until it is configured.",
                RecordingFailure.NowhereToPutPictures);
        }

        if (remake is ThumbnailRemake.OutOfReach)
        {
            return ServiceResult<ThumbnailRemade, RecordingFailure>.Failure(
                $"The output root recording {id.Wire} was written to is not mounted here, so its file cannot be "
                + "read to draw a picture of it.",
                RecordingFailure.FileOutOfReach);
        }

        return await recordings.FindAsync(id, cancellationToken) is { } drawn
            ? ServiceResult<ThumbnailRemade, RecordingFailure>.Success(new ThumbnailRemade(drawn, remake))
            : Missing<ThumbnailRemade>(id);
    }

    public async Task<ServiceResult<IReadOnlyList<ThumbnailRemakeRow>>> RemakeThumbnailsAsync(
        IReadOnlyList<RecordingId> ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        List<ThumbnailRemakeRow> rows = new(ids.Count);

        foreach (RecordingId id in ids)
        {
            rows.Add(await QueueThumbnailAsync(id, cancellationToken));
        }

        return ServiceResult<IReadOnlyList<ThumbnailRemakeRow>>.Success(rows);
    }

    private async Task<ThumbnailRemakeRow> QueueThumbnailAsync(RecordingId id, CancellationToken cancellationToken)
    {
        try
        {
            if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
            {
                return ThumbnailRemakeRow.NotQueued(id, "There is no such recording.", RecordingFailure.NoSuchRecording);
            }

            if (recording.IsInFlight)
            {
                return ThumbnailRemakeRow.NotQueued(id, "The recording is still being written.", RecordingFailure.StillRecording);
            }

            return await thumbnails.AskAgainAsync(id, cancellationToken) switch
            {
                ThumbnailAskedAgain.Queued => ThumbnailRemakeRow.Queued(id),
                ThumbnailAskedAgain.NothingToAskAbout => ThumbnailRemakeRow.NotQueued(id, "There is no such recording.", RecordingFailure.NoSuchRecording),
                _ => ThumbnailRemakeRow.NotQueued(
                    id,
                    "Nothing tells this process where to put thumbnails, so none can be drawn until it is configured.",
                    RecordingFailure.NowhereToPutPictures),
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "Queueing recording {Recording} for a picture threw; the other rows of the request are unaffected.", id.Wire);

            return ThumbnailRemakeRow.Threw(id);
        }
    }

    public async Task<ServiceResult<RecordingDiscarded, RecordingFailure>> DiscardAsync(
        RecordingId id,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return Missing<RecordingDiscarded>(id);
        }

        if (recording.IsInFlight)
        {
            return ServiceResult<RecordingDiscarded, RecordingFailure>.Failure(
                $"Recording {id.Wire} is still being written, so it is stopped before it is thrown away.",
                RecordingFailure.StillRecording);
        }

        if (await encodes.AnyUnderWayAsync(id, cancellationToken))
        {
            return ServiceResult<RecordingDiscarded, RecordingFailure>.Failure(
                $"Recording {id.Wire} has an encode waiting or running, so that is called off before the recording "
                + "is thrown away.",
                RecordingFailure.BeingEncoded);
        }

        using IDisposable? turn = deletions.Begin(id);

        if (turn is null)
        {
            return ServiceResult<RecordingDiscarded, RecordingFailure>.Failure(
                $"Recording {deletions.Underway?.Wire} is being thrown away; only one is at a time, so this "
                + "one waits rather than running beside it.",
                RecordingFailure.OneIsAlreadyBeingDiscarded);
        }

        using CancellationTokenSource limit = new(deletions.Longest, clock);
        using CancellationTokenSource asking =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);

        RecordingErasure erasure;

        try
        {
            erasure = await eraser.EraseAsync(id, recording.OutputRoot, asking.Token);

            if (erasure.EverythingIsGone)
            {
                if (await encodes.AnyUnderWayAsync(id, asking.Token))
                {
                    return ServiceResult<RecordingDiscarded, RecordingFailure>.Failure(
                        $"Recording {id.Wire} is off the disk, and an encode of it was queued while it was being "
                        + "taken away, so its row and its encodes stay until that encode has ended.",
                        RecordingFailure.BeingEncoded);
                }

                erasure = AndThen(erasure, await encodes.EraseAsync(id, asking.Token), id);
            }
        }
        catch (OperationCanceledException) when (GaveUp(limit, cancellationToken))
        {
            return ServiceResult<RecordingDiscarded, RecordingFailure>.Failure(
                $"Throwing recording {id.Wire} away was still going after {deletions.Longest}, so it was given "
                + $"up on. Recording {id.Wire} is still in the ledger, which is what says the throwing away is "
                + "unfinished, and asking again carries on from here.",
                RecordingFailure.TookTooLong);
        }

        if (erasure.Fault is { } fault)
        {
            if (erasure.LeftFilesBehind
                && await recordings.NoteErasureAsync(id, erasure, clock.GetUtcNow().UtcDateTime, cancellationToken)
                    is RecordingErasureNote.Noted)
            {
                events.Signal(AppEventName.Recordings);
            }

            return ServiceResult<RecordingDiscarded, RecordingFailure>.Failure(
                $"{erasure.Note} {Aftermath(fault, id)}",
                Failed(fault));
        }

        RecordingDiscard discard = await recordings.DiscardAsync(id, cancellationToken);

        if (discard is RecordingDiscard.Discarded)
        {
            await positions.ForgetAsync(id, cancellationToken);
            events.Signal(AppEventName.Recordings);
            events.Signal(AppEventName.Quality);
        }

        return discard switch
        {
            RecordingDiscard.Discarded => ServiceResult<RecordingDiscarded, RecordingFailure>.Success(
                new RecordingDiscarded(id, erasure.FilesRemoved)),
            RecordingDiscard.StillRecording => ServiceResult<RecordingDiscarded, RecordingFailure>.Failure(
                $"Recording {id.Wire} is still being written, so it is stopped before it is thrown away.",
                RecordingFailure.StillRecording),
            _ => Missing<RecordingDiscarded>(id),
        };
    }

    private static RecordingErasure AndThen(RecordingErasure recording, EncodesErased derived, RecordingId id)
        => derived.EverythingIsGone
            ? RecordingErasure.Erased(recording.FilesRemoved + derived.FilesRemoved)
            : RecordingErasure.Refused(
                ErasureFault.FileLeftBehind,
                $"Recording {id.Wire} is off the disk, and what its encodes left could not all be removed: "
                + $"{string.Join(", ", derived.Left.Select(name => name.Value))}.",
                derived.Left.Count);

    private static bool GaveUp(CancellationTokenSource limit, CancellationToken asked)
        => limit.IsCancellationRequested && !asked.IsCancellationRequested;

    private async Task<RecordingSeen> SeenAsync(
        Recording recording,
        Subject? viewer,
        CancellationToken cancellationToken)
    {
        EncodeStandingBoard standings = await encoding.ReadAsync([recording.Id], cancellationToken);
        PlaybackPosition? watched = viewer is null
            ? null
            : await positions.FindAsync(recording.Id, viewer, cancellationToken);

        return new RecordingSeen(
            recording,
            standings.For(recording.Id),
            await BandsAsync(cancellationToken),
            watched);
    }

    private async Task<QualityBands> BandsAsync(CancellationToken cancellationToken)
        => QualityThresholdStanding.Bands(
            QualityThresholdStanding.Over(await thresholds.ListAsync(cancellationToken), clock.GetUtcNow().UtcDateTime));

    private static string Aftermath(ErasureFault fault, RecordingId id) => fault switch
    {
        ErasureFault.FileLeftBehind => $"Recording {id.Wire} is still in the ledger, which is what says the "
            + "throwing away is unfinished, and asking again carries on from here.",
        ErasureFault.RootOutOfReach or ErasureFault.DriverUnreachable or ErasureFault.DriverRefused =>
            $"Recording {id.Wire} is left as it was.",
        _ => throw new ArgumentOutOfRangeException(
            nameof(fault),
            fault,
            "An erasure that failed says which way it failed."),
    };

    private static RecordingFailure Failed(ErasureFault fault) => fault switch
    {
        ErasureFault.RootOutOfReach => RecordingFailure.RootOutOfReach,
        ErasureFault.FileLeftBehind => RecordingFailure.FilesLeftBehind,
        ErasureFault.DriverUnreachable => RecordingFailure.DriverUnreachable,
        ErasureFault.DriverRefused => RecordingFailure.DriverRefused,
        _ => throw new ArgumentOutOfRangeException(
            nameof(fault),
            fault,
            "An erasure that failed says which way it failed."),
    };

    private static ServiceResult<T, RecordingFailure> Missing<T>(RecordingId id)
        => ServiceResult<T, RecordingFailure>.Failure(
            $"There is no recording {id.Wire}.",
            RecordingFailure.NoSuchRecording);

    private static ServiceResult<T, RecordingFailure> Unanswered<T, TCalled>(DriverCall<TCalled> call)
        => ServiceResult<T, RecordingFailure>.Failure(
            Describe(call),
            call.Outcome is DriverCallOutcome.Unreachable
                ? RecordingFailure.DriverUnreachable
                : RecordingFailure.DriverRefused);

    private static string Describe<T>(DriverCall<T> call)
    {
        if (call.Failure is { } failure)
        {
            return failure;
        }

        if (call.Problem is not { } problem)
        {
            return "The driver answered without saying anything.";
        }

        return problem.Problems.Count == 0
            ? problem.Title
            : $"{problem.Title}: {string.Join(" ", problem.Problems)}";
    }
}
