using Carina.BroadcastTestSupport;
using Carina.Infrastructure.Scanning;

namespace Carina.Infrastructure.Tests.Scanning;

public sealed class TableHarvestTests
{
    [Fact]
    public void TablesArrivingInPiecesThatAreNotWholePacketsAreStillRead()
    {
        var harvest = new TableHarvest();
        byte[] stream = SyntheticStream.Carrying(50002, new SyntheticService(50101, "Carina One")).ToBytes();

        for (int at = 0; at < stream.Length; at += 100)
        {
            harvest.Push(stream.AsSpan(at, Math.Min(100, stream.Length - at)));
        }

        Assert.True(harvest.IsComplete);
        Assert.Equal(0, harvest.UnreadablePackets);
        Assert.Equal(stream.Length, harvest.Bytes);
    }

    [Fact]
    public void TablesArrivingAsWholePacketsAreReadAsBefore()
    {
        var harvest = new TableHarvest();

        harvest.Push(SyntheticStream.Carrying(50002, new SyntheticService(50101, "Carina One")).ToBytes());

        Assert.True(harvest.IsComplete);
        Assert.Equal(0, harvest.UnreadablePackets);
    }
}
