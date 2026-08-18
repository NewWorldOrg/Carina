using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class ProgrammeIdentifierTests
{
    [Theory]
    [InlineData(EventId.MinValue)]
    [InlineData(EventId.MaxValue)]
    [InlineData(30000)]
    public void AnEventIdTheBroadcastCanUseIsAccepted(int value)
        => Assert.Equal(value, new EventId(value).Value);

    [Theory]
    [InlineData(0x0000)]
    [InlineData(0xFFFF)]
    [InlineData(-1)]
    [InlineData(0x10000)]
    public void AnEventIdNoBroadcastNamesAProgrammeWithIsRefused(int value)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new EventId(value));

    [Fact]
    public void TwoProgrammeIdsNamingTheSameProgrammeAreTheSame()
    {
        Assert.Equal(Id(32739, 1049, 47289), Id(32739, 1049, 47289));
        Assert.Equal(Id(32739, 1049, 47289).GetHashCode(), Id(32739, 1049, 47289).GetHashCode());
    }

    [Theory]
    [InlineData(1, 1049, 47289)]
    [InlineData(32739, 1, 47289)]
    [InlineData(32739, 1049, 1)]
    public void AProgrammeIdDifferingInAnyPartIsAnotherProgramme(int network, int service, int carried)
        => Assert.NotEqual(Id(32739, 1049, 47289), Id(network, service, carried));

    [Fact]
    public void AProgrammeIdReadsBackAsTheThreeNumbersItIsMadeOf()
        => Assert.Equal("32739-1049-47289", Id(32739, 1049, 47289).ToString());

    private static ProgrammeId Id(int network, int service, int carried)
        => new(new NetworkId(network), new ServiceId(service), new EventId(carried));
}
