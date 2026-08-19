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

    private static readonly Subject Stranger = new("somebody-else");

    private readonly HeldClock clock = new(new DateTimeOffset(2026, 8, 19, 9, 0, 0, TimeSpan.Zero));

    private readonly HeldAuthSessions sessions = new();

    [Fact]
    public async Task TheListNamesTheDeviceThatIsAskingAsTheCurrentOne()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession there = Started(Owner, "another device");

        IReadOnlyList<SessionView> views = await ListAsync(here);

        Assert.True(Assert.Single(views, view => view.Id.Equals(here.Id)).Current);
        Assert.False(Assert.Single(views, view => view.Id.Equals(there.Id)).Current);
    }

    [Fact]
    public async Task TheListLeavesOutSessionsThatAreOverSoTheOperatorSeesOnlyWhatIsStillOpen()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession ended = Started(Owner, "an ended device");
        ended.Revoke(clock.GetUtcNow().UtcDateTime);

        AuthSession stale = AuthSession.Rehydrate(
            SessionId.Issue(),
            Owner,
            AuthMethod.Local,
            Now() - SessionPolicy.Default.IdleTimeout,
            Now() - SessionPolicy.Default.IdleTimeout,
            "a device left alone",
            null);

        sessions.Sessions.Add(stale);

        IReadOnlyList<SessionView> views = await ListAsync(here);

        Assert.DoesNotContain(views, view => view.Id.Equals(ended.Id));
        Assert.DoesNotContain(views, view => view.Id.Equals(stale.Id));
        Assert.Equal([here.Id.Value], views.Select(view => view.Id.Value));
    }

    [Fact]
    public async Task TheListShowsNothingBelongingToAnybodyElse()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession theirs = Started(Stranger, "a stranger's device");

        IReadOnlyList<SessionView> views = await ListAsync(here);

        Assert.DoesNotContain(views, view => view.Id.Equals(theirs.Id));
    }

    [Fact]
    public async Task EndingAnotherDeviceLeavesThisOneSignedIn()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession there = Started(Owner, "another device");

        ServiceResult ended = await Service().RevokeAsync(Owner, there.Id, Cancel);

        Assert.True(ended.IsSuccess);
        Assert.Equal(SessionStatus.Revoked, there.StatusAt(Now(), SessionPolicy.Default));
        Assert.Equal(SessionStatus.Active, here.StatusAt(Now(), SessionPolicy.Default));
    }

    [Fact]
    public async Task ASessionBelongingToSomebodyElseIsAnsweredTheSameWayAsOneThatDoesNotExist()
    {
        AuthSession theirs = Started(Stranger, "a stranger's device");

        ServiceResult onTheirs = await Service().RevokeAsync(Owner, theirs.Id, Cancel);
        ServiceResult onNothing = await Service().RevokeAsync(Owner, SessionId.Issue(), Cancel);

        Assert.False(onTheirs.IsSuccess);
        Assert.False(onNothing.IsSuccess);
        Assert.Equal(onNothing.ErrorMessage, onTheirs.ErrorMessage);
        Assert.Equal(SessionStatus.Active, theirs.StatusAt(Now(), SessionPolicy.Default));
    }

    [Fact]
    public async Task SigningOutTakesTheSessionRowAwayRatherThanLeavingItRevoked()
    {
        AuthSession here = Started(Owner, "this device");

        ServiceResult ended = await Service().LogOutAsync(here.Id, Cancel);

        Assert.True(ended.IsSuccess);
        Assert.Equal(1, sessions.Deletions);
        Assert.Empty(sessions.Sessions);
    }

    [Fact]
    public async Task SigningOutTouchesNoOtherDevice()
    {
        AuthSession here = Started(Owner, "this device");
        AuthSession there = Started(Owner, "another device");

        await Service().LogOutAsync(here.Id, Cancel);

        Assert.Equal([there.Id.Value], sessions.Sessions.Select(session => session.Id.Value));
        Assert.Equal(SessionStatus.Active, there.StatusAt(Now(), SessionPolicy.Default));
    }

    private AuthSessionService Service() => new(sessions, SessionPolicy.Default, clock);

    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    private AuthSession Started(Subject subject, string device)
    {
        AuthSession session = AuthSession.Start(
            SessionId.Issue(),
            subject,
            AuthMethod.Local,
            device,
            Now());

        sessions.Sessions.Add(session);

        return session;
    }

    private async Task<IReadOnlyList<SessionView>> ListAsync(AuthSession here)
    {
        ServiceResult<IReadOnlyList<SessionView>> asked = await Service().ListAsync(
            here.Subject,
            here.Id,
            Cancel);

        return asked.Data!;
    }
}
