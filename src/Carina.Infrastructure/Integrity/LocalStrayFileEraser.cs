using Carina.Contracts;
using Carina.Domain.Integrity;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Integrity;

/// <summary>
/// Throws away a file no ledger claims from a place this process writes into itself, after reading
/// the place and the file again: it refuses a path that leaves the place or passes through a link, a
/// place that cannot be read or holds nothing, and a file that is gone or has changed since it was
/// found.
/// </summary>
public sealed class LocalStrayFileEraser(
    LocalWrittenFileSurvey places,
    ILogger<LocalStrayFileEraser> logger) : IStrayFileEraser
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

        if (places.WhereIs(finding.Root) is not { } room)
        {
            return StrayFileErasure.Refused(
                StrayErasureFault.RootOutOfReach,
                $"'{finding.Root.Value}' is not a place this process writes into, so nothing under it is removed.");
        }

        if (PlaceUnder(room, finding.Path) is not { } full || ThroughALink(room, full))
        {
            return StrayFileErasure.Refused(
                StrayErasureFault.FileLeftBehind,
                $"'{finding.Path}' does not name a file inside '{finding.Root.Value}' reached without following "
                + "a link, so nothing is removed.");
        }

        RootListing listing = await places.ListAsync(finding.Root, cancellationToken);

        if (!listing.Reachable || listing.Files.Count is 0)
        {
            return StrayFileErasure.Refused(
                StrayErasureFault.RootOutOfReach,
                $"'{finding.Root.Value}' could not be read here or holds nothing at all, which is what it looks "
                + "like when its mount has gone, so nothing under it is removed.");
        }

        if (StrayFileDisposal.Change(finding, listing.At(finding.Path)) is { } change)
        {
            return StrayFileErasure.Refused(StrayErasureFault.FileChanged, Changed(finding, change), change);
        }

        if (ChangedJustNow(new FileInfo(full), finding) is { } now)
        {
            return StrayFileErasure.Refused(StrayErasureFault.FileChanged, Changed(finding, now), now);
        }

        try
        {
            File.Delete(full);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                failure,
                "{Path} under {Root}, which nothing claims, could not be removed.",
                finding.Path,
                finding.Root.Value);

            return StrayFileErasure.Refused(
                StrayErasureFault.FileLeftBehind,
                $"'{finding.Path}' under '{finding.Root.Value}' could not be removed; the log says why.");
        }

        logger.LogInformation(
            "{Path} under {Root}, which nothing claims, was removed on request.",
            finding.Path,
            finding.Root.Value);

        return StrayFileErasure.Erased(true);
    }

    /// <summary>
    /// The full path <paramref name="path"/> names inside <paramref name="room"/>, or nothing when it is
    /// empty, rooted, holds a separator or a character no name holds, or climbs out.
    /// </summary>
    public static string? PlaceUnder(string room, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(room);

        if (string.IsNullOrEmpty(path)
            || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains('\0', StringComparison.Ordinal)
            || Path.IsPathRooted(path)
            || path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            return null;
        }

        string held = Path.TrimEndingDirectorySeparator(Path.GetFullPath(room));
        string full = Path.GetFullPath(Path.Combine(held, path));

        return full.StartsWith(held + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? full : null;
    }

    private static bool ThroughALink(string room, string full)
    {
        string held = Path.TrimEndingDirectorySeparator(Path.GetFullPath(room));
        string? current = full;

        try
        {
            while (current is not null && !string.Equals(current, held, StringComparison.Ordinal))
            {
                if (Path.Exists(current) && new FileInfo(current).LinkTarget is not null)
                {
                    return true;
                }

                current = Path.GetDirectoryName(current);
            }

            return current is null;
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static StrayFileChange? ChangedJustNow(FileInfo file, IntegrityFinding finding)
    {
        if (!file.Exists)
        {
            return StrayFileChange.Gone;
        }

        if (file.Length != finding.ObservedSize)
        {
            return StrayFileChange.Resized;
        }

        return StrayFileStamp.Truncated(file.LastWriteTimeUtc) == finding.LastWrittenAt
            ? null
            : StrayFileChange.Rewritten;
    }

    private static string Changed(IntegrityFinding finding, StrayFileChange change) => change switch
    {
        StrayFileChange.Gone => $"'{finding.Path}' under '{finding.Root.Value}' is no longer there, so there is "
            + "nothing of it to throw away.",
        StrayFileChange.Resized => $"'{finding.Path}' under '{finding.Root.Value}' is not the size it was when it "
            + "was found, so it is left where it is.",
        StrayFileChange.Rewritten => $"'{finding.Path}' under '{finding.Root.Value}' was written to after it was "
            + "found, so it is left where it is.",
        _ => throw new ArgumentOutOfRangeException(
            nameof(change),
            change,
            "A file that changed changed in one of the ways this type holds."),
    };
}
