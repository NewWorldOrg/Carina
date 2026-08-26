namespace Carina.Architecture.Tests;

public sealed class FileSystemRuleSelfCheckTests
{
    [Theory]
    [InlineData("File.Delete(path);", "File.Delete")]
    [InlineData("System.IO.File.Delete(path);", "File.Delete")]
    [InlineData("Func<string, FileStream> wipe = File.Create;", "File.Create")]
    [InlineData("var wipe = System.IO.File.Create;", "File.Create")]
    [InlineData("File . Delete (path);", "File.Delete")]
    [InlineData("File.Move(from, to, true);", "File.Move")]
    [InlineData("File.Replace(a, b, null);", "File.Replace")]
    [InlineData("File.WriteAllBytes(path, bytes);", "File.WriteAllBytes")]
    [InlineData("File.AppendAllText(path, text);", "File.AppendAllText")]
    [InlineData("File.OpenWrite(path);", "File.OpenWrite")]
    [InlineData("Directory.Delete(path, true);", "Directory.Delete")]
    [InlineData("Directory.Move(from, to);", "Directory.Move")]
    [InlineData("found.Delete();", ".Delete(")]
    [InlineData("found.MoveTo(elsewhere);", ".MoveTo(")]
    [InlineData(
        "if (found.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-30)) { found.Create().Dispose(); }",
        ".Create(")]
    [InlineData("using (StreamWriter writing = found.CreateText()) { }", ".CreateText(")]
    [InlineData("held.SetLength(0);", ".SetLength(")]
    [InlineData("new FileStream(path, FileMode.Create);", "FileMode.")]
    [InlineData("new StreamWriter(path);", "newStreamWriter")]
    [InlineData("new BinaryWriter(stream);", "newBinaryWriter")]
    [InlineData("[DllImport(\"libc\")]", "DllImport")]
    [InlineData("[LibraryImport(\"libc\")]", "LibraryImport")]
    [InlineData("NativeLibrary.GetExport(handle, \"unlink\");", "NativeLibrary")]
    [InlineData("Process.Start(\"rm\", path);", "Process.Start")]
    [InlineData("new ProcessStartInfo(\"rm\");", "ProcessStartInfo")]
    [InlineData("typeof(File).GetMethod(\"Delete\");", "GetMethod(")]
    [InlineData("typeof(File).GetMember(\"Delete\");", "GetMember(")]
    [InlineData("Activator.CreateInstance(type);", "Activator.CreateInstance")]
    [InlineData("Delegate.CreateDelegate(type, method);", "CreateDelegate")]
    public void TheRuleSeesThisWayOfChangingWhatIsOnDisk(string source, string named)
    {
        Assert.Contains(named, FileSystemRules.WhatCouldChangeWhatIsOnDiskIn(source), StringComparer.Ordinal);
    }

    [Theory]
    [InlineData("long weighed = new FileInfo(path).Length;")]
    [InlineData("bool there = File.Exists(path);")]
    [InlineData("foreach (string entry in Directory.EnumerateFiles(path)) { }")]
    [InlineData("string held = File.ReadAllText(path);")]
    [InlineData("bool room = Directory.Exists(path);")]
    public void TheRuleLeavesAWayOfOnlyLookingAlone(string source)
    {
        Assert.Empty(FileSystemRules.WhatCouldChangeWhatIsOnDiskIn(source));
    }

    [Theory]
    [InlineData("Type.GetType(\"System.IO.\" + \"File\")!.InvokeMember(\"Delete\", flags, null, null, [path]);")]
    [InlineData("dynamic reached = held; reached.Delete;")]
    [InlineData("((delegate*<byte*, int>)pointer)(name);")]
    public void TheRuleCannotSeeThisWayOfChangingWhatIsOnDisk(string source)
    {
        Assert.Empty(FileSystemRules.WhatCouldChangeWhatIsOnDiskIn(source));
    }

    [Fact]
    public void TheRuleReadsTheWholeOfTheSourceTreeAndNotOneFeatureOfIt()
    {
        IReadOnlyList<string> found = FileSystemRules.WhatCouldChangeWhatIsOnDisk(RepositoryLayout.SourceDirectory);

        Assert.Contains(found, entry => entry.StartsWith("/Carina.Driver/", StringComparison.Ordinal));
        Assert.Contains(found, entry => entry.StartsWith("/Carina.Api/", StringComparison.Ordinal));
        Assert.Contains(found, entry => entry.StartsWith("/Carina.Broadcast/", StringComparison.Ordinal));
        Assert.Contains(found, entry => entry.StartsWith("/Carina.Infrastructure/", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRuleNamesTheFileAndTheWayTogetherSoOneMoreWayInOneFileIsSeen()
    {
        Assert.Equal(
            [".Replace(", "File.Create"],
            FileSystemRules.WhatCouldChangeWhatIsOnDiskIn(
                "path.Replace('a', 'b'); Func<string, FileStream> wipe = File.Create;"));
    }

    [Fact]
    public void TheRuleReadsNothingOutOfNothing()
    {
        Assert.Empty(FileSystemRules.WhatCouldChangeWhatIsOnDiskIn(string.Empty));
    }

    [Fact]
    public void ReadingNothingAtAllIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => FileSystemRules.WhatCouldChangeWhatIsOnDiskIn(null!));
    }
}
