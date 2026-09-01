using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveFrameTests
{
    private static readonly byte[] Payload = [0xde, 0xad, 0xbe, 0xef];

    [Fact]
    public void AHeaderIsOneByteOfChannelAndEightOfClock()
    {
        Assert.Equal(9, LiveFrame.HeaderLength);
    }

    [Fact]
    public void TheChannelIsTheFirstByte()
    {
        Assert.Equal(0x11, new LiveFrame(LiveChannel.Sound, LivePts.Start, Payload).ToArray()[0]);
    }

    [Fact]
    public void TheClockIsTheNextEightBytesMostSignificantFirst()
    {
        byte[] written = new LiveFrame(LiveChannel.Picture, LivePts.Of(0x0102030405060708UL), Payload).ToArray();

        Assert.Equal([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08], written[1..9]);
    }

    [Fact]
    public void ThePayloadFollowsTheHeaderUntouched()
    {
        Assert.Equal(Payload, new LiveFrame(LiveChannel.Picture, LivePts.Start, Payload).ToArray()[9..]);
    }

    [Fact]
    public void AFrameIsAsLongAsItsHeaderAndItsPayloadTogether()
    {
        Assert.Equal(9 + Payload.Length, new LiveFrame(LiveChannel.Picture, LivePts.Start, Payload).Length);
    }

    [Fact]
    public void AFrameCarryingNothingIsJustItsHeader()
    {
        Assert.Equal(
            [0x40, 0, 0, 0, 0, 0, 0, 0, 0],
            new LiveFrame(LiveChannel.Control, LivePts.Start, ReadOnlyMemory<byte>.Empty).ToArray());
    }

    [Fact]
    public void TheStartOfTheClockIsEightZeroBytes()
    {
        Assert.Equal(
            [0, 0, 0, 0, 0, 0, 0, 0],
            new LiveFrame(LiveChannel.Picture, LivePts.Start, Payload).ToArray()[1..9]);
    }

    [Fact]
    public void TheFurthestReadingOfTheClockIsEightBytesOfOnes()
    {
        Assert.Equal(
            [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff],
            new LiveFrame(LiveChannel.Picture, LivePts.Furthest, Payload).ToArray()[1..9]);
    }

    [Fact]
    public void WhereTheBroadcastClockComesAroundIsCarriedWholeRatherThanCutToThirtyThreeBits()
    {
        Assert.Equal(
            [0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00],
            new LiveFrame(LiveChannel.Picture, LivePts.Of(LivePts.ComesAroundAt), Payload).ToArray()[1..9]);
        Assert.Equal(
            [0x00, 0x00, 0x00, 0x01, 0xff, 0xff, 0xff, 0xff],
            new LiveFrame(LiveChannel.Picture, LivePts.Of(LivePts.ComesAroundAt - 1UL), Payload).ToArray()[1..9]);
    }

    [Fact]
    public void AFrameIsNotWrittenIntoSomethingTooSmallToHoldIt()
    {
        Assert.Throws<ArgumentException>(() =>
            new LiveFrame(LiveChannel.Picture, LivePts.Start, Payload).WriteTo(new byte[12]));
    }

    [Fact]
    public void AChannelNobodySetAsideIsNotAFrame()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LiveFrame((LiveChannel)0x02, LivePts.Start, Payload));
    }

    [Theory]
    [InlineData(LiveChannel.PictureHeader)]
    [InlineData(LiveChannel.Picture)]
    [InlineData(LiveChannel.SoundHeader)]
    [InlineData(LiveChannel.Sound)]
    [InlineData(LiveChannel.CaptionHeader)]
    [InlineData(LiveChannel.Caption)]
    [InlineData(LiveChannel.ServiceInformation)]
    [InlineData(LiveChannel.Control)]
    public void EveryFrameSurvivesTheRoundTripThroughItsBytes(LiveChannel channel)
    {
        var frame = new LiveFrame(channel, LivePts.Of(123_456_789UL), Payload);

        LiveFraming read = LiveFrame.Read(frame.ToArray());

        Assert.Null(read.Fault);
        Assert.Equal(channel, read.Frame?.Channel);
        Assert.Equal(LivePts.Of(123_456_789UL), read.Frame?.Pts);
        Assert.Equal(Payload, read.Frame?.Payload.ToArray());
    }

    [Fact]
    public void AFrameOfNothingSurvivesTheRoundTripThroughItsBytes()
    {
        LiveFraming read = LiveFrame.Read(
            new LiveFrame(LiveChannel.Control, LivePts.Furthest, ReadOnlyMemory<byte>.Empty).ToArray());

        Assert.Null(read.Fault);
        Assert.Equal(LivePts.Furthest, read.Frame?.Pts);
        Assert.Empty(read.Frame?.Payload.ToArray() ?? [0x01]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(8)]
    public void SomethingShorterThanAHeaderIsNotAFrame(int length)
    {
        LiveFraming read = LiveFrame.Read(new byte[length]);

        Assert.Null(read.Frame);
        Assert.Equal(LiveFrameFault.ShorterThanAHeader, read.Fault);
    }

    [Theory]
    [InlineData(0x02)]
    [InlineData(0x31)]
    [InlineData(0xff)]
    public void SomethingOnAChannelNobodySetAsideIsNotAFrame(byte channel)
    {
        byte[] bytes = new byte[9];
        bytes[0] = channel;

        LiveFraming read = LiveFrame.Read(bytes);

        Assert.Null(read.Frame);
        Assert.Equal(LiveFrameFault.AChannelNobodySetAside, read.Fault);
    }
}
