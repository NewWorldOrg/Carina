using Carina.Domain.Auth;
using Carina.Infrastructure.Auth;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Auth;

public sealed class PendingOidcLoginStoreTests
{
    private static readonly DateTime At = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AHandshakeIsHandedBackOnceAndThenIsGone()
    {
        var clock = new WoundClock(At);
        var store = new PendingOidcLoginStore(clock, OidcLoginPolicy.Default);
        PendingOidcLogin pending = Begun();

        store.Hold(pending);

        Assert.Same(pending, store.Take(pending.State));
        Assert.Null(store.Take(pending.State));
    }

    [Fact]
    public void AStateNobodyIssuedAnswersToNoHandshake()
    {
        var store = new PendingOidcLoginStore(new WoundClock(At), OidcLoginPolicy.Default);

        Assert.Null(store.Take(Unguessable.Issue()));
    }

    [Fact]
    public void HandshakesLeftOpenPastTheirWindowAreLetGoRatherThanKeptForever()
    {
        var clock = new WoundClock(At);
        var store = new PendingOidcLoginStore(clock, OidcLoginPolicy.Default);
        PendingOidcLogin stale = Begun();

        store.Hold(stale);
        clock.Wind(OidcLoginPolicy.Default.HandshakeLifetime);
        store.Hold(Begun());

        Assert.Equal(1, store.Count);
        Assert.Null(store.Take(stale.State));
    }

    [Fact]
    public void ACallerWhoStartsHandshakesWithoutFinishingThemCannotGrowTheStoreWithoutBound()
    {
        var store = new PendingOidcLoginStore(new WoundClock(At), OidcLoginPolicy.Default);

        for (int held = 0; held < PendingOidcLoginStore.MostHeldAtOnce * 2; held++)
        {
            store.Hold(Begun());
        }

        Assert.Equal(PendingOidcLoginStore.MostHeldAtOnce, store.Count);
    }

    private static PendingOidcLogin Begun() => PendingOidcLogin.Begin(Unguessable.Issue(), "/", At);
}
