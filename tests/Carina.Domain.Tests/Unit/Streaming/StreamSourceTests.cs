using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Unit.Streaming;

[Trait("Category", "Unit")]
public sealed class StreamSourceTests
{
    [Fact]
    public void ASourceIsTheValueItWasGiven()
    {
        Assert.Equal("/srv/recordings/k-1.ts", new StreamSource("/srv/recordings/k-1.ts").Value);
        Assert.Equal(new StreamSource("/srv/recordings/k-1.ts"), new StreamSource("/srv/recordings/k-1.ts"));
    }
}
