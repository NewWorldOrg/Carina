using Carina.Domain.Segments;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// The reduced copies under <see cref="LearningImportSettings.ImportFrom"/>, one directory each, looked at
/// in the order of their names, and the one to import next: a copy that can be read
/// (<see cref="ReducedCopyReader"/>) of a recording whose record <see cref="LearningExtraction.AwaitsImport"/>,
/// or that has no record yet and is given one waiting with the copy of its programme. Claiming it puts its
/// record reading, unless another recording is being read. Each directory is looked at once a start: a copy
/// that is not imported is said so with why, and one already holding its data, or imported, is passed over
/// until the next start; a copy whose import was stopped is looked at again. Once nothing is left to import,
/// how many were imported and how many were not since the start is said once. Nothing under the directory
/// is written.
/// </summary>
public sealed class ReducedCopyImports(
    LearningImportSettings settings,
    IReducedCopyImporter importer,
    LearningRecords records,
    TimeProvider clock,
    ILogger<ReducedCopyImports> logger)
{
    private static readonly ExtractionVersion Reduced = ExtractionVersion.CurrentFromReducedCopy;

    private readonly Lock gate = new();

    private readonly HashSet<string> lookedAt = new(StringComparer.Ordinal);

    private int imported;

    private int notImported;

    private bool toldSince = true;

    private bool unlisted;

    /// <summary>
    /// Claims the next copy that waits to be imported, its record reading; null when none waits or another
    /// recording is being read.
    /// </summary>
    public async Task<ReducedCopy?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        if (settings.ImportFrom is not { } shelf || Listed(shelf) is not { } directories)
        {
            return null;
        }

        foreach (string directory in directories.Where(Unseen))
        {
            ReducedCopyRead read = ReducedCopyReader.Read(directory);

            if (read.Copy is not { } copy)
            {
                Refused(directory, read.Refusal);

                continue;
            }

            switch (await ClaimAsync(copy, cancellationToken))
            {
                case ExtractionChange.Written:
                    return copy;
                case ExtractionChange.AnotherIsReading:
                    return null;
                case ExtractionChange.KeptMoving:
                    continue;
                default:
                    Seen(directory);
                    continue;
            }
        }

        TellOnce(shelf);

        return null;
    }

    /// <summary>
    /// Imports a copy this claimed, and counts it once it has settled.
    /// </summary>
    public async Task ImportAsync(ReducedCopy copy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(copy);

        await importer.ImportAsync(copy, cancellationToken);

        LearningExtraction? record = await records.FindAsync(copy.Id, cancellationToken);
        bool kept = record is { State: LearningExtractionState.Done or LearningExtractionState.Partial } && Equals(record.Version, Reduced);

        lock (gate)
        {
            lookedAt.Add(copy.Directory);
            imported += kept ? 1 : 0;
            notImported += kept ? 0 : 1;
            toldSince = false;
        }
    }

    private async Task<ExtractionChange> ClaimAsync(ReducedCopy copy, CancellationToken cancellationToken)
    {
        if (await records.FindAsync(copy.Id, cancellationToken) is null)
        {
            await records.AddAsync(LearningExtraction.Waiting(copy.Id, copy.Programme, Now()), cancellationToken);
        }

        return await records.ChangeAsync(
            copy.Id,
            record =>
            {
                if (!record.AwaitsImport(Reduced))
                {
                    return false;
                }

                record.Import(Reduced, Now());

                return true;
            },
            cancellationToken);
    }

    private IReadOnlyList<string>? Listed(string shelf)
    {
        try
        {
            IReadOnlyList<string> directories =
            [
                .. Directory.EnumerateDirectories(shelf)
                    .Where(directory => !Path.GetFileName(directory).StartsWith('.'))
                    .Order(StringComparer.Ordinal),
            ];

            unlisted = false;

            return directories;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            if (!unlisted)
            {
                logger.LogWarning("The reduced copies under {Shelf} cannot be listed: {Reason}", shelf, unreadable.Message);
            }

            unlisted = true;

            return null;
        }
    }

    private bool Unseen(string directory)
    {
        lock (gate)
        {
            return !lookedAt.Contains(directory);
        }
    }

    private void Seen(string directory)
    {
        lock (gate)
        {
            lookedAt.Add(directory);
        }
    }

    private void Refused(string directory, string? why)
    {
        lock (gate)
        {
            lookedAt.Add(directory);
            notImported++;
            toldSince = false;
        }

        logger.LogWarning("The reduced copy in {Directory} is not imported: {Reason}", directory, why);
    }

    private void TellOnce(string shelf)
    {
        int done;
        int left;

        lock (gate)
        {
            if (toldSince)
            {
                return;
            }

            toldSince = true;
            done = imported;
            left = notImported;
        }

        logger.LogInformation(
            "Every reduced copy under {Shelf} has been looked at: {Imported} imported since the start, {NotImported} not imported.",
            shelf,
            done,
            left);
    }

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;
}
