using System.Text;

using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Unit;

public sealed class PlaybackTicketGateTests
{
    private const string TheContent = "the recording itself";

    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Subject Watcher = new("watcher");

    private static readonly PlaybackTarget Seven = PlaybackTarget.Recording("7");

    private static readonly PlaybackTarget Eight = PlaybackTarget.Recording("8");

    [Fact]
    public async Task ATicketForThisRecordingReachesItAndSaysWhoIsWatching()
    {
        PlaybackTicketStore tickets = Store(out _);
        IssuedPlaybackTicket issued = Issued(tickets, Watcher, Seven);
        Served served = await ServeAsync(tickets, Offering(issued.InTheClear), Seven);

        Assert.Equal(StatusCodes.Status200OK, served.Status);
        Assert.Equal(Watcher, served.Watcher);
        Assert.Equal($"{TheContent} for {Seven.Value}", served.Body);
    }

    [Fact]
    public async Task ARequestWithNoTicketIsRefusedAndTheRecordingIsNeverReached()
    {
        Served served = await ServeAsync(Store(out _), new DefaultHttpContext(), Seven);

        Assert.Equal(StatusCodes.Status403Forbidden, served.Status);
        Assert.Null(served.Watcher);
        Assert.DoesNotContain(TheContent, served.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStatusIsDecidedBeforeTheFirstByteLeaves()
    {
        Served refused = await ServeAsync(Store(out _), new DefaultHttpContext(), Seven);

        Assert.Equal(StatusCodes.Status403Forbidden, refused.StatusAtTheFirstByte);

        PlaybackTicketStore tickets = Store(out _);
        IssuedPlaybackTicket issued = Issued(tickets, Watcher, Seven);
        Served served = await ServeAsync(tickets, Offering(issued.InTheClear), Seven);

        Assert.Equal(StatusCodes.Status200OK, served.StatusAtTheFirstByte);
    }

    [Fact]
    public async Task ATicketAdmitsOneRequestSoAPlayerThatComesBackForARangeIsRefused()
    {
        PlaybackTicketStore tickets = Store(out _);
        IssuedPlaybackTicket issued = Issued(tickets, Watcher, Seven);

        Assert.Equal(StatusCodes.Status200OK, (await ServeAsync(tickets, Offering(issued.InTheClear), Seven)).Status);

        Served again = await ServeAsync(tickets, Offering(issued.InTheClear), Seven);

        Assert.Equal(StatusCodes.Status403Forbidden, again.Status);
        Assert.DoesNotContain(TheContent, again.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATicketForAnotherRecordingDoesNotOpenThisOne()
    {
        PlaybackTicketStore tickets = Store(out _);
        IssuedPlaybackTicket issued = Issued(tickets, Watcher, Eight);
        Served served = await ServeAsync(tickets, Offering(issued.InTheClear), Seven);

        Assert.Equal(StatusCodes.Status403Forbidden, served.Status);
        Assert.DoesNotContain(TheContent, served.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALapsedTicketIsRefused()
    {
        PlaybackTicketStore tickets = Store(out WoundClock clock);
        IssuedPlaybackTicket issued = Issued(tickets, Watcher, Seven);

        clock.Wind(PlaybackTicketPolicy.Default.Lifetime);

        Assert.Equal(
            StatusCodes.Status403Forbidden,
            (await ServeAsync(tickets, Offering(issued.InTheClear), Seven)).Status);
    }

    [Fact]
    public async Task NeitherAnswerMayBeCachedByAnythingInFront()
    {
        PlaybackTicketStore tickets = Store(out _);
        IssuedPlaybackTicket issued = Issued(tickets, Watcher, Seven);

        Served[] answers =
        [
            await ServeAsync(tickets, Offering(issued.InTheClear), Seven),
            await ServeAsync(tickets, new DefaultHttpContext(), Seven),
        ];

        Assert.All(answers, answer =>
        {
            Assert.Equal(PlaybackTicketGate.NeverCached, answer.Headers.CacheControl);
            Assert.Equal(HeaderNames.Authorization, answer.Headers.Vary);
        });
    }

    [Fact]
    public async Task TheRefusalNeverRepeatsTheTicketItWasOffered()
    {
        PlaybackTicketStore tickets = Store(out _);
        IssuedPlaybackTicket issued = Issued(tickets, Watcher, Eight);
        HttpContext context = Offering(issued.InTheClear);
        Served served = await ServeAsync(tickets, context, Seven);

        Assert.DoesNotContain(issued.InTheClear, served.Body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            context.Response.Headers,
            header => header.Value.Any(value =>
                value?.Contains(issued.InTheClear, StringComparison.Ordinal) is true));
    }

    [Fact]
    public async Task EveryRefusalReadsTheSameSoNobodyLearnsWhichThingWasWrong()
    {
        PlaybackTicketStore tickets = Store(out WoundClock clock);
        IssuedPlaybackTicket spent = Issued(tickets, Watcher, Seven);
        await ServeAsync(tickets, Offering(spent.InTheClear), Seven);
        IssuedPlaybackTicket forAnother = Issued(tickets, Watcher, Eight);
        IssuedPlaybackTicket lapsing = Issued(tickets, Watcher, Seven);

        List<Served> refusals =
        [
            await ServeAsync(tickets, new DefaultHttpContext(), Seven),
            await ServeAsync(tickets, Offering(Unguessable.Issue()), Seven),
            await ServeAsync(tickets, Offering(spent.InTheClear), Seven),
            await ServeAsync(tickets, Offering(forAnother.InTheClear), Seven),
            await ServeAsync(tickets, Offering("not-a-ticket"), Seven),
        ];

        clock.Wind(PlaybackTicketPolicy.Default.Lifetime);
        refusals.Add(await ServeAsync(tickets, Offering(lapsing.InTheClear), Seven));

        Assert.All(refusals, refusal => Assert.Equal(StatusCodes.Status403Forbidden, refusal.Status));
        Assert.Single(refusals.Select(refusal => refusal.Body).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ARefusalDoesNotAskAPlayerToPopUpALoginBox()
    {
        Served served = await ServeAsync(Store(out _), new DefaultHttpContext(), Seven);

        Assert.False(served.Headers.ContainsKey(HeaderNames.WWWAuthenticate));
    }

    private static HttpContext Offering(string ticket)
    {
        DefaultHttpContext context = new();
        context.Request.Headers[HeaderNames.Authorization] = $"Bearer {ticket}";

        return context;
    }

    private static IssuedPlaybackTicket Issued(
        PlaybackTicketStore store,
        Subject subject,
        PlaybackTarget target)
    {
        IssuedPlaybackTicket? issued = store.Issue(subject, target);

        Assert.NotNull(issued);

        return issued;
    }

    private static PlaybackGrantStore Grants()
        => new(new WoundClock(At), PlaybackGrantPolicy.Default);

    private static PlaybackTicketStore Store(out WoundClock clock)
    {
        clock = new WoundClock(At);

        return new PlaybackTicketStore(clock, PlaybackTicketPolicy.Default);
    }

    private static async Task<Served> ServeAsync(
        IPlaybackTicketStore tickets,
        HttpContext context,
        PlaybackTarget target)
    {
        WatchfulBody body = new(context);
        context.Response.Body = body;
        Subject? watcher = null;

        await new PlaybackTicketGate(tickets, Grants()).AdmitOnceAsync(
            context,
            target,
            async (admitted, opened) =>
            {
                watcher = admitted;

                await context.Response.WriteAsync($"{TheContent} for {opened.Value}");
            });

        return new Served(
            context.Response.StatusCode,
            body.StatusAtTheFirstByte,
            body.Written,
            watcher,
            context.Response.Headers);
    }

    private sealed record Served(
        int Status,
        int? StatusAtTheFirstByte,
        string Body,
        Subject? Watcher,
        IHeaderDictionary Headers);

    private sealed class WatchfulBody(HttpContext context) : Stream
    {
        private readonly MemoryStream written = new();

        public int? StatusAtTheFirstByte { get; private set; }

        public string Written => Encoding.UTF8.GetString(written.ToArray());

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => written.Length;

        public override long Position
        {
            get => written.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => written.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => Write(new ReadOnlySpan<byte>(buffer, offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            StatusAtTheFirstByte ??= context.Response.StatusCode;
            written.Write(buffer);
        }

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);

            return ValueTask.CompletedTask;
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(new ReadOnlySpan<byte>(buffer, offset, count));

            return Task.CompletedTask;
        }
    }
}
