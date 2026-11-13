using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Unit.Programmes;

[Trait("Category", "Unit")]
public sealed class ProgrammeIdTests
{
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
