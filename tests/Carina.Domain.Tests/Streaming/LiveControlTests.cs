using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveControlTests
{
    [Fact]
    public void ControlRidesTheChannelSetAsideForItAndCarriesNoClock()
    {
        LiveFrame frame = LiveControls.Frame(LiveControl.Ping);

        Assert.Equal(LiveChannel.Control, frame.Channel);
        Assert.Equal(LivePts.Start, frame.Pts);
    }

    [Fact]
    public void AControlMessageIsOneByteSayingWhichOneItIs()
    {
        Assert.Equal([0x40, 0, 0, 0, 0, 0, 0, 0, 0, 0x01], LiveControls.Frame(LiveControl.Ping).ToArray());
    }

    [Theory]
    [InlineData(LiveControl.Ping, 0x01)]
    [InlineData(LiveControl.Pong, 0x02)]
    [InlineData(LiveControl.Leaving, 0x03)]
    public void EachControlMessageKeepsItsNumber(LiveControl said, byte number)
    {
        Assert.Equal(number, (byte)said);
    }

    [Fact]
    public void TheServerAsksAndTheViewerAnswersAndNeitherSpeaksTheOtherHalf()
    {
        Assert.Equal([LiveControl.Ping], LiveControls.FromTheServer);
        Assert.Equal([LiveControl.Pong, LiveControl.Leaving], LiveControls.FromTheViewer);
        Assert.Empty(LiveControls.FromTheServer.Intersect(LiveControls.FromTheViewer));
    }

    [Fact]
    public void EveryControlMessageIsSaidByOneSideOrTheOther()
    {
        Assert.Equal(
            Enum.GetValues<LiveControl>().Order().ToArray(),
            LiveControls.FromTheServer.Concat(LiveControls.FromTheViewer).Order().ToArray());
    }

    [Theory]
    [InlineData(LiveControl.Pong)]
    [InlineData(LiveControl.Leaving)]
    public void WhatTheViewerIsAllowedToSayIsReadBack(LiveControl said)
    {
        Assert.Equal(said, LiveControls.SaidByAViewer(LiveControls.Frame(said).Payload.Span));
    }

    [Fact]
    public void AViewerRepeatingWhatOnlyTheServerSaysIsNotUnderstood()
    {
        Assert.Null(LiveControls.SaidByAViewer(LiveControls.Frame(LiveControl.Ping).Payload.Span));
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0x04)]
    [InlineData(0x40)]
    [InlineData(0xff)]
    public void ANumberNoControlMessageHasIsNotUnderstood(byte number)
    {
        Assert.Null(LiveControls.SaidByAViewer([number]));
    }

    [Fact]
    public void SayingNothingIsNotUnderstood()
    {
        Assert.Null(LiveControls.SaidByAViewer([]));
    }

    [Fact]
    public void SayingMoreThanOneThingAtOnceIsNotUnderstood()
    {
        Assert.Null(LiveControls.SaidByAViewer([0x02, 0x02]));
    }

    [Fact]
    public void AControlMessageCarriesNoWordsForTheServerToActOn()
    {
        Assert.Equal(1, LiveControls.Frame(LiveControl.Ping).Payload.Length);
        Assert.Null(LiveControls.SaidByAViewer("leave"u8));
    }
}
