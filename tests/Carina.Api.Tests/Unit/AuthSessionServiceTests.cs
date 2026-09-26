using Carina.Api.Common;
using Carina.Api.Services;
using Carina.BroadcastTestSupport;
using Carina.Domain.Auth;
using Carina.TestSupport;

namespace Carina.Api.Tests.Unit;

public sealed class AuthSessionServiceTests
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly Subject Owner = new("carina");

    private static readonly Subject Stranger = new("108204329581372");

    private readonly HeldClock clock = new(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private readonly HeldAuthSessions sessions = new();

    private readonly RecordedGrants grants = new();

    [Fact]
    public void TheListNamesTheDeviceThatIsAskingAsTheCurrentOne()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession there = Started(Owner, "another device");

        IReadOnlyList<SessionView> views = List(here);

        Assert.True(Assert.Single(views, view => view.Handle.Equals(here.Handle)).Current);
        Assert.False(Assert.Single(views, view => view.Handle.Equals(there.Handle)).Current);
    }

    [Fact]
    public void TheListLeavesOutSessionsThatAreOverSoTheOperatorSeesOnlyWhatIsStillOpen()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession ended = Started(Owner, "an ended device");
        ended.Revoke(clock.GetUtcNow().UtcDateTime);

        AuthSession stale = AuthSession.Rehydrate(
            SessionHandle.Of(SessionId.Issue()),
            Owner,
            Owner.Value,
            AuthMethod.Local,
            Now() - SessionPolicy.Default.IdleTimeout,
            Now() - SessionPolicy.Default.IdleTimeout,
            "a device left alone",
            null);

        sessions.Sessions.Add(stale);

        IReadOnlyList<SessionView> views = List(here);

        Assert.DoesNotContain(views, view => view.Handle.Equals(ended.Handle));
        Assert.DoesNotContain(views, view => view.Handle.Equals(stale.Handle));
        Assert.Equal([here.Handle], views.Select(view => view.Handle));
    }

    [Fact]
    public void BrAu018TheListCarriesEverySessionOnTheSystemWhoeverSignedItIn()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession theirs = StartedThroughTheProvider(Stranger, "somebody@example.test", "a stranger's device");

        IReadOnlyList<SessionView> views = List(here);

        Assert.Contains(views, view => view.Handle.Equals(theirs.Handle));
        Assert.Contains(views, view => view.Handle.Equals(here.Handle));
    }

    [Fact]
    public void BrAu018EachRowSaysWhoseItIsAndHowThatPersonSignedIn()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession theirs = StartedThroughTheProvider(Stranger, "somebody@example.test", "a stranger's device");

        IReadOnlyList<SessionView> views = List(here);

        SessionView mine = Assert.Single(views, view => view.Handle.Equals(here.Handle));
        SessionView other = Assert.Single(views, view => view.Handle.Equals(theirs.Handle));

        Assert.Equal("carina", mine.DisplayName);
        Assert.Equal(AuthMethod.Local, mine.Method);
        Assert.Equal("somebody@example.test", other.DisplayName);
        Assert.Equal(AuthMethod.Oidc, other.Method);
    }

    [Fact]
    public void BrAu018OnlyTheDeviceThatIsAskingIsTheCurrentOneHoweverManyPeopleAreListed()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession theirs = StartedThroughTheProvider(Stranger, "somebody@example.test", "a stranger's device");

        IReadOnlyList<SessionView> views = List(here);

        Assert.True(Assert.Single(views, view => view.Handle.Equals(here.Handle)).Current);
        Assert.False(Assert.Single(views, view => view.Handle.Equals(theirs.Handle)).Current);
    }

    [Fact]
    public async Task EndingAnotherDeviceLeavesThisOneSignedIn()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession there = Started(Owner, "another device");

        ServiceResult ended = await Service().RevokeAsync(there.Handle, Cancel);

        Assert.True(ended.IsSuccess);
        Assert.Equal(SessionStatus.Revoked, there.StatusAt(Now(), SessionPolicy.Default));
        Assert.Equal(SessionStatus.Active, here.StatusAt(Now(), SessionPolicy.Default));
    }

    [Fact]
    public async Task BrAu018SomebodyElsesSessionCanBeEndedFromHereAndTheirPlaybackGoesWithIt()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession theirs = StartedThroughTheProvider(Stranger, "somebody@example.test", "a stranger's device");

        ServiceResult ended = await Service().RevokeAsync(theirs.Handle, Cancel);

        Assert.True(ended.IsSuccess);
        Assert.Equal(SessionStatus.Revoked, theirs.StatusAt(Now(), SessionPolicy.Default));
        Assert.Equal(SessionStatus.Active, here.StatusAt(Now(), SessionPolicy.Default));
        Assert.Equal([Stranger], grants.RevokedFor);
    }

    [Fact]
    public async Task ASessionThatWasNeverIssuedIsNotFoundAndNobodysPlaybackIsTouched()
    {
        ServiceResult onNothing = await Service().RevokeAsync(SessionHandle.Of(SessionId.Issue()), Cancel);

        Assert.False(onNothing.IsSuccess);
        Assert.Equal(AuthSessionService.NoSuchSession, onNothing.ErrorMessage);
        Assert.Empty(grants.RevokedFor);
    }

    [Fact]
    public async Task TheCookieOfASessionIsNotAHandleThatEndsIt()
    {
        AuthSession there = Started(Owner, "another device");

        ServiceResult onTheCookie = await Service().RevokeAsync(new SessionHandle(sessions.CookieOf(there).Value), Cancel);

        Assert.False(onTheCookie.IsSuccess);
        Assert.Equal(AuthSessionService.NoSuchSession, onTheCookie.ErrorMessage);
        Assert.Equal(SessionStatus.Active, there.StatusAt(Now(), SessionPolicy.Default));
    }

    [Fact]
    public void NoRowOfTheListCarriesTheCookieOfTheSessionItShows()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession theirs = StartedThroughTheProvider(Stranger, "somebody@example.test", "a stranger's device");

        IReadOnlyList<SessionView> views = List(here);

        Assert.DoesNotContain(views, view => view.Handle.Value == sessions.CookieOf(here).Value);
        Assert.DoesNotContain(views, view => view.Handle.Value == sessions.CookieOf(theirs).Value);
    }

    [Fact]
    public async Task EndingTheSessionThatIsAskingIsAllowedAndLeavesItRevokedRatherThanDeleted()
    {
        AuthSession here = Started(Owner, "this device");

        ServiceResult ended = await Service().RevokeAsync(here.Handle, Cancel);

        Assert.True(ended.IsSuccess);
        Assert.Equal(SessionStatus.Revoked, here.StatusAt(Now(), SessionPolicy.Default));
        Assert.Equal(0, sessions.Deletions);
        Assert.Contains(sessions.Sessions, session => session.Handle.Equals(here.Handle));
    }

    [Fact]
    public async Task SigningOutTakesTheSessionRowAwayRatherThanLeavingItRevoked()
    {
        AuthSession here = Started(Owner, "this device");

        ServiceResult ended = await Service().LogOutAsync(Owner, here.Handle, Cancel);

        Assert.True(ended.IsSuccess);
        Assert.Equal(1, sessions.Deletions);
        Assert.Empty(sessions.Sessions);
    }

    [Fact]
    public async Task SigningOutTouchesNoOtherDevice()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession there = Started(Owner, "another device");

        await Service().LogOutAsync(Owner, here.Handle, Cancel);

        Assert.Equal([there.Handle.Value], sessions.Sessions.Select(session => session.Handle.Value));
        Assert.Equal(SessionStatus.Active, there.StatusAt(Now(), SessionPolicy.Default));
    }

    private AuthSessionService Service() => new(sessions, grants, SessionPolicy.Default, clock);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private AuthSession Started(Subject subject, string device)
    {
        SessionId cookie = SessionId.Issue();

        return sessions.Seat(cookie, AuthSession.Start(cookie, subject, subject.Value, AuthMethod.Local, device, Now()));
    }

    private AuthSession StartedThroughTheProvider(Subject subject, string displayName, string device)
    {
        SessionId cookie = SessionId.Issue();

        return sessions.Seat(cookie, AuthSession.Start(cookie, subject, displayName, AuthMethod.Oidc, device, Now()));
    }

    private IReadOnlyList<SessionView> List(AuthSession here)
        => Service().ListAsync(here.Handle, Cancel).GetAwaiter().GetResult().Data!;

    private sealed class RecordedGrants : IPlaybackGrantStore
    {
        public List<Subject> RevokedFor { get; } = [];

        public void Open(string carrier, Subject subject, PlaybackTarget target)
        {
        }

        public Subject? Admit(string? offered, PlaybackTarget target) => null;

        public int RevokeEverythingOf(Subject subject)
        {
            RevokedFor.Add(subject);

            return 0;
        }
    }
}
