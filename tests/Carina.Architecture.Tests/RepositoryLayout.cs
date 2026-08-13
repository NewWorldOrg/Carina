namespace Carina.Architecture.Tests;

public static class RepositoryLayout
{
    private const string RootMarker = "Carina.slnx";

    public static string Root { get; } = FindRoot();

    public static string SourceDirectory { get; } = Path.Combine(Root, "src");

    public static string TestDirectory { get; } = Path.Combine(Root, "tests");

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
