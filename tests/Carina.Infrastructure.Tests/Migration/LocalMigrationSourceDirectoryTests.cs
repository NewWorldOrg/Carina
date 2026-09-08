using Carina.Domain.Migration;
using Carina.Infrastructure.Migration;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class LocalMigrationSourceDirectoryTests : IDisposable
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private readonly string from = Directory.CreateTempSubdirectory("carina-source-directory").FullName;

    public void Dispose() => Directory.Delete(from, recursive: true);

    [Fact]
    public async Task EveryFileUnderTheOutputDirectoryIsListedWhateverItIs()
    {
        Write("one.m2ts", "a recording");
        Write("bash.sh", "not a recording at all");
        Write("half.tmp", "cut off");

        IReadOnlyList<SourceFile> found = await Walking().ListAsync(Cancel);

        Assert.Equal(["bash.sh", "half.tmp", "one.m2ts"], found.Select(file => file.Path));
    }

    [Fact]
    public async Task AFileIsListedAtTheSizeItReallyIsAndNotTheSizeAnybodyClaims()
    {
        Write("one.m2ts", "a recording");
        Write("empty.m2ts", string.Empty);

        IReadOnlyList<SourceFile> found = await Walking().ListAsync(Cancel);

        Assert.Equal(0, found.Single(file => file.Path is "empty.m2ts").SizeBytes);
        Assert.Equal(11, found.Single(file => file.Path is "one.m2ts").SizeBytes);
    }

    [Fact]
    public async Task AFileUnderASubdirectoryIsNamedFromTheOutputDirectoryDown()
    {
        Directory.CreateDirectory(Path.Combine(from, "older"));
        Write(Path.Combine("older", "one.m2ts"), "a recording");

        IReadOnlyList<SourceFile> found = await Walking().ListAsync(Cancel);

        Assert.Equal(["older/one.m2ts"], found.Select(file => file.Path));
    }

    [Fact]
    public async Task AnEmptyOutputDirectoryIsAnEmptyListAndNotARefusal()
        => Assert.Empty(await Walking().ListAsync(Cancel));

    [Fact]
    public async Task ADirectoryThatIsNotThereIsSaidToBeUnreadableRatherThanEmpty()
        => await Assert.ThrowsAsync<MigrationSourceUnreadableException>(
            () => new LocalMigrationSourceDirectory(Path.Combine(from, "nowhere")).ListAsync(Cancel));

    private LocalMigrationSourceDirectory Walking() => new(from);

    private void Write(string name, string content) => File.WriteAllText(Path.Combine(from, name), content);
}
