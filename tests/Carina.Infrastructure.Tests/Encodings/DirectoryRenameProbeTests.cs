using Carina.Infrastructure.Encodings;
using Carina.Infrastructure.Tests.Integrity;

namespace Carina.Infrastructure.Tests.Encodings;

public sealed class DirectoryRenameProbeTests
{
    private readonly DirectoryRenameProbe probe = new();

    [Fact(DisplayName = "BR-ED2-009: within one directory a move is a rename, and the probe leaves nothing behind")]
    public void WithinOneDirectoryAMoveIsARenameAndTheProbeLeavesNothingBehind()
    {
        using var room = new TempTree();
        room.Holding("kept.txt", 3);

        RenameVerdict verdict = probe.Probe(room.Root, room.Root);

        Assert.Equal(RenameStanding.WouldBeARename, verdict.Standing);
        Assert.Equal(["file kept.txt 3 " + Sha("kept.txt", 3)], room.Snapshot().Select(entry => entry[..entry.LastIndexOf(' ')] + " " + entry[(entry.LastIndexOf(' ') + 1)..]));
    }

    [Fact(DisplayName = "BR-ED2-009: between two directories on one mount a move is a rename, and neither side keeps the probe")]
    public void BetweenTwoDirectoriesOnOneMountAMoveIsARename()
    {
        using var workshop = new TempTree();
        using var room = new TempTree();

        RenameVerdict verdict = probe.Probe(workshop.Root, room.Root);

        Assert.Equal(RenameStanding.WouldBeARename, verdict.Standing);
        Assert.Empty(workshop.Snapshot());
        Assert.Empty(room.Snapshot());
    }

    [Fact(DisplayName = "BR-ED2-009: the kernel's refusal to rename across mounts is read as exactly that")]
    public void TheKernelsRefusalToRenameAcrossMountsIsReadAsExactlyThat()
    {
        RenameVerdict verdict = DirectoryRenameProbe.Read(new IOException("Invalid cross-device link", DirectoryRenameProbe.CrossDeviceLink));

        Assert.Equal(RenameStanding.WouldCrossAMount, verdict.Standing);
        Assert.Equal("Invalid cross-device link", verdict.Note);
    }

    [Fact]
    public void AnyOtherRefusalToRenameIsReadAsBeingUnableToWriteThere()
    {
        RenameVerdict verdict = DirectoryRenameProbe.Read(new IOException("Read-only file system", 30));

        Assert.Equal(RenameStanding.CannotWriteTo, verdict.Standing);
    }

    [Fact]
    public void ADirectoryThatIsNotThereCannotBeWrittenFrom()
    {
        using var room = new TempTree();

        RenameVerdict verdict = probe.Probe(room.Under("nowhere"), room.Root);

        Assert.Equal(RenameStanding.CannotWriteFrom, verdict.Standing);
        Assert.Empty(room.Snapshot());
    }

    [Fact]
    public void ADestinationThatIsNotThereCannotBeWrittenToAndTheProbeIsTakenBackOut()
    {
        using var workshop = new TempTree();
        using var room = new TempTree();

        RenameVerdict verdict = probe.Probe(workshop.Root, room.Under("nowhere"));

        Assert.Equal(RenameStanding.CannotWriteTo, verdict.Standing);
        Assert.Empty(workshop.Snapshot());
        Assert.Empty(room.Snapshot());
    }

    [Fact]
    public void ADestinationThatIsAFileCannotBeWrittenTo()
    {
        using var workshop = new TempTree();
        using var room = new TempTree();
        room.Holding("not-a-directory", 1);

        RenameVerdict verdict = probe.Probe(workshop.Root, room.Under("not-a-directory"));

        Assert.Equal(RenameStanding.CannotWriteTo, verdict.Standing);
        Assert.Empty(workshop.Snapshot());
    }

    private static string Sha(string name, int size)
    {
        using var tree = new TempTree();
        tree.Holding(name, size);

        return tree.Snapshot().Single()[^64..];
    }
}
