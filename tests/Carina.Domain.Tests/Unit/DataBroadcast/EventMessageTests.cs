using Carina.Domain.DataBroadcast;

namespace Carina.Domain.Tests.Unit.DataBroadcast;

[Trait("Category", "Unit")]
public sealed class EventMessageTests
{
    [Theory]
    [InlineData(0x1000, 0, 0)]
    [InlineData(0, 0x1_0000, 0)]
    [InlineData(0, 0, 0x100)]
    public void AnEventMessageOutsideItsFieldsIsRefused(int group, int id, int messageType)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventMessage(group, id, messageType, EventTiming.Immediate, 0, ReadOnlyMemory<byte>.Empty));
    }

    [Fact(DisplayName = "BR-BD-003: an event message is either immediate or timed by the programme's clock")]
    public void AnEventMessageIsImmediateOrTimedByTheProgramme()
    {
        Assert.True(new EventMessage(1, 2, 3, EventTiming.Immediate, 0, ReadOnlyMemory<byte>.Empty).IsImmediate);
        Assert.False(new EventMessage(1, 2, 3, EventTiming.Npt, 0, ReadOnlyMemory<byte>.Empty).IsImmediate);
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventMessage(1, 2, 3, (EventTiming)0, 0, ReadOnlyMemory<byte>.Empty));
    }

    [Fact(DisplayName = "BR-BD-004: an event message names the stream it came on in one byte, takes that byte, and keeps it when moved")]
    public void AnEventMessageNamesItsStreamInOneByte()
    {
        EventMessage tagged = new(1, 2, 3, EventTiming.Immediate, 100, new byte[] { 0x01 }, componentTag: 0x6E);
        EventMessage untagged = new(1, 2, 3, EventTiming.Immediate, 100, new byte[] { 0x01 });

        Assert.Equal((0x6E, (int?)null), (tagged.ComponentTag, untagged.ComponentTag));
        Assert.Equal(untagged.Bytes + 1, tagged.Bytes);
        Assert.Equal((150L, 0x6E), (tagged.Shifted(50).FiresAt, tagged.Shifted(50).ComponentTag));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventMessage(1, 2, 3, EventTiming.Immediate, 0, ReadOnlyMemory<byte>.Empty, componentTag: 0x100));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EventMessage(1, 2, 3, EventTiming.Immediate, 0, ReadOnlyMemory<byte>.Empty, componentTag: -1));
    }
}
