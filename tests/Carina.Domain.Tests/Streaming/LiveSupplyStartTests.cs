using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveSupplyStartTests
{
    [Fact]
    public void ASupplyThatOpenedIsFlowingAndHasNoRefusal()
    {
        LiveSupplyStart start = LiveSupplyStart.Opened(new Silence());

        Assert.True(start.Flowing);
        Assert.NotNull(start.Stream);
        Assert.Null(start.Refusal);
    }

    [Theory]
    [InlineData(LiveRefusal.NoSuchChannel)]
    [InlineData(LiveRefusal.NoTunerFree)]
    [InlineData(LiveRefusal.WouldNotTune)]
    [InlineData(LiveRefusal.DriverUnavailable)]
    public void ASupplyRefusesForAReasonATunerCanHave(LiveRefusal why)
    {
        LiveSupplyStart start = LiveSupplyStart.Refused(why, "held");

        Assert.False(start.Flowing);
        Assert.Equal(why, start.Refusal);
        Assert.Equal("held", start.Note);
    }

    [Theory]
    [InlineData(LiveRefusal.TooManyAlready)]
    [InlineData(LiveRefusal.TranscoderWouldNotStart)]
    public void ASupplyCannotRefuseForAReasonThatBelongsToTheTranscoder(LiveRefusal why)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveSupplyStart.Refused(why, "not mine to say"));
    }

    [Fact]
    public void TheTwoListsOfReasonsTogetherAreEveryReasonThereIsAndShareNone()
    {
        LiveRefusal[] all = [.. LiveRefusals.FromTheSupply, .. LiveRefusals.FromTheTranscoder];

        Assert.Equal(Enum.GetValues<LiveRefusal>().Order(), all.Order());
        Assert.Equal(all.Length, all.Distinct().Count());
    }

    private sealed class Silence : ILiveTransportStream
    {
        public Stream Bytes => Stream.Null;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
