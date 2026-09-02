using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveChannelTests
{
    [Theory]
    [InlineData(LiveChannel.PictureHeader, 0x00)]
    [InlineData(LiveChannel.Picture, 0x01)]
    [InlineData(LiveChannel.SoundHeader, 0x10)]
    [InlineData(LiveChannel.Sound, 0x11)]
    [InlineData(LiveChannel.CaptionHeader, 0x20)]
    [InlineData(LiveChannel.Caption, 0x21)]
    [InlineData(LiveChannel.ServiceInformation, 0x30)]
    [InlineData(LiveChannel.Control, 0x40)]
    public void AChannelKeepsTheNumberTheWireWasSpecifiedWith(LiveChannel channel, byte number)
    {
        Assert.Equal(number, (byte)channel);
    }

    [Fact]
    public void TheNumbersAreTheEightThatWereSetAsideAndNoOthers()
    {
        Assert.Equal(
            [0x00, 0x01, 0x10, 0x11, 0x20, 0x21, 0x30, 0x40],
            Enum.GetValues<LiveChannel>().Select(channel => (byte)channel).Order().ToArray());
    }

    [Fact]
    public void NoTwoChannelsShareANumber()
    {
        Assert.Equal(
            Enum.GetValues<LiveChannel>().Length,
            Enum.GetValues<LiveChannel>().Select(channel => (byte)channel).Distinct().Count());
    }

    [Theory]
    [InlineData(LiveChannel.CaptionHeader)]
    [InlineData(LiveChannel.Caption)]
    [InlineData(LiveChannel.ServiceInformation)]
    public void AChannelSetAsideForLaterCarriesNothingYet(LiveChannel channel)
    {
        Assert.Contains(channel, LiveChannels.SetAsideForLater);
        Assert.DoesNotContain(channel, LiveChannels.Carrying);
    }

    [Theory]
    [InlineData(LiveChannel.PictureHeader)]
    [InlineData(LiveChannel.Picture)]
    [InlineData(LiveChannel.SoundHeader)]
    [InlineData(LiveChannel.Sound)]
    [InlineData(LiveChannel.Control)]
    public void AChannelInUseIsNotOneThatWasSetAside(LiveChannel channel)
    {
        Assert.Contains(channel, LiveChannels.Carrying);
        Assert.DoesNotContain(channel, LiveChannels.SetAsideForLater);
    }

    [Fact]
    public void EveryChannelIsEitherCarryingSomethingOrSetAsideForLater()
    {
        Assert.Equal(
            Enum.GetValues<LiveChannel>().Order().ToArray(),
            LiveChannels.Carrying.Concat(LiveChannels.SetAsideForLater).Order().ToArray());
    }

    [Theory]
    [InlineData(0x02)]
    [InlineData(0x0f)]
    [InlineData(0x12)]
    [InlineData(0x31)]
    [InlineData(0x41)]
    [InlineData(0xff)]
    public void ANumberNobodySetAsideIsNotAChannel(byte number)
    {
        Assert.False(Enum.IsDefined((LiveChannel)number));
    }

    [Theory]
    [InlineData(LiveChannel.Picture)]
    [InlineData(LiveChannel.Sound)]
    public void AMediaChannelIsTheOnlyKindABacklogMayThrowAway(LiveChannel channel)
    {
        Assert.Contains(channel, LiveChannels.Expendable);
        Assert.DoesNotContain(channel, LiveChannels.Headers);
    }

    [Theory]
    [InlineData(LiveChannel.PictureHeader)]
    [InlineData(LiveChannel.SoundHeader)]
    public void AHeaderIsKeptForWhoeverArrivesLateAndIsNeverThrownAway(LiveChannel channel)
    {
        Assert.Contains(channel, LiveChannels.Headers);
        Assert.DoesNotContain(channel, LiveChannels.Expendable);
    }

    [Theory]
    [InlineData(LiveChannel.Control)]
    [InlineData(LiveChannel.CaptionHeader)]
    [InlineData(LiveChannel.Caption)]
    [InlineData(LiveChannel.ServiceInformation)]
    public void WhatIsNeitherAHeaderNorMediaIsNeverThrownAwayEither(LiveChannel channel)
    {
        Assert.DoesNotContain(channel, LiveChannels.Expendable);
        Assert.DoesNotContain(channel, LiveChannels.Headers);
    }

    [Fact]
    public void TheExpendableChannelsAreExactlyTheTwoMediaChannels()
    {
        Assert.Equal([0x01, 0x11], LiveChannels.Expendable.Select(channel => (byte)channel).Order().ToArray());
    }

    [Fact]
    public void TheHeadersAreExactlyTheTwoThatOpenAMediaChannel()
    {
        Assert.Equal([0x00, 0x10], LiveChannels.Headers.Select(channel => (byte)channel).Order().ToArray());
    }
}
