using Carina.Domain.Channels;

namespace Carina.Domain.Tests.Unit.Channels;

[Trait("Category", "Unit")]
public sealed class CandidateChannelIdTests
{
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
