using Carina.Domain.Auth;

namespace Carina.Domain.Tests.Auth;

public sealed class AuthSessionTests
{
    private static readonly DateTime Started = new(2026, 8, 19, 9, 0, 0, DateTimeKind.Utc);

    private static readonly SessionPolicy Policy =
        new(TimeSpan.FromDays(30), TimeSpan.FromDays(7), TimeSpan.FromMinutes(5));

    [Fact]
    public void AStartedSessionBeginsItsIdleWindowAtTheMomentItWasStarted()
    {
        AuthSession session = Start();

        Assert.Equal(Started, session.CreatedAt);
        Assert.Equal(Started, session.LastUsedAt);
        Assert.Null(session.RevokedAt);
        Assert.Equal(SessionStatus.Active, session.StatusAt(Started, Policy));
    }

    [Fact]
    public void ASessionRemembersWhichWayItsOwnerSignedIn()
    {
        AuthSession session = AuthSession.Start(
            SessionId.Issue(),
            new Subject("alice"),
            AuthMethod.Oidc,
            "iPad Safari",
            Started);

        Assert.Equal(AuthMethod.Oidc, session.Method);
        Assert.Equal("iPad Safari", session.DeviceLabel);
    }

    [Fact]
    public void ASessionStillInsideBothWindowsIsActive()
    {
        AuthSession session = Start();

        Assert.Equal(SessionStatus.Active, session.StatusAt(Started.AddDays(6), Policy));
    }

    [Fact]
    public void ASessionUntouchedForExactlyTheIdleWindowIsSpent()
    {
        AuthSession session = Start();

        Assert.Equal(SessionStatus.Expired, session.StatusAt(Started + Policy.IdleTimeout, Policy));
    }

    [Fact]
    public void ASessionUntouchedForATickShortOfTheIdleWindowIsStillActive()
    {
        AuthSession session = Start();

        Assert.Equal(
            SessionStatus.Active,
            session.StatusAt(Started + Policy.IdleTimeout - TimeSpan.FromTicks(1), Policy));
    }

    [Fact]
    public void ASessionKeptWarmStillDiesAtItsAbsoluteLifetime()
    {
        AuthSession session = Start();
        session.Touch(Started + Policy.AbsoluteLifetime - TimeSpan.FromHours(1), Policy);

        Assert.Equal(SessionStatus.Expired, session.StatusAt(Started + Policy.AbsoluteLifetime, Policy));
    }

    [Fact]
    public void ASessionATickShortOfItsAbsoluteLifetimeIsStillActive()
    {
        AuthSession session = Start();
        session.Touch(Started + Policy.AbsoluteLifetime - TimeSpan.FromHours(1), Policy);

        Assert.Equal(
            SessionStatus.Active,
            session.StatusAt(Started + Policy.AbsoluteLifetime - TimeSpan.FromTicks(1), Policy));
    }

    [Fact]
    public void ARevokedSessionIsNeverActiveAgainHoweverRecentlyItWasUsed()
    {
        AuthSession session = Start();
        session.Revoke(Started.AddMinutes(1));

        Assert.Equal(SessionStatus.Revoked, session.StatusAt(Started.AddMinutes(2), Policy));
    }

    [Fact]
    public void ARevokedSessionReadsAsRevokedRatherThanExpiredLongAfterwards()
    {
        AuthSession session = Start();
        session.Revoke(Started.AddMinutes(1));

        Assert.Equal(SessionStatus.Revoked, session.StatusAt(Started.AddYears(1), Policy));
    }

    [Fact]
    public void RevokingTwiceKeepsTheMomentItWasFirstCutOff()
    {
        AuthSession session = Start();
        session.Revoke(Started.AddMinutes(1));

        Assert.False(session.Revoke(Started.AddMinutes(9)));
        Assert.Equal(Started.AddMinutes(1), session.RevokedAt);
    }

    [Fact]
    public void RevokingReportsThatTheRowChanged()
    {
        AuthSession session = Start();

        Assert.True(session.Revoke(Started.AddMinutes(1)));
    }

    [Fact]
    public void ASessionCannotBeCutOffBeforeItExisted()
    {
        AuthSession session = Start();

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Revoke(Started.AddMinutes(-1)));
    }

    [Fact]
    public void UseWithinTheThrottleDoesNotBecomeAWrite()
    {
        AuthSession session = Start();

        Assert.False(session.Touch(Started + Policy.BetweenLastUsedWrites - TimeSpan.FromTicks(1), Policy));
        Assert.Equal(Started, session.LastUsedAt);
    }

    [Fact]
    public void UseAfterExactlyTheThrottleIsWorthAWrite()
    {
        AuthSession session = Start();

        Assert.True(session.Touch(Started + Policy.BetweenLastUsedWrites, Policy));
        Assert.Equal(Started + Policy.BetweenLastUsedWrites, session.LastUsedAt);
    }

    [Fact]
    public void ABurstOfRequestsCostsOneWriteRatherThanOnePerRequest()
    {
        AuthSession session = Start();
        int writes = 0;

        for (int second = 1; second <= 600; second++)
        {
            if (session.Touch(Started.AddSeconds(second), Policy))
            {
                writes++;
            }
        }

        Assert.Equal(2, writes);
        Assert.Equal(Started.AddSeconds(600), session.LastUsedAt);
    }

    [Fact]
    public void TheThrottleIsMeasuredFromTheLastWriteRatherThanTheLastRequest()
    {
        AuthSession session = Start();
        session.Touch(Started.AddMinutes(4), Policy);

        Assert.True(session.Touch(Started.AddMinutes(5), Policy));
    }

    [Fact]
    public void ARevokedSessionIsNotWrittenToJustBecauseItWasUsed()
    {
        AuthSession session = Start();
        session.Revoke(Started.AddMinutes(1));

        Assert.False(session.Touch(Started.AddHours(1), Policy));
        Assert.Equal(Started, session.LastUsedAt);
    }

    [Fact]
    public void AClockThatWentBackwardsNeverMovesTheIdleWindowBackwards()
    {
        AuthSession session = Start();
        session.Touch(Started.AddMinutes(10), Policy);

        Assert.False(session.Touch(Started.AddMinutes(1), Policy));
        Assert.Equal(Started.AddMinutes(10), session.LastUsedAt);
    }

    [Fact]
    public void ARehydratedSessionCarriesBackEverythingTheRowHeld()
    {
        SessionId id = SessionId.Issue();
        AuthSession session = AuthSession.Rehydrate(
            id,
            new Subject("alice"),
            AuthMethod.Local,
            Started,
            Started.AddHours(3),
            "Firefox on Linux",
            Started.AddHours(4));

        Assert.Equal(id, session.Id);
        Assert.Equal(new Subject("alice"), session.Subject);
        Assert.Equal(Started.AddHours(3), session.LastUsedAt);
        Assert.Equal(Started.AddHours(4), session.RevokedAt);
    }

    [Fact]
    public void ARowClaimingItWasUsedBeforeItExistedIsNotASession()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AuthSession.Rehydrate(
                SessionId.Issue(),
                new Subject("alice"),
                AuthMethod.Local,
                Started,
                Started.AddSeconds(-1),
                "Firefox on Linux",
                null));
    }

    [Fact]
    public void ARowClaimingItWasCutOffBeforeItExistedIsNotASession()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AuthSession.Rehydrate(
                SessionId.Issue(),
                new Subject("alice"),
                AuthMethod.Local,
                Started,
                Started,
                "Firefox on Linux",
                Started.AddSeconds(-1)));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ASessionRefusesATimeThatIsNotUtc(DateTimeKind kind)
    {
        Assert.Throws<ArgumentException>(
            () => AuthSession.Start(
                SessionId.Issue(),
                new Subject("alice"),
                AuthMethod.Local,
                "Firefox on Linux",
                DateTime.SpecifyKind(Started, kind)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ASessionWithoutADeviceLabelWouldLeaveTheListUnreadable(string label)
    {
        Assert.Throws<ArgumentException>(() => StartWith(label));
    }

    [Fact]
    public void ADeviceLabelIsTrimmedRatherThanStoredWithItsPadding()
    {
        AuthSession session = StartWith("  iPad Safari  ");

        Assert.Equal("iPad Safari", session.DeviceLabel);
    }

    [Fact]
    public void ADeviceLabelBuiltFromAUserAgentCannotSmuggleControlCharacters()
    {
        Assert.Throws<ArgumentException>(() => StartWith("Chrome\r\nSet-Cookie: forged"));
    }

    [Fact]
    public void ADeviceLabelLongerThanTheColumnIsRefusedBeforeTheDatabaseSeesIt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => StartWith(new string('u', AuthSession.LongestDeviceLabel + 1)));
    }

    [Fact]
    public void ASessionIsNeverStartedWithoutAnIdOrASubject()
    {
        Assert.Throws<ArgumentNullException>(
            () => AuthSession.Start(null!, new Subject("alice"), AuthMethod.Local, "Firefox", Started));
        Assert.Throws<ArgumentNullException>(
            () => AuthSession.Start(SessionId.Issue(), null!, AuthMethod.Local, "Firefox", Started));
    }

    [Fact]
    public void JudgingASessionNeedsAPolicyToJudgeItAgainst()
    {
        AuthSession session = Start();

        Assert.Throws<ArgumentNullException>(() => session.StatusAt(Started, null!));
        Assert.Throws<ArgumentNullException>(() => session.Touch(Started, null!));
    }

    private static AuthSession Start() => StartWith("Firefox on Linux");

    private static AuthSession StartWith(string deviceLabel)
        => AuthSession.Start(SessionId.Issue(), new Subject("alice"), AuthMethod.Local, deviceLabel, Started);
}
