using System.Text;

using Carina.Driver.Configuration;

namespace Carina.Driver.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private const string Before = """{"devices":[{"id":"adapter0.frontend0"}]}""";
    private const string After = """{"devices":[{"id":"adapter1.frontend0"}]}""";

    private readonly string root = Directory
        .CreateTempSubdirectory("carina-atomic-")
        .FullName;

    private readonly string target;

    public AtomicFileTests()
    {
        target = Path.Combine(root, "driver.json");
        File.WriteAllText(target, Before);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void AReplacedFileHoldsTheNewContent()
    {
        AtomicFile.Replace(target, After);

        Assert.Equal(After, File.ReadAllText(target));
    }

    [Fact]
    public void TheNewContentIsWrittenBesideTheTargetSoThatPuttingItInPlaceIsOneRename()
    {
        var staged = AtomicFile.Stage(target, After);

        Assert.Equal(Path.GetDirectoryName(target), Path.GetDirectoryName(staged));
        Assert.NotEqual(target, staged);
    }

    [Fact]
    public void AWriteThatStopsBeforeTheRenameLeavesTheOldFileWhole()
    {
        AtomicFile.Stage(target, After);

        Assert.Equal(Before, File.ReadAllText(target));
    }

    [Fact]
    public void TheTargetIsNeverOpenedForWritingSoItCannotBeCaughtHalfWritten()
    {
        var written = File.GetLastWriteTimeUtc(target);

        AtomicFile.Stage(target, After);

        Assert.Equal(written, File.GetLastWriteTimeUtc(target));
        Assert.Equal(Before.Length, new FileInfo(target).Length);
    }

    [Fact]
    public void TheRenameSwapsTheWholeFileRatherThanGrowingIt()
    {
        var staged = AtomicFile.Stage(target, After);

        AtomicFile.Commit(staged, target);

        Assert.Equal(After, File.ReadAllText(target));
        Assert.False(File.Exists(staged));
    }

    [Fact]
    public void AReplaceThatFailsLeavesNothingBehindToBeMistakenForTheLedger()
    {
        Directory.CreateDirectory(Path.Combine(root, "occupied"));

        Assert.ThrowsAny<Exception>(() =>
            AtomicFile.Replace(Path.Combine(root, "occupied"), After)
        );

        Assert.Equal([target], Directory.EnumerateFiles(root));
    }

    [Fact]
    public void TheContentIsWrittenAsBytesWithNoPreambleForTheReaderToStumbleOn()
    {
        AtomicFile.Replace(target, After);

        Assert.Equal(Encoding.UTF8.GetBytes(After), File.ReadAllBytes(target));
    }

    [Fact]
    public void ReplacingAFileThatIsNotThereYetCreatesIt()
    {
        var fresh = Path.Combine(root, "fresh.json");

        AtomicFile.Replace(fresh, After);

        Assert.Equal(After, File.ReadAllText(fresh));
    }
}
