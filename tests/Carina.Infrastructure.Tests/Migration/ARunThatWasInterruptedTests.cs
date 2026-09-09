using Carina.Domain.Migration;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class ARunThatWasInterruptedTests : IDisposable
{
    private readonly ARunOverRealFiles run = new();

    public void Dispose() => run.Dispose();

    [Fact]
    public async Task ARunThatDiesBeforeItLinksAnythingLeavesNoRowAndNoFileBehind()
    {
        run.Source.WhenWalking = new IOException("the machine went down before the run had read the disk");

        await Assert.ThrowsAsync<IOException>(() => run.RunAsync(MigrationPass.ForReal));

        Assert.Empty(run.Records.Saved);
        Assert.Empty(run.Recordings.Written);
        Assert.Empty(run.Bench.Rules.Rules);
        Assert.Empty(run.Bench.Jobs.Jobs);
        Assert.Empty(Directory.GetFileSystemEntries(run.Into));
        Assert.Equal(run.Lease.Taken, run.Lease.Returned);
    }

    [Fact]
    public async Task ARunThatDiesBetweenALinkAndItsRowWritesNoRecordOfItself()
    {
        Interrupt(after: 1);

        await Assert.ThrowsAsync<IOException>(() => run.RunAsync(MigrationPass.ForReal));

        Assert.Empty(run.Records.Saved);
        Assert.Single(run.Recordings.Written);
        Assert.Equal(2, Directory.GetFiles(run.Into).Length);
        Assert.Equal(run.Lease.Taken, run.Lease.Returned);
    }

    [Fact]
    public async Task EmptyingTheNewRootAndForgettingTheRowsIsAllItTakesAfterARunWasInterrupted()
    {
        IReadOnlyList<FileStanding> before = WhatTheFilesystemSays.Under(run.From);

        Interrupt(after: 1);
        await Assert.ThrowsAsync<IOException>(() => run.RunAsync(MigrationPass.ForReal));

        run.Recordings.WhenAdding = null;
        run.Recordings.Written.Clear();
        run.Bench.Rules.Rules.Clear();
        run.Bench.Jobs.Jobs.Clear();

        foreach (string stray in Directory.GetFiles(run.Into))
        {
            File.Delete(stray);
        }

        await run.RunAsync(MigrationPass.ForReal);

        IReadOnlyList<FileStanding> source = WhatTheFilesystemSays.Under(run.From);
        IReadOnlyList<FileStanding> arrived = WhatTheFilesystemSays.Under(run.Into);

        Assert.Equal(2, run.Recordings.Written.Count);
        Assert.Single(run.Records.Saved);
        Assert.Equal(WhatTheFilesystemSays.Identities(before), WhatTheFilesystemSays.Identities(source));
        Assert.Equal(
            arrived.Select(file => file.Inode).Order(),
            source.Where(file => file.Links is 2).Select(file => file.Inode).Order());
        Assert.Equal(
            run.Recordings.Written.Select(recording => recording.FileName.Value).Order(StringComparer.Ordinal),
            arrived.Select(file => file.Name).Order(StringComparer.Ordinal));
    }

    private void Interrupt(int after)
    {
        run.Recordings.WrittenBeforeItRefuses = after;
        run.Recordings.WhenAdding = new IOException("the machine went down part way through the run");
    }
}
