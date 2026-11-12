using Carina.Api.Authentication;
using Carina.Domain.Auth;
using Carina.TestSupport;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace Carina.Api.Tests.Unit;

public sealed class SignInRecordTests
{
    private const string Tablet =
        "Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148";

    private static readonly DateTime Founded = new(2026, 10, 2, 3, 0, 0, DateTimeKind.Utc);

    private readonly RecordingLogger logger = new();

    private readonly WoundClock clock = new(Founded);

    [Fact(DisplayName = "BR-AU-020: a line names what happened, the route, whether each cookie came, and the agent")]
    public void BrAu020ALineNamesWhatHappenedTheRouteWhetherEachCookieCameAndTheAgent()
    {
        DefaultHttpContext context = Asking("GET", "/api/auth/me", "api/auth/me", Tablet);
        context.Request.Headers[SignInRecord.FetchSite] = "same-origin";

        Record().Write(context, SignInMoment.RefusedWithoutASessionCookie);

        (LogLevel level, string said) = Assert.Single(logger.Lines);

        Assert.Equal(LogLevel.Information, level);
        Assert.Contains(nameof(SignInMoment.RefusedWithoutASessionCookie), said, StringComparison.Ordinal);
        Assert.Contains("GET /api/auth/me", said, StringComparison.Ordinal);
        Assert.Contains($"session cookie {SignInRecord.Absent}", said, StringComparison.Ordinal);
        Assert.Contains($"handshake cookie {SignInRecord.Absent}", said, StringComparison.Ordinal);
        Assert.Contains("fetch site same-origin", said, StringComparison.Ordinal);
        Assert.Contains(Tablet, said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: a line says a cookie came without saying what it carried")]
    public void BrAu020ALineSaysACookieCameWithoutSayingWhatItCarried()
    {
        string session = SessionId.Issue().Value;
        string mark = Unguessable.Issue();
        DefaultHttpContext context = Asking("GET", "/api/auth/me", "api/auth/me", Tablet);
        context.Request.Headers[HeaderNames.Cookie] =
            $"{SessionCookie.Name}={session}; {OidcHandshake.MarkName}={mark}";

        Record().Write(context, SignInMoment.TheSessionCookieNamedNoSession);

        string said = Assert.Single(logger.Lines).Said;

        Assert.Contains($"session cookie {SignInRecord.Carried}", said, StringComparison.Ordinal);
        Assert.Contains($"handshake cookie {SignInRecord.Carried}", said, StringComparison.Ordinal);
        Assert.DoesNotContain(session, said, StringComparison.Ordinal);
        Assert.DoesNotContain(mark, said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: a line carries the route as it is declared, so neither the query nor an identifier in the path is written")]
    public void BrAu020ALineCarriesTheRouteAsItIsDeclared()
    {
        string handle = SessionHandle.Of(SessionId.Issue()).Value;
        DefaultHttpContext context = Asking("DELETE", $"/api/auth/sessions/{handle}", "api/auth/sessions/{id}", Tablet);
        context.Request.QueryString = new QueryString("?state=a-state-in-the-query&code=a-code-in-the-query");

        Record().Write(context, SignInMoment.RefusedWithoutASessionCookie);

        string said = Assert.Single(logger.Lines).Said;

        Assert.Contains("DELETE /api/auth/sessions/{id}", said, StringComparison.Ordinal);
        Assert.DoesNotContain(handle, said, StringComparison.Ordinal);
        Assert.DoesNotContain("a-state-in-the-query", said, StringComparison.Ordinal);
        Assert.DoesNotContain("a-code-in-the-query", said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: a request that matched no route says so rather than writing the path it asked for")]
    public void BrAu020ARequestThatMatchedNoRouteSaysSo()
    {
        DefaultHttpContext context = Asking("GET", "/api/whatever/was-typed-here", null, Tablet);

        Record().Write(context, SignInMoment.RefusedWithoutASessionCookie);

        string said = Assert.Single(logger.Lines).Said;

        Assert.Contains(SignInRecord.NoRoute, said, StringComparison.Ordinal);
        Assert.DoesNotContain("was-typed-here", said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: a line about a known session says how it signed in and on which device")]
    public void BrAu020ALineAboutAKnownSessionSaysHowItSignedInAndOnWhichDevice()
    {
        DefaultHttpContext context = Asking("GET", "/api/auth/me", "api/auth/me", "node");

        Record().Write(context, SignInMoment.TheSessionWasUsed, AuthMethod.Oidc, Tablet);

        string said = Assert.Single(logger.Lines).Said;

        Assert.Contains($"signed in by {AuthMethod.Oidc} on {Tablet}", said, StringComparison.Ordinal);
        Assert.Contains("agent node", said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: an agent cannot forge a second line, and a long one is cut")]
    public void BrAu020AnAgentCannotForgeASecondLineAndALongOneIsCut()
    {
        string forged = $"curl\r\n2026-10-02T03:00:00.000Z info: forged\u2028{new string('a', 400)}";
        DefaultHttpContext context = Asking("GET", "/api/auth/me", "api/auth/me", forged);

        Record().Write(context, SignInMoment.RefusedWithoutASessionCookie);

        string said = Assert.Single(logger.Lines).Said;

        Assert.DoesNotContain("\r", said, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", said, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2028", said, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('a', SignInRecord.LongestAgent + 1), said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: an agent is trimmed of spaces, cut and has its unprintable letters replaced, however long it arrives")]
    public void BrAu020AnAgentIsTrimmedCutAndMadePrintableHoweverLongItArrives()
    {
        string head = $"  Agent\t{new string('b', SignInRecord.LongestAgent)}";
        DefaultHttpContext context = Asking("GET", "/api/auth/me", "api/auth/me", $"{head}{new string('c', 100_000)}");

        Record().Write(context, SignInMoment.RefusedWithoutASessionCookie);

        string said = Assert.Single(logger.Lines).Said;
        string expected = $"Agent?{new string('b', SignInRecord.LongestAgent - "Agent?".Length)}";

        Assert.EndsWith($"agent {expected}.", said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: agents that differ only past the cut are quieted as one")]
    public void BrAu020AgentsThatDifferOnlyPastTheCutAreQuietedAsOne()
    {
        SignInRecord record = Record();
        string head = new('a', SignInRecord.LongestAgent);

        record.Write(Asking("GET", "/api/auth/me", "api/auth/me", $"{head}one"), SignInMoment.RefusedWithoutASessionCookie);
        record.Write(Asking("GET", "/api/auth/me", "api/auth/me", $"{head}two"), SignInMoment.RefusedWithoutASessionCookie);

        Assert.Single(logger.Lines);
    }

    [Theory(DisplayName = "BR-AU-020: the same kind of refusal from the same agent is written once in the quiet span")]
    [InlineData(SignInMoment.RefusedWithoutASessionCookie)]
    [InlineData(SignInMoment.TheSessionCookieNamedNoSession)]
    [InlineData(SignInMoment.TheSessionHadExpired)]
    [InlineData(SignInMoment.TheSessionHadBeenRevoked)]
    public void BrAu020TheSameKindOfRefusalFromTheSameAgentIsWrittenOnceInTheQuietSpan(SignInMoment moment)
    {
        SignInRecord record = Record();

        for (int asked = 0; asked < 5; asked++)
        {
            record.Write(Asking("GET", "/api/auth/me", "api/auth/me", Tablet), moment);
        }

        Assert.Single(logger.Lines);

        clock.Wind(SignInRecord.Quiet);
        record.Write(Asking("GET", "/api/auth/me", "api/auth/me", Tablet), moment);

        Assert.Equal(2, logger.Lines.Count);
    }

    [Fact(DisplayName = "BR-AU-020: another agent, or another kind of refusal, is not silenced by the first")]
    public void BrAu020AnotherAgentOrAnotherKindIsNotSilencedByTheFirst()
    {
        SignInRecord record = Record();

        record.Write(Asking("GET", "/api/auth/me", "api/auth/me", Tablet), SignInMoment.RefusedWithoutASessionCookie);
        record.Write(Asking("GET", "/api/auth/me", "api/auth/me", "node"), SignInMoment.RefusedWithoutASessionCookie);
        record.Write(Asking("GET", "/api/auth/me", "api/auth/me", Tablet), SignInMoment.TheSessionHadExpired);

        Assert.Equal(3, logger.Lines.Count);
    }

    [Theory(DisplayName = "BR-AU-020: a deliberate act at a way in is written every time")]
    [InlineData(SignInMoment.TheSessionWasUsed)]
    [InlineData(SignInMoment.ALocalSignInOpenedASession)]
    [InlineData(SignInMoment.ALocalSignInWasRefused)]
    [InlineData(SignInMoment.ALocalSignInWasHeldOff)]
    [InlineData(SignInMoment.TheWayToTheProviderWasOpened)]
    [InlineData(SignInMoment.TheWayToTheProviderCouldNotBeOpened)]
    [InlineData(SignInMoment.TheWayBackFromTheProviderOpenedASession)]
    [InlineData(SignInMoment.TheWayBackFromTheProviderWasRefused)]
    [InlineData(SignInMoment.SignedOut)]
    public void BrAu020ADeliberateActAtAWayInIsWrittenEveryTime(SignInMoment moment)
    {
        SignInRecord record = Record();

        record.Write(Asking("GET", "/api/auth/me", "api/auth/me", Tablet), moment);
        record.Write(Asking("GET", "/api/auth/me", "api/auth/me", Tablet), moment);

        Assert.Equal(2, logger.Lines.Count);
    }

    [Fact(DisplayName = "BR-AU-020: the refusals at the gate are the only moments quieted, whatever moments there come to be")]
    public void BrAu020TheRefusalsAtTheGateAreTheOnlyMomentsQuieted()
    {
        SignInMoment[] quieted =
        [
            SignInMoment.RefusedWithoutASessionCookie,
            SignInMoment.TheSessionCookieNamedNoSession,
            SignInMoment.TheSessionHadExpired,
            SignInMoment.TheSessionHadBeenRevoked,
        ];

        foreach (SignInMoment moment in Enum.GetValues<SignInMoment>())
        {
            var heard = new RecordingLogger();
            var record = new SignInRecord(heard, clock);

            record.Write(Asking("GET", "/api/auth/me", "api/auth/me", Tablet), moment);
            record.Write(Asking("GET", "/api/auth/me", "api/auth/me", Tablet), moment);

            Assert.Equal(quieted.Contains(moment) ? 1 : 2, heard.Lines.Count);
        }
    }

    [Fact(DisplayName = "BR-AU-020: a line for a session that was ended says who ended it, how many were ended, and how the ended one had signed in and where")]
    public void BrAu020ALineForASessionThatWasEndedSaysWhoEndedItAndWhatWasEnded()
    {
        string handle = SessionHandle.Of(SessionId.Issue()).Value;

        Record().Write(
            Asking("DELETE", $"/api/auth/sessions/{handle}", "api/auth/sessions/{id}", Tablet),
            SignInMoment.TheSessionRevokedAnother,
            AuthMethod.Local,
            "a desk",
            ended: new EndedSessions(1, AuthMethod.Oidc, "a phone"));

        (LogLevel level, string said) = Assert.Single(logger.Lines);

        Assert.Equal(LogLevel.Information, level);
        Assert.Contains(nameof(SignInMoment.TheSessionRevokedAnother), said, StringComparison.Ordinal);
        Assert.Contains("DELETE /api/auth/sessions/{id}", said, StringComparison.Ordinal);
        Assert.Contains(
            "signed in by Local on a desk, ended 1 signed in by Oidc on a phone",
            said,
            StringComparison.Ordinal);
        Assert.Contains(Tablet, said, StringComparison.Ordinal);
        Assert.DoesNotContain(handle, said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: a line for the sessions a changed password ended says how many and names none of them")]
    public void BrAu020ALineForTheSessionsAChangedPasswordEndedSaysHowManyAndNamesNone()
    {
        Record().Write(
            Asking("POST", "/api/auth/password", "api/auth/password", Tablet),
            SignInMoment.AChangedPasswordRevokedTheOthers,
            AuthMethod.Local,
            "a desk",
            ended: new EndedSessions(3));

        string said = Assert.Single(logger.Lines).Said;

        Assert.Contains(
            $"signed in by Local on a desk, ended 3 signed in by {SignInRecord.Unsaid} on {SignInRecord.Unsaid}",
            said,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: the label of an ended device cannot break the line it is written in")]
    public void BrAu020TheLabelOfAnEndedDeviceCannotBreakTheLineItIsWrittenIn()
    {
        Record().Write(
            Asking("DELETE", "/api/auth/sessions/any", "api/auth/sessions/{id}", Tablet),
            SignInMoment.TheSessionRevokedAnother,
            ended: new EndedSessions(1, AuthMethod.Local, "a phone\nSign-in SignedOut at"));

        string said = Assert.Single(logger.Lines).Said;

        Assert.DoesNotContain('\n', said);
        Assert.Contains("on a phone?Sign-in SignedOut at", said, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: a way in that was refused is a warning")]
    public void BrAu020AWayInThatWasRefusedIsAWarning()
    {
        SignInRecord record = Record();

        record.Write(
            Asking("GET", "/api/auth/oidc/callback", "api/auth/oidc/callback", Tablet),
            SignInMoment.TheWayBackFromTheProviderWasRefused,
            reason: nameof(OidcRefusal.TheHandshakeBelongsToAnotherBrowser));

        (LogLevel level, string said) = Assert.Single(logger.Lines);

        Assert.Equal(LogLevel.Warning, level);
        Assert.Contains(
            $"reason {nameof(OidcRefusal.TheHandshakeBelongsToAnotherBrowser)}",
            said,
            StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-AU-020: agents beyond what is remembered are not written, so the memory and the log both stay bounded")]
    public void BrAu020AgentsBeyondWhatIsRememberedAreNotWritten()
    {
        SignInRecord record = Record();

        for (int agent = 0; agent < SignInRecord.MostAgentsRemembered + 50; agent++)
        {
            record.Write(
                Asking("GET", "/api/auth/me", "api/auth/me", $"agent-{agent}"),
                SignInMoment.RefusedWithoutASessionCookie);
        }

        Assert.Equal(SignInRecord.MostAgentsRemembered, logger.Lines.Count);

        clock.Wind(SignInRecord.Quiet);
        record.Write(
            Asking("GET", "/api/auth/me", "api/auth/me", "an agent that came after the span"),
            SignInMoment.RefusedWithoutASessionCookie);

        Assert.Equal(SignInRecord.MostAgentsRemembered + 1, logger.Lines.Count);
    }

    private static DefaultHttpContext Asking(string method, string path, string? route, string agent)
    {
        var context = new DefaultHttpContext();

        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.Headers.UserAgent = agent;

        if (route is not null)
        {
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse(route),
                0,
                EndpointMetadataCollection.Empty,
                route));
        }

        return context;
    }

    private SignInRecord Record() => new(logger, clock);

    private sealed class RecordingLogger : ILogger<SignInRecord>
    {
        public List<(LogLevel Level, string Said)> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
