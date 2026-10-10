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
    [InlineData(LiveChannel.DataBroadcast, 0x30)]
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

    [Fact(DisplayName = "BR-BD-004: the data broadcast goes out on channel 0x30")]
    public void TheDataBroadcastGoesOutOnTheChannelSetAsideForIt()
    {
        Assert.Equal(0x30, (byte)LiveChannel.DataBroadcast);
        Assert.Contains(LiveChannel.DataBroadcast, LiveChannels.Carrying);
    }

    [Fact]
    public void EveryChannelCarriesSomething()
    {
        Assert.Equal(
            Enum.GetValues<LiveChannel>().Order().ToArray(),
            LiveChannels.Carrying.Order().ToArray());
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
    [InlineData(LiveChannel.CaptionHeader)]
    public void AHeaderIsKeptForWhoeverArrivesLateAndIsNeverThrownAway(LiveChannel channel)
    {
        Assert.Contains(channel, LiveChannels.Headers);
        Assert.DoesNotContain(channel, LiveChannels.Expendable);
    }

    [Theory]
    [InlineData(LiveChannel.Control)]
    [InlineData(LiveChannel.Caption)]
    [InlineData(LiveChannel.DataBroadcast)]
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

    [Fact(DisplayName = "BR-BD-004: what is kept for whoever arrives late is every header, the caption that is showing, and the data broadcast")]
    public void WhatIsKeptForWhoeverArrivesLateIsEveryHeaderTheCaptionAndTheDataBroadcast()
    {
        Assert.Equal(
            [LiveChannel.PictureHeader, LiveChannel.SoundHeader, LiveChannel.CaptionHeader, LiveChannel.Caption, LiveChannel.DataBroadcast],
            LiveChannels.Kept);
        Assert.All(LiveChannels.Headers, header => Assert.Contains(header, LiveChannels.Kept));
        Assert.DoesNotContain(LiveChannel.Picture, LiveChannels.Kept);
        Assert.DoesNotContain(LiveChannel.Sound, LiveChannels.Kept);
        Assert.DoesNotContain(LiveChannel.Control, LiveChannels.Kept);
    }

    [Fact]
    public void NothingKeptIsEverThrownAway()
    {
        Assert.All(LiveChannels.Kept, kept => Assert.DoesNotContain(kept, LiveChannels.Expendable));
    }

    [Fact(DisplayName = "BR-BD-004: a viewer whose backlog is full goes without the media and the data broadcast, and without nothing else")]
    public void AViewerWhoseBacklogIsFullGoesWithoutTheMediaAndTheDataBroadcast()
    {
        Assert.Equal(
            [LiveChannel.Picture, LiveChannel.Sound, LiveChannel.DataBroadcast],
            LiveChannels.CutWhenBehind);
        Assert.All(LiveChannels.Expendable, expendable => Assert.Contains(expendable, LiveChannels.CutWhenBehind));
    }

    [Fact]
    public void TheHeadersAreExactlyTheThreeThatOpenAChannel()
    {
        Assert.Equal([0x00, 0x10, 0x20], LiveChannels.Headers.Select(channel => (byte)channel).Order().ToArray());
    }
}
