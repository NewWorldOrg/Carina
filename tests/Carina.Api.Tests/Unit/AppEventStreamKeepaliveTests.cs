using System.Text;

using Carina.Api.Events;
using Carina.Contracts;
using Carina.Infrastructure.Events;

using Microsoft.AspNetCore.Http;

namespace Carina.Api.Tests.Unit;

internal sealed class Sink : Stream
{
    private readonly Lock gate = new();

    private readonly List<byte> written = [];

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => Text().Length;

    public override long Position
    {
        get => Length;
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        lock (gate)
        {
            written.AddRange(buffer.AsSpan(offset, count));
        }
    }

    public string Text()
    {
        lock (gate)
        {
            return Encoding.UTF8.GetString([.. written]);
        }
    }
}

public sealed class AppEventStreamKeepaliveTests
{
    private static readonly TimeSpan Quickly = TimeSpan.FromMilliseconds(20);

    private static readonly TimeSpan LongEnough = TimeSpan.FromSeconds(5);

    [Fact]
    public void TheKeepaliveIsACommentSoAnEventSourceDispatchesNothingForIt()
    {
        Assert.StartsWith(":", AppEventStream.Keepalive, StringComparison.Ordinal);
        Assert.EndsWith("\n\n", AppEventStream.Keepalive, StringComparison.Ordinal);
        Assert.DoesNotContain("event:", AppEventStream.Keepalive, StringComparison.Ordinal);
        Assert.DoesNotContain("data", AppEventStream.Keepalive, StringComparison.Ordinal);
    }

    [Fact]
    public void TheKeepaliveGoesOutOftenEnoughToOutlastAnIdleProxyInFront()
    {
        Assert.True(AppEventStream.BetweenKeepalives < TimeSpan.FromSeconds(30));
        Assert.True(AppEventStream.BetweenKeepalives > TimeSpan.Zero);
    }

    [Fact]
    public async Task AStreamThatCarriesNoSignalStillWritesSoThatWhateverIsInFrontSeesTraffic()
    {
        var hub = new AppEventHub();
        var sink = new Sink();
        HttpContext context = Streaming(sink, out CancellationTokenSource stop);

        Task carrying = AppEventStream.Invoke(context, hub, LongEnough, Quickly);

        await Until(() => sink.Text().Contains(AppEventStream.Keepalive, StringComparison.Ordinal));

        await stop.CancelAsync();
        await carrying;
        stop.Dispose();

        Assert.Contains(AppEventStream.Keepalive, sink.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASignalStillArrivesOnAStreamThatHasBeenKeepingItselfAlive()
    {
        var hub = new AppEventHub();
        var sink = new Sink();
        HttpContext context = Streaming(sink, out CancellationTokenSource stop);

        Task carrying = AppEventStream.Invoke(context, hub, LongEnough, Quickly);

        await Until(() => sink.Text().Contains(AppEventStream.Keepalive, StringComparison.Ordinal));

        hub.Signal(AppEventName.Tuners);

        await Until(() => sink.Text().Contains("event: tuners", StringComparison.Ordinal));

        await stop.CancelAsync();
        await carrying;
        stop.Dispose();
    }

    private static HttpContext Streaming(Stream body, out CancellationTokenSource stop)
    {
        stop = new CancellationTokenSource();

        var context = new DefaultHttpContext();
        context.Response.Body = body;
        context.RequestAborted = stop.Token;

        return context;
    }

    private static async Task Until(Func<bool> settled)
    {
        for (int tries = 0; tries < 200; tries++)
        {
            if (settled())
            {
                return;
            }

            await Task.Delay(25);
        }

        Assert.Fail("The stream never wrote what was expected of it.");
    }
}
