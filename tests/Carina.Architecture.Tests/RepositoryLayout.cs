namespace Carina.Architecture.Tests;

/// <summary>
/// Locates the repository on disk so that the rules can be checked against the
/// project files instead of against compiled output.
/// </summary>
public static class RepositoryLayout
{
    private const string RootMarker = "Carina.slnx";

    /// <summary>Absolute path of the repository root.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Absolute path of the production source directory.</summary>
    public static string SourceDirectory { get; } = Path.Combine(Root, "src");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, RootMarker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root: no {RootMarker} found above {AppContext.BaseDirectory}.");
    }
}
