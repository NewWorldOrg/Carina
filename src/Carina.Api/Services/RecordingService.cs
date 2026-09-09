using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Driver;
using Carina.Domain.Encodings;
using Carina.Domain.Events;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Thumbnails;

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
}

public sealed record ThumbnailRemade(Recording Recording, ThumbnailRemake Remake);

public sealed record RecordingSeen(Recording Recording, EncodeStanding Encode);

public sealed record RecordingPage(PaginatedList<Recording> Found, EncodeStandingBoard Encoding);

public sealed record RecordingStopAsked(RecordingSeen Seen, RecordingStopReason Reason, DateTime AskedAt);

public sealed record RecordingDiscarded(RecordingId Id, int FilesRemoved);

public sealed class RecordingService(
    IRecordingDirectory recordings,
    IEncodeStandingReader encoding,
    IDriverClient driver,
    IThumbnailRemaker thumbnails,
    IRecordingFileEraser eraser,
    RecordingDeletions deletions,
    IAppEventPublisher events,
    TimeProvider clock)
{
    public async Task<ServiceResult<RecordingPage>> ListAsync(
        RecordingQuery query,
        CancellationToken cancellationToken)
    {
        PaginatedList<Recording> found = await recordings.ListAsync(query, cancellationToken);
        EncodeStandingBoard standings = await encoding.ReadAsync(
            [.. found.Items.Select(recording => recording.Id)],
            cancellationToken);

        return ServiceResult<RecordingPage>.Success(new RecordingPage(found, standings));
    }

    public async Task<ServiceResult<RecordingSeen, RecordingFailure>> DetailAsync(
        RecordingId id,
        CancellationToken cancellationToken)
        => await recordings.FindAsync(id, cancellationToken) is { } recording
            ? ServiceResult<RecordingSeen, RecordingFailure>.Success(await SeenAsync(recording, cancellationToken))
            : Missing<RecordingSeen>(id);

    public async Task<ServiceResult<RecordingStopAsked, RecordingFailure>> StopAsync(
        RecordingId id,
        RecordingStopReason reason,
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
                new RecordingStopAsked(await SeenAsync(asking, cancellationToken), reason, asked))
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

        using IDisposable? turn = deletions.Begin(id);

        if (turn is null)
        {
            return ServiceResult<RecordingDiscarded, RecordingFailure>.Failure(
                $"Recording {deletions.Underway?.Wire} is being thrown away; only one is at a time, so this "
                + "one waits rather than running beside it.",
                RecordingFailure.OneIsAlreadyBeingDiscarded);
        }

        using var limit = new CancellationTokenSource(deletions.Longest, clock);
        using CancellationTokenSource asking =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, limit.Token);

        RecordingErasure erasure;

        try
        {
            erasure = await eraser.EraseAsync(id, recording.OutputRoot, asking.Token);
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
            return ServiceResult<RecordingDiscarded, RecordingFailure>.Failure(
                $"{erasure.Note} {Aftermath(fault, id)}",
                Failed(fault));
        }

        RecordingDiscard discard = await recordings.DiscardAsync(id, cancellationToken);

        if (discard is RecordingDiscard.Discarded)
        {
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

    private static bool GaveUp(CancellationTokenSource limit, CancellationToken asked)
        => limit.IsCancellationRequested && !asked.IsCancellationRequested;

    private async Task<RecordingSeen> SeenAsync(Recording recording, CancellationToken cancellationToken)
    {
        EncodeStandingBoard standings = await encoding.ReadAsync([recording.Id], cancellationToken);

        return new RecordingSeen(recording, standings.For(recording.Id));
    }

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
