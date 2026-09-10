using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeShapesTests
{
    [Fact]
    public void APictureEncodedTheWayEveryBrowserDecodesIsOneThatCanBeHandedOverAsItIs()
        => Assert.True(EncodeShapes.EveryBrowserPlays(EncodeCodec.H264));

    [Fact]
    public void APictureEncodedTheNewerWayIsNotOneEveryBrowserDecodes()
        => Assert.False(EncodeShapes.EveryBrowserPlays(EncodeCodec.H265));

    [Fact]
    public void ACodecThisApplicationDoesNotHaveIsNotAskedWhetherABrowserPlaysIt()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EncodeShapes.EveryBrowserPlays((EncodeCodec)99));
}
