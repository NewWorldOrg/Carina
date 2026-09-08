using Carina.Domain.Migration;

namespace Carina.Infrastructure.Migration;

public sealed class LocalMigrationSourceDirectory : IMigrationSourceDirectory
{
    public static readonly EnumerationOptions HowItWalks = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = false,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    private readonly string root;

    public LocalMigrationSourceDirectory(string from)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);

        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(from));
    }

    public Task<IReadOnlyList<SourceFile>> ListAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(root))
        {
            throw new MigrationSourceUnreadableException(
                "The output directory of the system being replaced is not there, and an empty listing would "
                + "read as a system that had recorded nothing.");
        }

        List<SourceFile> found = [];

        try
        {
            foreach (string entry in Directory.EnumerateFiles(root, "*", HowItWalks))
            {
                cancellationToken.ThrowIfCancellationRequested();

                FileInfo file = new(entry);

                if (file.Exists)
                {
                    found.Add(new SourceFile(Under(root, entry), file.Length));
                }
            }
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            throw new MigrationSourceUnreadableException(
                "The output directory of the system being replaced could not be walked to the end, and a part "
                + $"of it would read as the whole of it ({unreadable.GetType().Name}).");
        }

        return Task.FromResult<IReadOnlyList<SourceFile>>(
            [.. found.OrderBy(file => file.Path, StringComparer.Ordinal)]);
    }

    private static string Under(string from, string entry)
        => string.Join('/', Path.GetRelativePath(from, entry).Split(Path.DirectorySeparatorChar));
}
