using Carina.Contracts;
using Carina.Domain.Driver;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Integrity;

public sealed class DriverStrayFileEraser(
    IDriverClient driver,
    IRecordingFileSurvey survey,
    ILogger<DriverStrayFileEraser> logger) : IStrayFileEraser
{
    public async Task<StrayFileErasure> EraseAsync(IntegrityFinding finding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(finding);
        cancellationToken.ThrowIfCancellationRequested();

        if (StrayFileDisposal.Refusal(finding) is { } refusal)
        {
            throw new ArgumentException(
                $"A finding that is refused as {refusal} names no file to throw away from here.",
                nameof(finding));
        }

        DriverCall<IReadOnlyList<StorageRootDto>> storage = await driver.GetStorageAsync(cancellationToken);

        if (!storage.TryGetValue(out IReadOnlyList<StorageRootDto>? declared))
        {
            return StrayFileErasure.Refused(FaultIn(storage), Describe(storage));
        }

        RootListing listing = await survey.ListAsync(finding.Root, cancellationToken);

        if (OutputRootPresence.Missing(
                declared,
                finding.Root,
                listing.Reachable,
                [.. listing.Files.Select(file => file.Path)]) is { } absence)
        {
            logger.LogWarning(
                "A file no recording owns was asked to go and output root {Root} is {Absence}, so nothing "
                + "under it is removed.",
                finding.Root.Value,
                absence);

            return StrayFileErasure.Refused(StrayErasureFault.RootOutOfReach, OutOfReach(finding.Root, absence));
        }

        if (StrayFileDisposal.Change(finding, listing.At(finding.Path)) is { } change)
        {
            logger.LogInformation(
                "A file no recording owns under output root {Root} was asked to go and is {Change} since it "
                + "was found, so it is left where it is.",
                finding.Root.Value,
                change);

            return StrayFileErasure.Refused(StrayErasureFault.FileChanged, Changed(finding, change), change);
        }

        DriverCall<StrayFileErasedDto> call = await driver.EraseStrayFileAsync(
            new StrayFileErasureRequest
            {
                OutputRoot = finding.Root.Value,
                Path = finding.Path,
                SizeBytes = finding.ObservedSize ?? 0,
                LastWrittenAt = new DateTimeOffset(finding.LastWrittenAt!.Value, TimeSpan.Zero),
            },
            cancellationToken);

        if (!call.TryGetValue(out StrayFileErasedDto? erased))
        {
            return StrayFileErasure.Refused(FaultIn(call), Describe(call));
        }

        logger.LogInformation(
            "A file no recording owns was asked for by hand and the process that owns output root {Root} "
            + "took it off the disk.",
            finding.Root.Value);

        return StrayFileErasure.Erased(erased.FileRemoved);
    }

    private static StrayErasureFault FaultIn<T>(DriverCall<T> call)
    {
        if (call.Outcome is DriverCallOutcome.Unreachable)
        {
            return StrayErasureFault.DriverUnreachable;
        }

        return call.Problem?.Title switch
        {
            SessionRefusalTitles.OutputUnavailable => StrayErasureFault.RootOutOfReach,
            SessionRefusalTitles.StrayFileChanged => StrayErasureFault.FileChanged,
            SessionRefusalTitles.RecordingInProgress => StrayErasureFault.BeingWritten,
            SessionRefusalTitles.FileLeftBehind => StrayErasureFault.FileLeftBehind,
            _ => StrayErasureFault.DriverRefused,
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

    private static string Changed(IntegrityFinding finding, StrayFileChange change) => change switch
    {
        StrayFileChange.Gone => $"'{finding.Path}' under output root '{finding.Root.Value}' is no longer there, "
            + "so there is nothing of it to throw away.",
        StrayFileChange.Resized => $"'{finding.Path}' under output root '{finding.Root.Value}' is not the size "
            + "it was when it was found, so it is left where it is.",
        StrayFileChange.Rewritten => $"'{finding.Path}' under output root '{finding.Root.Value}' was written to "
            + "after it was found, so it is left where it is.",
        _ => throw new ArgumentOutOfRangeException(
            nameof(change),
            change,
            "A file that changed changed in one of the ways this type holds."),
    };

    private static string OutOfReach(OutputRoot root, RootAbsence absence) => absence switch
    {
        RootAbsence.Undeclared =>
            $"The process that owns the disk declares no output root called '{root.Value}', so nothing under "
            + "it is removed.",
        RootAbsence.OutOfReach =>
            $"Output root '{root.Value}' could not be read here, so nothing under it is removed.",
        RootAbsence.HoldsNothing =>
            $"Output root '{root.Value}' holds nothing at all, which is what it looks like when its mount has "
            + "gone, so nothing under it is removed.",
        _ => throw new ArgumentOutOfRangeException(
            nameof(absence),
            absence,
            "A root that is not there is missing in one of the ways this type holds."),
    };
}
