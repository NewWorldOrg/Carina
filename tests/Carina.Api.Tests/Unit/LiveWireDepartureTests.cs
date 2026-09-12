using System.Net.WebSockets;

using Carina.Api.Live;
using Carina.Api.Tests.FeatureTest;
using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;
using Carina.TestSupport;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Api.Tests.Unit;

public sealed class LiveWireDepartureTests
{
    private static readonly DateTimeOffset Opened = new(2026, 9, 13, 4, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ASocketThatIsAlreadyGoneWhenItIsListenedToEndsAsTheViewerLeaving()
    {
        var socket = new ScriptedWebSocket { ReceiveThrows = new ObjectDisposedException(nameof(WebSocket)) };
        LiveDepartureLedger ledger = Ledger();

        await LiveWire.Invoke(Asking(socket), new SeatingAt(new HeldLiveSource()), ledger, Impatient(), Running, Clock);

        Assert.Equal(1L, Counted(ledger, LiveDeparture.ViewerLeft).Times);
    }

    [Fact]
    public async Task AWireWhoseCarryingThrewIsStillWrittenDownAsHavingEnded()
    {
        var socket = new ScriptedWebSocket { ReceiveThrows = new NotSupportedException("nothing here knows this one") };
        LiveDepartureLedger ledger = Ledger();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => LiveWire.Invoke(Asking(socket), new SeatingAt(new HeldLiveSource()), ledger, Impatient(), Running, Clock));

        Assert.Equal(1L, Counted(ledger, LiveDeparture.SourceBroke).Times);
        Assert.Equal(1L, ledger.Read().Counted.Sum(counted => counted.Times));
    }

    [Fact]
    public async Task HowLongAWireLastedIsWrittenDownEvenWhenCarryingItThrew()
    {
        var socket = new ScriptedWebSocket { ReceiveThrows = new NotSupportedException("nothing here knows this one") };
        HandTurnedClock clock = new(Opened);
        LiveDepartureLedger ledger = new(clock, NullLogger<LiveDepartureLedger>.Instance);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => LiveWire.Invoke(Asking(socket), new SeatingAt(new HeldLiveSource()), ledger, Impatient(), Running, clock));

        Assert.NotNull(Counted(ledger, LiveDeparture.SourceBroke).Longest);
    }

    private static LiveDepartureLedger Ledger()
        => new(new HandTurnedClock(Opened), NullLogger<LiveDepartureLedger>.Instance);

    private static LiveDepartureCount Counted(LiveDepartureLedger ledger, LiveDeparture departure)
        => ledger.Read().Counted.Single(counted => counted.Departure == departure);

    private static LiveWireSettings Impatient()
        => new() { BetweenPings = TimeSpan.FromSeconds(30), WritePatience = TimeSpan.FromSeconds(30) };

    private static TimeProvider Clock => new HandTurnedClock(Opened);

    private static IHostApplicationLifetime Running => new NotStopping();

    private static HttpContext Asking(WebSocket socket)
    {
        DefaultHttpContext context = new();

        context.Request.Path = LiveWire.Path;
        context.Request.QueryString = new QueryString("?network=32736&service=1024&profile=720p30");
        context.Features.Set<IHttpWebSocketFeature>(new Accepting(socket));

        return context;
    }

    private sealed class Accepting(WebSocket socket) : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;

        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => Task.FromResult(socket);
    }

    private sealed class NotStopping : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
