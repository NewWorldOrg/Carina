using Carina.Domain.Migration;

namespace Carina.Infrastructure.Tests.Migration;

public sealed class TheSourceOutlivesAMigrationTests : IDisposable
{
    private readonly ARunOverRealFiles run = new();

    public void Dispose() => run.Dispose();

    [Fact]
    public async Task ARunForRealRenamesCopiesAndDeletesNothingAndChangesNotOneByte()
    {
        IReadOnlyList<FileStanding> before = WhatTheFilesystemSays.Under(run.From);

        await run.RunAsync(MigrationPass.ForReal);

        IReadOnlyList<FileStanding> after = WhatTheFilesystemSays.Under(run.From);

        Assert.Equal(2, run.Recordings.Written.Count);
        Assert.Equal(4, after.Count);
        Assert.Equal(WhatTheFilesystemSays.Identities(before), WhatTheFilesystemSays.Identities(after));
    }

    [Fact]
    public async Task ARehearsalRenamesCopiesAndDeletesNothingAndChangesNotOneByte()
    {
        IReadOnlyList<FileStanding> before = WhatTheFilesystemSays.Under(run.From);

        await run.RunAsync(MigrationPass.Rehearsal);

        IReadOnlyList<FileStanding> after = WhatTheFilesystemSays.Under(run.From);

        Assert.Single(run.Records.Saved);
        Assert.Empty(run.Recordings.Written);
        Assert.Empty(Directory.GetFileSystemEntries(run.Into));
        Assert.Equal(WhatTheFilesystemSays.Identities(before), WhatTheFilesystemSays.Identities(after));
        Assert.Equal(before.Select(file => file.Links), after.Select(file => file.Links));
    }
}
