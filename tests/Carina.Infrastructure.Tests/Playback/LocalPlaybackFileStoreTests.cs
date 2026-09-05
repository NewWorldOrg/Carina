using Carina.Domain.Integrity;
using Carina.Domain.Playback;
using Carina.Domain.Recordings;
using Carina.Domain.Streaming;
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

        PlaybackFileSearch search = Store().Find(Root, Named);

        Assert.Null(search.Absence);
        Assert.NotNull(search.Found);
        Assert.Equal(4_000, search.Found.Bytes);
        Assert.Equal(Root, search.Found.Root);
        Assert.Equal(Named, search.Found.Name);
    }

    [Fact]
    public void AFileOfNoBytesIsFoundHoldingNothingRatherThanNotFound()
    {
        Write(0);

        PlaybackFile? found = Store().Find(Root, Named).Found;

        Assert.NotNull(found);
        Assert.Equal(0, found.Bytes);
        Assert.False(found.HoldsAnything);
    }

    [Fact]
    public void AFileThatIsNotThereWhileItsRootIsIsGone()
    {
        PlaybackFileSearch search = Store().Find(Root, Named);

        Assert.Null(search.Found);
        Assert.Equal(PlaybackFileAbsence.Gone, search.Absence);
    }

    [Fact]
    public void AFileWhoseRootDirectoryIsNotThereIsOutOfReachRatherThanGone()
    {
        PlaybackFileSearch search = Store(new IntegritySettings
        {
            OutputRoots = [new StorageRootPath(Root, Path.Combine(mounted.FullName, "unmounted"))],
        }).Find(Root, Named);

        Assert.Null(search.Found);
        Assert.Equal(PlaybackFileAbsence.OutOfReach, search.Absence);
    }

    [Fact]
    public void ARootNothingSaysWhereToFindIsNotSearchedAndIsOutOfReach()
    {
        Write(4_000);

        PlaybackFileSearch search = Store(new IntegritySettings()).Find(Root, Named);

        Assert.Null(search.Found);
        Assert.Equal(PlaybackFileAbsence.OutOfReach, search.Absence);
    }

    [Fact]
    public void WhatIsOpenedReadsBackTheBytesThatWereWritten()
    {
        byte[] written = Write(1_024);
        PlaybackFile found = Found();

        PlaybackFileOpening opened = Store().OpenRead(found);
        using Stream reading = opened.Reading!;
        var read = new MemoryStream();
        reading.CopyTo(read);

        Assert.Null(opened.Absence);
        Assert.Equal(written, read.ToArray());
        Assert.True(reading.CanSeek);
        Assert.False(reading.CanWrite);
    }

    [Fact]
    public void AFileTakenOffTheDiskBeforeItWasOpenedIsGone()
    {
        Write(16);
        PlaybackFile found = Found();
        File.Delete(Path.Combine(mounted.FullName, Named.Value));

        PlaybackFileOpening opened = Store().OpenRead(found);

        Assert.Null(opened.Reading);
        Assert.Equal(PlaybackFileAbsence.Gone, opened.Absence);
    }

    [Fact]
    public void ARootThatWentAwayBeforeTheFileWasOpenedIsOutOfReachRatherThanGone()
    {
        Write(16);
        PlaybackFile found = Found();
        mounted.Delete(recursive: true);

        PlaybackFileOpening opened = Store().OpenRead(found);

        Assert.Null(opened.Reading);
        Assert.Equal(PlaybackFileAbsence.OutOfReach, opened.Absence);
    }

    [Fact]
    public void ARootNothingSaysWhereToFindOpensAsOutOfReach()
    {
        Write(16);

        PlaybackFileOpening opened = Store(new IntegritySettings()).OpenRead(Found());

        Assert.Null(opened.Reading);
        Assert.Equal(PlaybackFileAbsence.OutOfReach, opened.Absence);
    }

    [Fact]
    public void AFileIsNamedForAProgrammeThatOpensItItselfRatherThanBeingHandedTheBytes()
    {
        Write(16);
        PlaybackFile found = Found();

        StreamSource? source = Store().SourceOf(found);

        Assert.NotNull(source);
        Assert.Equal(Path.Combine(mounted.FullName, Named.Value), source.Value);
    }

    [Fact]
    public void ARootNothingSaysWhereToFindIsNamedToNoProgramme()
    {
        Write(16);
        PlaybackFile found = Found();

        Assert.Null(Store(new IntegritySettings()).SourceOf(found));
    }

    [Fact]
    public void TheStoreIsAskedForSomethingRatherThanNothing()
    {
        Assert.Throws<ArgumentNullException>(() => Store().Find(null!, Named));
        Assert.Throws<ArgumentNullException>(() => Store().Find(Root, null!));
        Assert.Throws<ArgumentNullException>(() => Store().OpenRead(null!));
        Assert.Throws<ArgumentNullException>(() => Store().SourceOf(null!));
    }

    public void Dispose()
    {
        if (Directory.Exists(mounted.FullName))
        {
            mounted.Delete(recursive: true);
        }
    }

    private PlaybackFile Found()
    {
        PlaybackFile? found = Store().Find(Root, Named).Found;

        Assert.NotNull(found);

        return found;
    }

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
