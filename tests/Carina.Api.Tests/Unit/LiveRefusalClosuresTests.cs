using System.Net.WebSockets;

using Carina.Api.Live;
using Carina.Domain.Streaming;

namespace Carina.Api.Tests.Unit;

public sealed class LiveRefusalClosuresTests
{
    [Fact]
    public void EveryRefusalHasACloseStatusAndASentence()
    {
        foreach (LiveRefusal refusal in Enum.GetValues<LiveRefusal>())
        {
            Assert.NotEqual(WebSocketCloseStatus.NormalClosure, LiveRefusalClosures.Status(refusal));
            Assert.False(string.IsNullOrWhiteSpace(LiveRefusalClosures.Because(refusal)));
        }
    }

    [Fact]
    public void AChannelThatIsNotHeldIsTheViewersMistakeAndAFullMachineIsAPolicy()
    {
        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, LiveRefusalClosures.Status(LiveRefusal.NoSuchChannel));
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, LiveRefusalClosures.Status(LiveRefusal.NoTunerFree));
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, LiveRefusalClosures.Status(LiveRefusal.TooManyAlready));
        Assert.Equal(WebSocketCloseStatus.InternalServerError, LiveRefusalClosures.Status(LiveRefusal.DriverUnavailable));
    }

    [Fact]
    public void ASentenceFitsInsideACloseFrame()
    {
        foreach (LiveRefusal refusal in Enum.GetValues<LiveRefusal>())
        {
            Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(LiveRefusalClosures.Because(refusal)), 1, 123);
        }
    }

    [Fact]
    public void AReasonOffTheListHasNeither()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveRefusalClosures.Status((LiveRefusal)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveRefusalClosures.Because((LiveRefusal)99));
    }
}
