using Carina.Contracts;
using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveSupplyEndingTests
{
    [Theory]
    [InlineData(LiveSupplyEnd.LetGo)]
    [InlineData(LiveSupplyEnd.TakenForARecording)]
    [InlineData(LiveSupplyEnd.DriverDraining)]
    [InlineData(LiveSupplyEnd.WindowClosed)]
    [InlineData(LiveSupplyEnd.TunerFailed)]
    [InlineData(LiveSupplyEnd.StoppedByAnother)]
    [InlineData(LiveSupplyEnd.DriverLost)]
    public void AnEndingCarriesOneOfTheReasonsNamedAndItsNote(LiveSupplyEnd why)
    {
        LiveSupplyEnding ending = LiveSupplyEnding.Of(why, "  because of the test  ");

        Assert.Equal(why, ending.Why);
        Assert.Equal("because of the test", ending.Note);
    }

    [Fact]
    public void AReasonOffTheListIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LiveSupplyEnding.Of((LiveSupplyEnd)99, "nowhere"));
    }

    [Fact]
    public void ANoteIsKeptFreeOfPathsLikeEveryOtherNoteShownToAViewer()
    {
        LiveSupplyEnding ending = LiveSupplyEnding.Of(LiveSupplyEnd.TunerFailed, "the device /dev/dvb/adapter3/frontend0 went away");

        Assert.DoesNotContain("/dev/", ending.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void ALiveSessionIsNamedWithItsOwnPrefixAndFitsTheDriversIdShape()
    {
        SessionId first = LiveSessions.Fresh();
        SessionId second = LiveSessions.Fresh();

        Assert.StartsWith(LiveSessions.Prefix, first.Value, StringComparison.Ordinal);
        Assert.NotEqual(first, second);
        Assert.True(SessionId.TryParse(first.Value, out _));
        Assert.Equal("live-", LiveSessions.Prefix);
    }
}
