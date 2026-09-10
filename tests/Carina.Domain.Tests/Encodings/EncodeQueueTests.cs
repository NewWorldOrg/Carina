using Carina.Domain.Encodings;

namespace Carina.Domain.Tests.Encodings;

public sealed class EncodeQueueTests
{
    [Fact(DisplayName = "A job bound for the card waits while anything is being transcoded for someone watching")]
    public void AJobBoundForTheCardWaitsWhileSomeoneIsWatching()
    {
        Assert.True(EncodeQueues.YieldsToAViewer(EncodeEncoder.Vaapi, cardIsUsable: true, watching: 1));
        Assert.True(EncodeQueues.YieldsToAViewer(EncodeEncoder.Vaapi, cardIsUsable: true, watching: 4));
    }

    [Fact(DisplayName = "A job bound for the card starts as usual when nobody is watching")]
    public void AJobBoundForTheCardStartsWhenNobodyIsWatching()
        => Assert.False(EncodeQueues.YieldsToAViewer(EncodeEncoder.Vaapi, cardIsUsable: true, watching: 0));

    [Fact(DisplayName = "A job bound for the processor starts even while someone is watching, because it takes no card")]
    public void AJobBoundForTheProcessorStartsEvenWhileSomeoneIsWatching()
        => Assert.False(EncodeQueues.YieldsToAViewer(EncodeEncoder.Software, cardIsUsable: true, watching: 3));

    [Fact(DisplayName = "A job asking for a card this machine cannot use starts, because it will swerve to the processor")]
    public void AJobAskingForACardThisMachineCannotUseStarts()
        => Assert.False(EncodeQueues.YieldsToAViewer(EncodeEncoder.Vaapi, cardIsUsable: false, watching: 3));

    [Fact]
    public void AnEncoderOutsideTheTwoOnOfferIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => EncodeQueues.YieldsToAViewer((EncodeEncoder)7, cardIsUsable: true, watching: 0));

    [Fact]
    public void FewerThanNoViewersIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => EncodeQueues.YieldsToAViewer(EncodeEncoder.Vaapi, cardIsUsable: true, watching: -1));
}
