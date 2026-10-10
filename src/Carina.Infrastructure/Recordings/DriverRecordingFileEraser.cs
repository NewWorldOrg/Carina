using Carina.Contracts;
using Carina.Domain.Captions;
using Carina.Domain.Driver;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.DataBroadcast;
using Carina.Infrastructure.Thumbnails;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Recordings;

public sealed class DriverRecordingFileEraser(
    IDriverClient driver,
    IRecordingFileSurvey survey,
    ThumbnailSettings pictures,
    CaptionSettings captions,
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
            ErasureFault fault = FaultIn(call);

            return RecordingErasure.Refused(
                fault,
                Describe(call),
                fault is ErasureFault.FileLeftBehind ? 1 + DerivedStillThere(id) : null);
        }

        int removed = erased.FileRemoved ? 1 : 0;

        foreach ((string shelf, string derived, string what) in Derived(id))
        {
            bool wasThere = File.Exists(derived);

            if (Unlink(shelf, derived, what) is { } left)
            {
                return left;
            }

            removed += wasThere ? 1 : 0;
        }

        logger.LogInformation(
            "Recording {Recording} was asked for by hand and the process that owns output root {Root} took "
            + "its file off the disk.",
            id.Wire,
            root.Value);

        return RecordingErasure.Erased(removed);
    }

    private int DerivedStillThere(RecordingId id) => Derived(id).Count(derived => File.Exists(derived.Path));

    private IReadOnlyList<(string Shelf, string Path, string What)> Derived(RecordingId id)
    {
        List<(string Shelf, string Path, string What)> derived = [];

        if (pictures.WrittenTo is { } gallery)
        {
            derived.Add((gallery, Path.Combine(gallery, id.Wire + ThumbnailJob.Extension), "picture drawn of this recording"));
        }

        if (captions.WrittenTo is { } shelf)
        {
            derived.Add((shelf, Path.Combine(shelf, id.Wire + CaptionSettings.Extension), "captions taken from this recording"));
            derived.Add((shelf, Path.Combine(shelf, id.Wire + DataBroadcastShelf.Extension), "record of the data broadcast taken from this recording"));
        }

        return derived;
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

    private RecordingErasure? Unlink(string shelf, string path, string what)
    {
        if (!RecordingFilePlace.LiesDirectlyUnder(shelf, path))
        {
            logger.LogWarning(
                "The {What} resolves outside the directory it is kept in, so nothing is removed.",
                what);

            return RecordingErasure.Refused(
                ErasureFault.FileLeftBehind,
                $"The {what} does not resolve to a file in the directory it is kept in, so it is left where it is.",
                1);
        }

        try
        {
            File.Delete(path);

            return null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(failure, "The {What} could not be removed.", what);

            return RecordingErasure.Refused(
                ErasureFault.FileLeftBehind,
                $"The {what} could not be removed; the log says why.",
                1);
        }
    }
}
