
using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Unit.App.Auth;

[Trait("Category", "Unit")]
public sealed class PlaybackTicketGateTests
{
    private const string TheContent = "the recording itself";

    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Subject Watcher = new("watcher");

    private static readonly PlaybackTarget Seven = PlaybackTarget.Recording("7");

    private static readonly PlaybackTarget Eight = PlaybackTarget.Recording("8");

    private static readonly PlaybackTarget Tokyo = PlaybackTarget.LiveChannel("32736-1024");

    private static readonly PlaybackTarget Education = PlaybackTarget.LiveChannel("32737-1032");

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

    [Fact]
    public async Task APlayerThatComesBackForEveryRangeIsAdmittedOnTheGrantEnteringOpened()
    {
        Gate gate = Made(out _);
        string carrier = gate.Issue(Watcher, Seven);

        Assert.Equal(StatusCodes.Status200OK, await gate.EnterAsync(carrier, Seven));

        foreach (int again in Enumerable.Range(0, 20))
        {
            Assert.Equal(StatusCodes.Status200OK, await gate.EnterAsync(carrier, Seven));
        }
    }

    [Fact]
    public async Task TheTicketItselfIsSpentByTheFirstRequestSoItOpensNothingElse()
    {
        Gate gate = Made(out _);
        string carrier = gate.Issue(Watcher, Seven);

        await gate.EnterAsync(carrier, Seven);

        Assert.Null(gate.Tickets.Take(carrier, Seven));
    }

    [Fact]
    public async Task TheGrantIsShutInOnTheOneRecordingItWasOpenedFor()
    {
        Gate gate = Made(out _);
        string carrier = gate.Issue(Watcher, Seven);

        await gate.EnterAsync(carrier, Seven);

        Assert.Equal(StatusCodes.Status403Forbidden, await gate.EnterAsync(carrier, Eight));
    }

    [Fact]
    public async Task TheGrantStopsAdmittingWhenItsTwoHoursAreUpAndTheTicketCannotOpenAnother()
    {
        Gate gate = Made(out WoundClock clock);
        string carrier = gate.Issue(Watcher, Seven);

        await gate.EnterAsync(carrier, Seven);
        clock.Wind(PlaybackGrantPolicy.Default.Lifetime);

        Assert.Equal(StatusCodes.Status403Forbidden, await gate.EnterAsync(carrier, Seven));
    }

    [Fact]
    public async Task ATicketThatLapsedBeforeItWasUsedOpensNoGrant()
    {
        Gate gate = Made(out WoundClock clock);
        string carrier = gate.Issue(Watcher, Seven);

        clock.Wind(PlaybackTicketPolicy.Default.Lifetime);

        Assert.Equal(StatusCodes.Status403Forbidden, await gate.EnterAsync(carrier, Seven));
        Assert.Equal(StatusCodes.Status403Forbidden, await gate.EnterAsync(carrier, Seven));
    }

    [Fact]
    public async Task TakingTheWatchersGrantsBackShutsTheStreamOutOfTheNextRequest()
    {
        Gate gate = Made(out _);
        string carrier = gate.Issue(Watcher, Seven);

        await gate.EnterAsync(carrier, Seven);
        gate.Grants.RevokeEverythingOf(Watcher);

        Assert.Equal(StatusCodes.Status403Forbidden, await gate.EnterAsync(carrier, Seven));
    }

    [Fact]
    public async Task APlayerThatOpensALiveChannelAgainIsAdmittedOnTheGrantEnteringOpened()
    {
        Gate gate = Made(out _);
        string carrier = gate.Issue(Watcher, Tokyo);

        Assert.Equal(StatusCodes.Status200OK, await gate.EnterAsync(carrier, Tokyo));
        Assert.Equal(1, gate.Grants.Count);
        Assert.Equal(StatusCodes.Status200OK, await gate.EnterAsync(carrier, Tokyo));
        Assert.Null(gate.Tickets.Take(carrier, Tokyo));
    }

    [Fact]
    public async Task AGrantOpenedForALiveChannelOpensNoOtherChannelAndNoRecording()
    {
        Gate gate = Made(out _);
        string carrier = gate.Issue(Watcher, Tokyo);

        await gate.EnterAsync(carrier, Tokyo);

        Assert.Equal(StatusCodes.Status403Forbidden, await gate.EnterAsync(carrier, Education));
        Assert.Equal(StatusCodes.Status403Forbidden, await gate.EnterAsync(carrier, Seven));
    }

    [Fact]
    public async Task AGrantOpenedForARecordingOpensNoLiveChannel()
    {
        Gate gate = Made(out _);
        string carrier = gate.Issue(Watcher, Seven);

        await gate.EnterAsync(carrier, Seven);

        Assert.Equal(StatusCodes.Status403Forbidden, await gate.EnterAsync(carrier, Tokyo));
    }

    [Fact]
    public async Task EveryRefusalOnTheGrantWayInReadsTheSame()
    {
        Gate gate = Made(out _);
        string carrier = gate.Issue(Watcher, Eight);

        List<string> refusals =
        [
            await gate.BodyOfAsync(null, Seven),
            await gate.BodyOfAsync("not-a-ticket", Seven),
            await gate.BodyOfAsync(Unguessable.Issue(), Seven),
            await gate.BodyOfAsync(carrier, Seven),
        ];

        Assert.Single(refusals.Distinct(StringComparer.Ordinal));
        Assert.Equal(PlaybackTicketGate.TheSameRefusalForEveryBadTicket, refusals[0]);
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

        await new PlaybackTicketGate(tickets, Grants()).AdmitForAsLongAsTheGrantLastsAsync(
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

    private static Gate Made(out WoundClock clock)
    {
        clock = new WoundClock(At);

        return new Gate(clock);
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

        public string Written => System.Text.Encoding.UTF8.GetString(written.ToArray());

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

    private sealed class Gate
    {
        private readonly PlaybackTicketGate gate;

        internal Gate(WoundClock clock)
        {
            Tickets = new PlaybackTicketStore(clock, PlaybackTicketPolicy.Default);
            Grants = new PlaybackGrantStore(clock, PlaybackGrantPolicy.Default);
            gate = new PlaybackTicketGate(Tickets, Grants);
        }

        internal PlaybackTicketStore Tickets { get; }

        internal PlaybackGrantStore Grants { get; }

        internal string Issue(Subject subject, PlaybackTarget target)
        {
            IssuedPlaybackTicket? issued = Tickets.Issue(subject, target);

            Assert.NotNull(issued);

            return issued.InTheClear;
        }

        internal async Task<int> EnterAsync(string? carrier, PlaybackTarget target)
        {
            HttpContext context = Offering(carrier);

            await gate.AdmitForAsLongAsTheGrantLastsAsync(context, target, (_, _) => Task.CompletedTask);

            return context.Response.StatusCode;
        }

        internal async Task<string> BodyOfAsync(string? carrier, PlaybackTarget target)
        {
            HttpContext context = Offering(carrier);
            using var written = new MemoryStream();

            context.Response.Body = written;

            await gate.AdmitForAsLongAsTheGrantLastsAsync(context, target, (_, _) => Task.CompletedTask);

            return System.Text.Encoding.UTF8.GetString(written.ToArray());
        }

        private static HttpContext Offering(string? carrier)
        {
            DefaultHttpContext context = new();

            if (carrier is not null)
            {
                context.Request.Headers[HeaderNames.Authorization] =
                    $"Basic {Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"carina:{carrier}"))}";
            }

            return context;
        }
    }
}
