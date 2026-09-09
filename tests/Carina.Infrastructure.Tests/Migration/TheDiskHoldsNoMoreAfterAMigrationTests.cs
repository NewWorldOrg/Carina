using Carina.Domain.Migration;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class TheDiskHoldsNoMoreAfterAMigrationTests : IDisposable
{
    private readonly ARunOverRealFiles run = new();

    public void Dispose() => run.Dispose();

    [Fact]
    public async Task WhatWasCarriedIsOneFileUnderTwoNamesRatherThanTwoFiles()
    {
        await run.RunAsync(MigrationPass.ForReal);

        IReadOnlyList<FileStanding> source = WhatTheFilesystemSays.Under(run.From);
        IReadOnlyList<FileStanding> arrived = WhatTheFilesystemSays.Under(run.Into);

        Assert.Equal(2, arrived.Count);

        foreach (string name in Carried)
        {
            FileStanding original = WhatTheFilesystemSays.Named(source, name);
            FileStanding link = arrived.Single(file => file.Inode == original.Inode);

            Assert.Equal(2, original.Links);
            Assert.Equal(2, link.Links);
            Assert.Equal(original.Size, link.Size);
            Assert.Equal(original.Digest, link.Digest);
            Assert.NotEqual(original.Name, link.Name);
        }

        Assert.Equal(1, WhatTheFilesystemSays.Named(source, MigrationOnRealFiles.NotARecording).Links);
        Assert.Equal(1, WhatTheFilesystemSays.Named(source, MigrationOnRealFiles.NothingLandedIn).Links);
    }

    [Fact]
    public async Task TheDiskHoldsNoMoreBlocksAfterARunThanItHeldBeforeIt()
    {
        long before = Held();

        await run.RunAsync(MigrationPass.ForReal);

        Assert.Equal(2, run.Recordings.Written.Count);
        Assert.True(before > 0, "A run that carried nothing off a disk holding nothing would prove nothing.");
        Assert.Equal(before, Held());
    }

    private static string[] Carried => [MigrationOnRealFiles.Carried, MigrationOnRealFiles.AlsoCarried];

    private long Held()
        => WhatTheFilesystemSays.BytesTheDiskHoldsFor(
            WhatTheFilesystemSays.Under(run.From),
            WhatTheFilesystemSays.Under(run.Into));
}
