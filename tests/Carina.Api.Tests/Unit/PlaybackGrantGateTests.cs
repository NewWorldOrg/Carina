using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Unit;

public sealed class PlaybackGrantGateTests
{
    private static readonly DateTime At = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static readonly Subject Watcher = new("watcher");

    private static readonly PlaybackTarget Seven = PlaybackTarget.Recording("7");

    private static readonly PlaybackTarget Eight = PlaybackTarget.Recording("8");

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

        Assert.Null(gate.Tickets.Spend(carrier, Seven));
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
    public async Task TheLiveWayInSpendsATicketAndOpensNoGrantAtAll()
    {
        Gate gate = Made(out _);
        string carrier = gate.Issue(Watcher, PlaybackTarget.LiveChannel("32736-1024"));

        Assert.Equal(
            StatusCodes.Status200OK,
            await gate.WatchOnceAsync(carrier, PlaybackTarget.LiveChannel("32736-1024")));
        Assert.Equal(0, gate.Grants.Count);
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            await gate.WatchOnceAsync(carrier, PlaybackTarget.LiveChannel("32736-1024")));
    }

    [Fact]
    public async Task AGrantOpenedForARecordingDoesNotAdmitTheLiveWayIn()
    {
        Gate gate = Made(out _);
        string carrier = gate.Issue(Watcher, Seven);

        await gate.EnterAsync(carrier, Seven);

        Assert.Equal(StatusCodes.Status403Forbidden, await gate.WatchOnceAsync(carrier, Seven));
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

    private static Gate Made(out WoundClock clock)
    {
        clock = new WoundClock(At);

        return new Gate(clock);
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

        internal async Task<int> WatchOnceAsync(string? carrier, PlaybackTarget target)
        {
            HttpContext context = Offering(carrier);

            await gate.AdmitOnceAsync(context, target, (_, _) => Task.CompletedTask);

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
