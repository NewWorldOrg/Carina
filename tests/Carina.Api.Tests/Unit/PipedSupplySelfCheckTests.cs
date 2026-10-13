using Carina.Domain.Channels;
using Carina.Domain.Streaming;
using Carina.TestSupport;

namespace Carina.Api.Tests.Unit;

public sealed class PipedSupplySelfCheckTests
{
    private static readonly NetworkId Network = new(32736);

    private static readonly ServiceId Service = new(1024);

    [Fact(DisplayName = "the supply fed while the stream it handed out has been let go and no other is open yet writes nothing and does not fail")]
    public async Task FeedingAfterTheStreamWasLetGoWritesNothing()
    {
        PipedSupply supply = new();
        LiveSupplyStart first = await supply.OpenAsync(Network, Service, CancellationToken.None);
        await first.Stream!.DisposeAsync();

        bool fed = await supply.FeedAsync([1, 2, 3]);

        Assert.False(fed);
    }

    [Fact(DisplayName = "the supply is fed into the newest stream it handed out that is still open")]
    public async Task FeedingReachesTheNewestOpenStream()
    {
        PipedSupply supply = new();
        LiveSupplyStart first = await supply.OpenAsync(Network, Service, CancellationToken.None);
        await first.Stream!.DisposeAsync();
        LiveSupplyStart second = await supply.OpenAsync(Network, Service, CancellationToken.None);

        bool fed = await supply.FeedAsync([1, 2, 3]);
        byte[] heard = new byte[3];
        await second.Stream!.Bytes.ReadExactlyAsync(heard);

        Assert.True(fed);
        Assert.Equal(new byte[] { 1, 2, 3 }, heard);
    }

    [Fact(DisplayName = "the supply fed before it has handed out any stream writes nothing")]
    public async Task FeedingBeforeAnyStreamWritesNothing()
    {
        Assert.False(await new PipedSupply().FeedAsync([1]));
    }
}
