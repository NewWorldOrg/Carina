using Carina.Domain.Library;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Library;

public sealed class LibraryRecordingBlockTests
{
    [Fact]
    public void ARecordingThatBelongsToNoBroadcastStandsOnItsOwn()
    {
        LibraryRecordingSummary alone = Row();

        IReadOnlyList<LibraryRecordingBlock> blocks = LibraryRecordingBlock.Folded([alone]);

        Assert.Equal([alone], Assert.Single(blocks).Segments);
        Assert.Null(blocks[0].Key);
    }

    [Fact]
    public void TheSegmentsOfOneBroadcastAreShownAsOneBlockRatherThanThreeRowsInARow()
    {
        BroadcastGroupKey key = new("baseball-2026-08-24");
        LibraryRecordingSummary first = Row(key);
        LibraryRecordingSummary second = Row(key);
        LibraryRecordingSummary third = Row(key);

        LibraryRecordingBlock block = Assert.Single(LibraryRecordingBlock.Folded([first, second, third]));

        Assert.Equal(key, block.Key);
        Assert.Equal([first, second, third], block.Segments);
        Assert.Equal(first, block.First);
    }

    [Fact]
    public void ABlockKeepsThePlaceItsFirstSegmentHeldEvenWhenTheRestComeLater()
    {
        BroadcastGroupKey key = new("baseball-2026-08-24");
        LibraryRecordingSummary opened = Row(key);
        LibraryRecordingSummary between = Row();
        LibraryRecordingSummary closed = Row(key);

        IReadOnlyList<LibraryRecordingBlock> blocks = LibraryRecordingBlock.Folded([opened, between, closed]);

        Assert.Equal(2, blocks.Count);
        Assert.Equal([opened, closed], blocks[0].Segments);
        Assert.Equal([between], blocks[1].Segments);
    }

    [Fact]
    public void TwoBroadcastsNeverFoldIntoEachOther()
    {
        LibraryRecordingSummary here = Row(new BroadcastGroupKey("baseball"));
        LibraryRecordingSummary there = Row(new BroadcastGroupKey("marathon"));

        IReadOnlyList<LibraryRecordingBlock> blocks = LibraryRecordingBlock.Folded([here, there]);

        Assert.Equal(2, blocks.Count);
        Assert.Equal([here], blocks[0].Segments);
        Assert.Equal([there], blocks[1].Segments);
    }

    [Fact]
    public void FoldingLeavesEveryRowStandingOnItsOwnSoEachOneIsStillDeletedByItself()
    {
        BroadcastGroupKey key = new("baseball-2026-08-24");
        LibraryRecordingSummary[] rows = [Row(key), Row(key), Row()];

        Assert.Equal(
            [.. rows.Select(row => row.Id)],
            LibraryRecordingBlock.Folded(rows).SelectMany(block => block.Segments).Select(row => row.Id));
    }

    [Fact]
    public void AnEmptyPageFoldsIntoNoBlocksRatherThanOneEmptyOne()
        => Assert.Empty(LibraryRecordingBlock.Folded([]));

    private static LibraryRecordingSummary Row(BroadcastGroupKey? key = null)
        => LibraryRecordingSummary.Of(LibraryFactory.Complete(
            1_000_000,
            key,
            key is null ? BroadcastGroupRole.Standalone : BroadcastGroupRole.RelaySegment));
}
