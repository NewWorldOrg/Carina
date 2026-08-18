using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class RescanHintTests
{
    private static readonly NetworkId Network = new(4);
    private static readonly TransportStreamId Stream = new(32_736);

    [Fact]
    public void AStreamCarryingWhatWeExpectSuggestsNothing()
        => Assert.Empty(RescanHints.Between(Network, Stream, [new ServiceId(1), new ServiceId(2)], [new ServiceId(2), new ServiceId(1)]));

    [Fact]
    public void AServiceTheCatalogueHasNeverSeenIsWorthRescanningFor()
    {
        RescanHint only = Assert.Single(
            RescanHints.Between(Network, Stream, [new ServiceId(1), new ServiceId(9)], [new ServiceId(1)]));

        Assert.Equal(RescanReason.ServicesAppeared, only.Reason);
        Assert.Equal([9], only.Services.Select(service => service.Value));
    }

    [Fact]
    public void AServiceTheStreamNoLongerDeclaresIsWorthRescanningFor()
    {
        RescanHint only = Assert.Single(
            RescanHints.Between(Network, Stream, [new ServiceId(1)], [new ServiceId(1), new ServiceId(9)]));

        Assert.Equal(RescanReason.ServicesVanished, only.Reason);
        Assert.Equal([9], only.Services.Select(service => service.Value));
    }

    [Fact]
    public void AStreamThatBothGainedAndLostServicesSaysSoSeparately()
    {
        IReadOnlyList<RescanHint> hints = RescanHints.Between(
            Network,
            Stream,
            [new ServiceId(1), new ServiceId(9)],
            [new ServiceId(1), new ServiceId(7)]);

        Assert.Equal(
            [RescanReason.ServicesAppeared, RescanReason.ServicesVanished],
            hints.Select(hint => hint.Reason));
        Assert.Equal([9], hints[0].Services.Select(service => service.Value));
        Assert.Equal([7], hints[1].Services.Select(service => service.Value));
    }

    [Fact]
    public void AStreamWeHoldNothingForIsEntirelyNew()
    {
        RescanHint only = Assert.Single(
            RescanHints.Between(Network, Stream, [new ServiceId(1), new ServiceId(1)], []));

        Assert.Equal(RescanReason.ServicesAppeared, only.Reason);
        Assert.Equal([1], only.Services.Select(service => service.Value));
    }
}
