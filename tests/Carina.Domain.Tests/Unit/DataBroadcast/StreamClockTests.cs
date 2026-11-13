using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.Unit.DataBroadcast;

[Trait("Category", "Unit")]
public sealed class StreamClockTests
{
    [Fact(DisplayName = "BR-BD-005: every time is told on the 90 kHz clock of the stream")]
    public void EveryTimeIsToldOnTheNinetyKilohertzClockOfTheStream()
    {
        ModuleVersion version = new(0x40, 0, 0, 90_000, 135_000, [Carousels.Resource("a")]);
        EventMessage message = new(1, 2, 3, EventTiming.Npt, 27L * 60 * 60 * 90_000, ReadOnlyMemory<byte>.Empty);

        Assert.Equal(90_000, StreamClock.Hertz);
        Assert.Equal(TimeSpan.FromSeconds(1), version.FirstSeenAt);
        Assert.Equal(TimeSpan.FromSeconds(1.5), version.LastSeenAt);
        Assert.Equal(TimeSpan.FromHours(27), message.At);
    }
}
