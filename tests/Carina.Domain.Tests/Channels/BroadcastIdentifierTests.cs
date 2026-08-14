using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Channels;

public sealed class BroadcastIdentifierTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void ANetworkIdOutsideSixteenBitsIsRefused(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NetworkId(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void AServiceIdOutsideSixteenBitsIsRefused(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ServiceId(value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void ATransportStreamIdOutsideSixteenBitsIsRefused(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TransportStreamId(value));
    }

    [Fact]
    public void IdentifiersOfDifferentKindsWithTheSameNumberAreNotEqual()
    {
        Assert.NotEqual<object>(new NetworkId(4), new ServiceId(4));
        Assert.NotEqual<object>(new ServiceId(4), new TransportStreamId(4));
    }

    [Fact]
    public void TheSameNumberIsTheSameIdentifier()
    {
        Assert.Equal(new NetworkId(4), new NetworkId(4));
        Assert.Equal(new NetworkId(4).GetHashCode(), new NetworkId(4).GetHashCode());
    }

    [Fact]
    public void AnEmptyCandidateChannelIdIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new CandidateChannelId(Guid.Empty));
    }

    [Fact]
    public void AFreshCandidateChannelIdIsUnique()
    {
        Assert.NotEqual(CandidateChannelId.New(), CandidateChannelId.New());
    }
}
