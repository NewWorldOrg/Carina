using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class LiveStillTests
{
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0xff, 0xd9];

    private static readonly byte[] Png = [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00];

    [Fact]
    public void AStillRidesAChannelOfItsOwnStampedWithThePictureItWasMadeFrom()
    {
        LiveStill made = LiveStill.Of(LivePts.Of(180_000UL), Jpeg);

        Assert.NotNull(made.Frame);
        Assert.Equal(LiveChannel.Still, made.Frame.Channel);
        Assert.Equal(LivePts.Of(180_000UL), made.Frame.Pts);
        Assert.Equal(Jpeg, made.Frame.Payload.ToArray());
        Assert.Equal(string.Empty, made.Note);
    }

    [Fact]
    public void BytesThatDoNotOpenAsAJpegAreNotAStill()
    {
        Assert.Throws<ArgumentException>(() => LiveStill.Of(LivePts.Start, Png));
        Assert.Throws<ArgumentNullException>(() => LiveStill.Of(null!, Jpeg));
    }

    [Theory]
    [InlineData(new byte[] { 0xff, 0xd8, 0xff }, true)]
    [InlineData(new byte[] { 0xff, 0xd8, 0xff, 0xdb, 0x00 }, true)]
    [InlineData(new byte[] { 0xff, 0xd8 }, false)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4e, 0x47 }, false)]
    [InlineData(new byte[] { }, false)]
    public void AJpegIsKnownByTheMarkerItOpensWith(byte[] bytes, bool opensAsAJpeg)
    {
        Assert.Equal(opensAsAJpeg, LiveStill.IsJpeg(bytes));
    }

    [Fact]
    public void AStillThatWasNotMadeCarriesNoFrameAndSaysWhy()
    {
        LiveStill missing = LiveStill.Missing("the picture never arrived.");

        Assert.Null(missing.Frame);
        Assert.Equal("the picture never arrived.", missing.Note);
    }

    [Fact]
    public void AStillThatWasNotMadeAlwaysSaysWhy()
    {
        Assert.Throws<ArgumentException>(() => LiveStill.Missing(" "));
        Assert.Throws<ArgumentNullException>(() => LiveStill.Missing(null!));
    }
}
