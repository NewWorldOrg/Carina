using Carina.Api.Common;
using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Driver;
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
}

public sealed record ThumbnailRemade(Recording Recording, ThumbnailRemake Remake);

public sealed class RecordingService(
    IRecordingDirectory recordings,
    IDriverClient driver,
    IThumbnailRemaker thumbnails,
    TimeProvider clock)
{
    public async Task<ServiceResult<PaginatedList<Recording>>> ListAsync(
        RecordingQuery query,
        CancellationToken cancellationToken)
        => ServiceResult<PaginatedList<Recording>>.Success(await recordings.ListAsync(query, cancellationToken));

    public async Task<ServiceResult<Recording, RecordingFailure>> FindAsync(
        RecordingId id,
        CancellationToken cancellationToken)
        => await recordings.FindAsync(id, cancellationToken) is { } recording
            ? ServiceResult<Recording, RecordingFailure>.Success(recording)
            : Missing<Recording>(id);

    public async Task<ServiceResult<Recording, RecordingFailure>> StopAsync(
        RecordingId id,
        RecordingStopReason reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(reason);

        if (await recordings.FindAsync(id, cancellationToken) is not { } recording)
        {
            return Missing<Recording>(id);
        }

        if (!recording.IsInFlight)
        {
            return ServiceResult<Recording, RecordingFailure>.Failure(
                $"Recording {id.Wire} already ended {recording.Outcome}, so there is nothing left to stop.",
                RecordingFailure.AlreadyEnded);
        }

        DriverCall<IReadOnlyList<SessionSnapshot>> live = await driver.GetActiveSessionsAsync(cancellationToken);

        if (!live.TryGetValue(out IReadOnlyList<SessionSnapshot>? sessions))
        {
            return Unanswered<Recording, IReadOnlyList<SessionSnapshot>>(live);
        }

        SessionSnapshot? writing = sessions.FirstOrDefault(session =>
            string.Equals(session.RecordingId, id.Wire, StringComparison.Ordinal));

        if (writing is null)
        {
            return ServiceResult<Recording, RecordingFailure>.Failure(
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
            return Unanswered<Recording, SessionSnapshot>(stopped);
        }

        RecordingHalt halt = await recordings.HaltAsync(
            id,
            reason,
            clock.GetUtcNow().UtcDateTime,
            cancellationToken);

        return halt switch
        {
            RecordingHalt.Written => await FindAsync(id, cancellationToken),
            RecordingHalt.AlreadyEnded => ServiceResult<Recording, RecordingFailure>.Failure(
                $"Recording {id.Wire} ended while it was being stopped.",
                RecordingFailure.AlreadyEnded),
            _ => Missing<Recording>(id),
        };
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
