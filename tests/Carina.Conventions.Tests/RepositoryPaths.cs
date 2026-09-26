namespace Carina.Conventions.Tests;

public static class RepositoryPaths
{
    private const string RootMarker = "Carina.slnx";

    public static string Root { get; } = FindRoot();

    public static string SourceDirectory { get; } = Path.Combine(Root, "src");

    private static string FindRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

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
