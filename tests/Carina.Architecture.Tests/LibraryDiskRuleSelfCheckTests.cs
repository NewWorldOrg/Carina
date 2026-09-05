namespace Carina.Architecture.Tests;

public sealed class LibraryDiskRuleSelfCheckTests
{
    public static TheoryData<string> EachWayOfAskingTheDiskWhetherARecordingIsStillThere() =>
        new()
        {
            "private bool There(string path) => File.Exists(path);",
            "private bool There(string path) => Directory.Exists(path);",
            "private long Weight(string path) => new FileInfo(path).Length;",
            "private long Room(string path) => new DriveInfo(path).AvailableFreeSpace;",
            "private DateTime When(string path) => File.GetLastWriteTimeUtc(path);",
            "private IEnumerable<string> All(string path) => Directory.EnumerateFiles(path);",
        };

    [Theory]
    [MemberData(nameof(EachWayOfAskingTheDiskWhetherARecordingIsStillThere))]
    public void EveryWayOfAskingTheDiskFromTheLibraryIsCaught(string writes)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-library-disk-");

        try
        {
            Write(directory, "Carina.Domain/Library/Reader.cs", Source("Carina.Domain.Library", writes));

            Assert.NotEmpty(LibraryDiskRules.ReachesForTheDiskInsideTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheSameReachFromOutsideTheLibraryWalksPastThisRule()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-library-disk-elsewhere-");

        try
        {
            Write(
                directory,
                "Carina.Infrastructure/Integrity/Survey.cs",
                Source("Carina.Infrastructure.Integrity", "private bool There(string path) => File.Exists(path);"));

            Assert.Empty(LibraryDiskRules.ReachesForTheDiskInsideTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void ReadingTheLedgerAndSayingWhenSomebodyLastWeighedTheFileIsNotReachingForTheDisk()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-library-ledger-");

        try
        {
            Write(
                directory,
                "Carina.Domain/Library/Reader.cs",
                Source(
                    "Carina.Domain.Library",
                    "private long? Weight(Recording it) => it.FileSizeObserved;",
                    "private DateTime? When(Recording it) => it.ObservedAt;"));

            Assert.Empty(LibraryDiskRules.ReachesForTheDiskInsideTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string Source(string space, params string[] lines)
        => $"namespace {space};\n\npublic sealed class Reader\n{{\n    "
            + string.Join("\n    ", lines)
            + "\n}\n";

    private static void Write(DirectoryInfo directory, string relative, string source)
    {
        string path = Path.Combine(directory.FullName, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }
}
