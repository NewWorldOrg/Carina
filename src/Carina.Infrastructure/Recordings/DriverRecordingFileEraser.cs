using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Thumbnails;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed class DriverRecordingFileEraser(
    IDriverClient driver,
    IRecordingFileSurvey survey,
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

        if (await AbsentAsync(id, root, cancellationToken) is { } absent)
        {
            return absent;
        }

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

            if (Unlink(gallery, drawn) is { } left)
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

    private static RecordingErasure OutOfReach(OutputRoot root, RootAbsence absence) => absence switch
    {
        RootAbsence.Undeclared => RecordingErasure.Refused(
            ErasureFault.RootOutOfReach,
            $"The process that owns the disk declares no output root called '{root.Value}', so a file reported "
            + "missing under it says nothing about whether it was ever there."),
        RootAbsence.OutOfReach => RecordingErasure.Refused(
            ErasureFault.RootOutOfReach,
            $"Output root '{root.Value}' could not be read here, so a file reported missing under it says "
            + "nothing about whether it was ever there."),
        RootAbsence.HoldsNothing => RecordingErasure.Refused(
            ErasureFault.RootOutOfReach,
            $"Output root '{root.Value}' holds nothing at all, which is what it looks like when its mount has "
            + "gone, so nothing under it is removed."),
        _ => throw new ArgumentOutOfRangeException(
            nameof(absence),
            absence,
            "A root that is not there is missing in one of the ways this type holds."),
    };

    private async Task<RecordingErasure?> AbsentAsync(
        RecordingId id,
        OutputRoot root,
        CancellationToken cancellationToken)
    {
        DriverCall<IReadOnlyList<StorageRootDto>> call = await driver.GetStorageAsync(cancellationToken);

        if (!call.TryGetValue(out IReadOnlyList<StorageRootDto>? declared))
        {
            return RecordingErasure.Refused(FaultIn(call), Describe(call));
        }

        RootListing listing = await survey.ListAsync(root, cancellationToken);

        if (OutputRootPresence.Missing(
                declared,
                root,
                listing.Reachable,
                [.. listing.Files.Select(file => file.Path)]) is not { } absence)
        {
            return null;
        }

        logger.LogWarning(
            "Recording {Recording} was asked for by hand and output root {Root} is {Absence}, so nothing "
            + "under it is removed and the row stays in the ledger.",
            id.Wire,
            root.Value,
            absence);

        return OutOfReach(root, absence);
    }

    private RecordingErasure? Unlink(string gallery, string path)
    {
        if (!RecordingFilePlace.LiesDirectlyUnder(gallery, path))
        {
            logger.LogWarning(
                "The picture drawn of this recording resolves outside the directory pictures are kept in, "
                + "so nothing is removed.");

            return RecordingErasure.Refused(
                ErasureFault.FileLeftBehind,
                "The picture drawn of this recording does not resolve to a file in the directory pictures are "
                + "kept in, so it is left where it is.");
        }

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
                "The picture drawn of this recording could not be removed; the log says why.");
        }
    }
}
