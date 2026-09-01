using Carina.Api.Playback;
using Carina.Domain.Recordings;

namespace Carina.Api.Tests.Unit;

public sealed class ByteRangeTests
{
    private const long Size = 4_000;

    [Fact]
    public void AskingForNothingInParticularAsksForTheWholeFile()
    {
        ByteRange read = ByteRange.Read(null, Size);

        Assert.Equal(RangeAnswer.Whole, read.Answer);
        Assert.Equal(0, read.From);
        Assert.Equal(Size, read.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("items=0-1")]
    [InlineData("bytes=abc")]
    [InlineData("bytes=")]
    [InlineData("bytes=-")]
    [InlineData("bytes=1-x")]
    [InlineData("bytes=x-2")]
    [InlineData("bytes=+5-")]
    [InlineData("bytes= 5 5-")]
    [InlineData("bytes=100-50")]
    [InlineData("bytes=99999999999999999999-")]
    public void ARangeThatCannotBeReadIsIgnoredRatherThanRefused(string asked)
    {
        ByteRange read = ByteRange.Read(asked, Size);

        Assert.Equal(RangeAnswer.Whole, read.Answer);
        Assert.Equal(Size, read.Count);
    }

    [Theory]
    [InlineData("bytes=0-99,200-299")]
    [InlineData("bytes=0-99, 200-299")]
    [InlineData("bytes=-100,-200")]
    public void MoreThanOneRangeIsNotAcceptedAndTheWholeFileIsAnsweredInstead(string asked)
    {
        ByteRange read = ByteRange.Read(asked, Size);

        Assert.Equal(RangeAnswer.Whole, read.Answer);
        Assert.Equal(Size, read.Count);
    }

    [Fact]
    public void AskingFromTheStartOfTheFileAsksForAPartAndNotForTheWhole()
    {
        ByteRange read = ByteRange.Read("bytes=0-", Size);

        Assert.Equal(RangeAnswer.Part, read.Answer);
        Assert.Equal(0, read.From);
        Assert.Equal(Size - 1, read.Last);
        Assert.Equal(Size, read.Count);
    }

    [Fact]
    public void AskingFromAnOffsetRunsToTheEndOfTheFile()
    {
        ByteRange read = ByteRange.Read("bytes=100-", Size);

        Assert.Equal(RangeAnswer.Part, read.Answer);
        Assert.Equal(100, read.From);
        Assert.Equal(Size - 1, read.Last);
        Assert.Equal(Size - 100, read.Count);
    }

    [Fact]
    public void AskingForTheLastBytesIsCountedBackFromTheEndAndNotForwardFromTheStart()
    {
        ByteRange read = ByteRange.Read("bytes=-500", Size);

        Assert.Equal(RangeAnswer.Part, read.Answer);
        Assert.Equal(Size - 500, read.From);
        Assert.Equal(Size - 1, read.Last);
        Assert.Equal(500, read.Count);
    }

    [Fact]
    public void AskingForMoreLastBytesThanThereAreHandsBackTheWholeFileAsAPart()
    {
        ByteRange read = ByteRange.Read("bytes=-9000", Size);

        Assert.Equal(RangeAnswer.Part, read.Answer);
        Assert.Equal(0, read.From);
        Assert.Equal(Size, read.Count);
    }

    [Fact]
    public void AskingForTheLastNothingReachesPastTheFile()
    {
        Assert.Equal(RangeAnswer.OutOfReach, ByteRange.Read("bytes=-0", Size).Answer);
    }

    [Fact]
    public void AskingForBothEndsTakesWhatIsBetweenThemInclusive()
    {
        ByteRange read = ByteRange.Read("bytes=100-199", Size);

        Assert.Equal(RangeAnswer.Part, read.Answer);
        Assert.Equal(100, read.From);
        Assert.Equal(199, read.Last);
        Assert.Equal(100, read.Count);
    }

    [Fact]
    public void AskingForASingleByteIsOneByteAndNotNone()
    {
        ByteRange read = ByteRange.Read("bytes=0-0", Size);

        Assert.Equal(1, read.Count);
        Assert.Equal(0, read.Last);
    }

    [Fact]
    public void AnEndBeyondTheFileIsBroughtBackToTheLastByte()
    {
        ByteRange read = ByteRange.Read("bytes=100-999999", Size);

        Assert.Equal(RangeAnswer.Part, read.Answer);
        Assert.Equal(100, read.From);
        Assert.Equal(Size - 1, read.Last);
    }

    [Theory]
    [InlineData("bytes=999999-")]
    [InlineData("bytes=4000-")]
    [InlineData("bytes=4000-4100")]
    public void AStartBeyondTheFileReachesPastIt(string asked)
    {
        Assert.Equal(RangeAnswer.OutOfReach, ByteRange.Read(asked, Size).Answer);
    }

    [Theory]
    [InlineData("bytes=0-")]
    [InlineData("bytes=0-0")]
    [InlineData("bytes=-500")]
    public void EveryRangeOfAFileOfNoBytesReachesPastIt(string asked)
    {
        Assert.Equal(RangeAnswer.OutOfReach, ByteRange.Read(asked, 0).Answer);
    }

    [Fact]
    public void AFileOfNoBytesAskedForWithoutARangeIsAnEmptyWhole()
    {
        ByteRange read = ByteRange.Read(null, 0);

        Assert.Equal(RangeAnswer.Whole, read.Answer);
        Assert.Equal(0, read.Count);
    }

    [Fact]
    public void TheUnitIsReadWhicheverWayItIsSpelled()
    {
        Assert.Equal(RangeAnswer.Part, ByteRange.Read("BYTES=0-9", Size).Answer);
        Assert.Equal(10, ByteRange.Read("  bytes=0-9  ", Size).Count);
    }

    [Fact]
    public void AFileCannotHoldANegativeNumberOfBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ByteRange.Read("bytes=0-", -1));
    }

    [Theory]
    [InlineData("a.m2ts", "video/mp2t")]
    [InlineData("a.ts", "video/mp2t")]
    [InlineData("a.MTS", "video/mp2t")]
    [InlineData("a.mp4", "video/mp4")]
    [InlineData("a.m4v", "video/mp4")]
    [InlineData("a.bin", "application/octet-stream")]
    [InlineData("a", "application/octet-stream")]
    public void WhatIsHandedOverSaysWhatItIs(string fileName, string mediaType)
    {
        Assert.Equal(mediaType, PlaybackMediaType.Of(new RecordingFileName(fileName)));
    }
}
