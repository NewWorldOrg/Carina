using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingValueTests
{
    [Fact]
    public void ARecordingIdIsNotTheEmptyGuid()
        => Assert.Throws<ArgumentException>(() => new RecordingId(Guid.Empty));

    [Fact]
    public void ARecordingIdTravelsAsANameTheDriverWillAccept()
    {
        var id = new RecordingId(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"));

        Assert.Equal("6f9619ff8b86d011b42d00c04fc964ff", id.Wire);
    }

    [Theory]
    [InlineData("bulk")]
    [InlineData("archive-2")]
    [InlineData("cold_store.1")]
    public void AnOutputRootIsANameTheDriverDeclared(string name)
        => Assert.Equal(name, new OutputRoot(name).Value);

    [Theory]
    [InlineData("/mnt/recordings")]
    [InlineData("bulk/nested")]
    [InlineData("bulk\\nested")]
    [InlineData("..")]
    [InlineData("bulk..cold")]
    [InlineData("recordings on disk")]
    [InlineData("")]
    public void AnOutputRootIsNeverAPath(string name)
        => Assert.ThrowsAny<ArgumentException>(() => new OutputRoot(name));

    [Fact]
    public void AnOutputRootIsShortEnoughToCrossTheWire()
        => Assert.Throws<ArgumentException>(() => new OutputRoot(new string('a', OutputRoot.MaxLength + 1)));

    [Theory]
    [InlineData("a/b.m2ts")]
    [InlineData("a\\b.m2ts")]
    [InlineData("../escaped.m2ts")]
    [InlineData("held..out.m2ts")]
    [InlineData(".")]
    [InlineData(" leading.m2ts")]
    [InlineData("trailing.m2ts ")]
    [InlineData("")]
    public void AFileNameIsASingleNameWithNoWayOutOfItsRoom(string name)
        => Assert.ThrowsAny<ArgumentException>(() => new RecordingFileName(name));

    [Fact]
    public void AFileNameRefusesTheNulByte()
        => Assert.Throws<ArgumentException>(() => new RecordingFileName("recording\0.m2ts"));

    [Fact]
    public void AFileNameIsShortEnoughForAFileSystem()
        => Assert.Throws<ArgumentException>(
            () => new RecordingFileName(new string('a', RecordingFileName.MaxLength + 1)));

    [Fact]
    public void AFileNameBuiltForARecordingCarriesItsId()
    {
        RecordingId id = RecordingId.New();

        RecordingFileName name = RecordingFileName.For(id, ".m2ts");

        Assert.True(name.Names(id));
        Assert.False(name.Names(RecordingId.New()));
    }

    [Fact]
    public void CountersThatNobodyTookCarryNoNumberAtAll()
    {
        DropCounters unmeasured = DropCounters.Unmeasured;

        Assert.False(unmeasured.Measured);
        Assert.Null(unmeasured.Dropped);
        Assert.Null(unmeasured.Total);
    }

    [Fact]
    public void CountingToZeroIsNotTheSameAsNotCounting()
    {
        DropCounters counted = DropCounters.Counted(0, 1_000_000);

        Assert.True(counted.Measured);
        Assert.Equal(0, counted.Dropped);
        Assert.Equal(1_000_000, counted.Total);
        Assert.NotEqual(DropCounters.Unmeasured, counted);
    }

    [Fact]
    public void CountersCannotLoseMorePacketsThanPassed()
        => Assert.Throws<ArgumentOutOfRangeException>(() => DropCounters.Counted(11, 10));

    [Fact]
    public void CountersCannotLoseANegativeNumberOfPackets()
        => Assert.Throws<ArgumentOutOfRangeException>(() => DropCounters.Counted(-1, 10));

    [Fact]
    public void RehydratingUnmeasuredCountersWithANumberIsRefused()
    {
        Assert.Throws<ArgumentException>(() => DropCounters.Rehydrate(false, 0, 0));
        Assert.Throws<ArgumentException>(() => DropCounters.Rehydrate(true, null, 10));
        Assert.Equal(DropCounters.Unmeasured, DropCounters.Rehydrate(false, null, null));
        Assert.Equal(DropCounters.Counted(3, 10), DropCounters.Rehydrate(true, 3, 10));
    }
}
