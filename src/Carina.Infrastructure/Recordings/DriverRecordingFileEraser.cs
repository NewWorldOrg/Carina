using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Thumbnails;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed class DriverRecordingFileEraser(
    IDriverClient driver,
    ThumbnailSettings pictures,
    ILogger<DriverRecordingFileEraser> logger) : IRecordingFileEraser
{
    public async Task<RecordingErasure> EraseAsync(
        RecordingId id,
        OutputRoot root,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(root);
        cancellationToken.ThrowIfCancellationRequested();

        DriverCall<RecordingErasedDto> call =
            await driver.EraseRecordingAsync(id.Wire, root.Value, cancellationToken);

        if (!call.TryGetValue(out RecordingErasedDto? erased))
        {
            return RecordingErasure.Refused(FaultIn(call), Describe(call));
        }

        int removed = erased.FileRemoved ? 1 : 0;

        if (pictures.WrittenTo is { } gallery)
        {
            string drawn = Path.Combine(gallery, id.Wire + ThumbnailJob.Extension);
            bool drawnWasThere = File.Exists(drawn);

            if (Unlink(drawn) is { } left)
            {
                return left;
            }

            removed += drawnWasThere ? 1 : 0;
        }

        logger.LogInformation(
            "Recording {Recording} was asked for by hand and the process that owns output root {Root} took "
            + "its file off the disk.",
            id.Wire,
            root.Value);

        return RecordingErasure.Erased(removed);
    }

    private static ErasureFault FaultIn<T>(DriverCall<T> call)
    {
        if (call.Outcome is DriverCallOutcome.Unreachable)
        {
            return ErasureFault.DriverUnreachable;
        }

        return call.Problem?.Title switch
        {
            SessionRefusalTitles.OutputUnavailable => ErasureFault.RootOutOfReach,
            SessionRefusalTitles.FileLeftBehind => ErasureFault.FileLeftBehind,
            _ => ErasureFault.DriverRefused,
        };
    }

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

    private RecordingErasure? Unlink(string path)
    {
        try
        {
            File.Delete(path);

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(failure, "The picture drawn of this recording could not be removed.");

            return RecordingErasure.Refused(
                ErasureFault.FileLeftBehind,
                $"The picture drawn of this recording could not be removed: {failure.Message}");
        }
    }
}
