using Carina.Domain.Integrity;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Playback;

using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Playback;

public sealed class LocalPlaybackFileStoreTests : IDisposable
{
    private static readonly OutputRoot Root = new("bulk");

    private static readonly RecordingFileName Named = new("a1b2c3.m2ts");

    private readonly DirectoryInfo mounted = Directory.CreateTempSubdirectory("carina-playback-store-");

    [Fact]
    public void AFileOnTheDiskIsFoundWithTheNumberOfBytesItHolds()
    {
        Write(4_000);

        PlaybackFile? found = Store().Find(Root, Named);

        Assert.NotNull(found);
        Assert.Equal(4_000, found.Bytes);
        Assert.Equal(Root, found.Root);
        Assert.Equal(Named, found.Name);
    }

    [Fact]
    public void AFileOfNoBytesIsFoundHoldingNothingRatherThanNotFound()
    {
        Write(0);

        PlaybackFile? found = Store().Find(Root, Named);

        Assert.NotNull(found);
        Assert.Equal(0, found.Bytes);
        Assert.False(found.HoldsAnything);
    }

    [Fact]
    public void AFileThatIsNotThereIsNotFound()
    {
        Assert.Null(Store().Find(Root, Named));
    }

    [Fact]
    public void ARootNothingSaysWhereToFindIsNotSearched()
    {
        Write(4_000);

        Assert.Null(Store(new IntegritySettings()).Find(Root, Named));
    }

    [Fact]
    public void WhatIsOpenedReadsBackTheBytesThatWereWritten()
    {
        byte[] written = Write(1_024);
        PlaybackFile found = Store().Find(Root, Named)!;

        using Stream reading = Store().OpenRead(found)!;
        var read = new MemoryStream();
        reading.CopyTo(read);

        Assert.Equal(written, read.ToArray());
        Assert.True(reading.CanSeek);
        Assert.False(reading.CanWrite);
    }

    [Fact]
    public void AFileThatWentAwayBeforeItWasOpenedOpensAsNothing()
    {
        Write(16);
        PlaybackFile found = Store().Find(Root, Named)!;
        File.Delete(Path.Combine(mounted.FullName, Named.Value));

        Assert.Null(Store().OpenRead(found));
    }

    [Fact]
    public void TheStoreIsAskedForSomethingRatherThanNothing()
    {
        Assert.Throws<ArgumentNullException>(() => Store().Find(null!, Named));
        Assert.Throws<ArgumentNullException>(() => Store().Find(Root, null!));
        Assert.Throws<ArgumentNullException>(() => Store().OpenRead(null!));
    }

    public void Dispose() => mounted.Delete(recursive: true);

    private LocalPlaybackFileStore Store() => Store(new IntegritySettings
    {
        OutputRoots = [new StorageRootPath(Root, mounted.FullName)],
    });

    private static LocalPlaybackFileStore Store(IntegritySettings mounts)
        => new(mounts, NullLogger<LocalPlaybackFileStore>.Instance);

    private byte[] Write(int count)
    {
        byte[] bytes = [.. Enumerable.Range(0, count).Select(index => (byte)(index % 251))];
        File.WriteAllBytes(Path.Combine(mounted.FullName, Named.Value), bytes);

        return bytes;
    }
}
