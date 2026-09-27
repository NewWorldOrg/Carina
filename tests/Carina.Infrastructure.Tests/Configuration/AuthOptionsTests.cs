using Carina.Domain.Auth;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class AuthOptionsTests
{
    [Fact]
    public void NothingConfiguredLeavesASessionAtWhatItAlreadyLasted()
        => Assert.Equal(SessionPolicy.Default, ReadSession());

    [Fact]
    public void NothingConfiguredLeavesTheLoginLimitAtWhatItAlreadyWas()
        => Assert.Equal(LoginRatePolicy.Default, ReadLogin());

    [Fact]
    public void BothEndsOfASessionAreReadAsWritten()
    {
        SessionPolicy read = ReadSession(
            ("SessionAbsoluteLifetime", "14.00:00:00"),
            ("SessionIdleTimeout", "2.00:00:00"));

        Assert.Equal(TimeSpan.FromDays(14), read.AbsoluteLifetime);
        Assert.Equal(TimeSpan.FromDays(2), read.IdleTimeout);
    }

    [Fact]
    public void HowOftenASessionWritesDownThatItWasUsedIsReadAsWritten()
        => Assert.Equal(
            TimeSpan.FromMinutes(15),
            ReadSession(("SessionBetweenLastUsedWrites", "00:15:00")).BetweenLastUsedWrites);

    [Fact]
    public void NothingConfiguredLeavesTheWritesThatSayASessionWasUsedAsOftenAsTheyWere()
        => Assert.Equal(
            SessionPolicy.Default.BetweenLastUsedWrites,
            ReadSession(("SessionIdleTimeout", "2.00:00:00")).BetweenLastUsedWrites);

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:01:00")]
    public void WritingThatASessionWasUsedWithNoTimeBetweenIsRefused(string setting)
        => Assert.Contains(
            "longer than nothing",
            Refused(("SessionBetweenLastUsedWrites", setting)),
            StringComparison.Ordinal);

    [Fact]
    public void WritingThatASessionWasUsedLessOftenThanItIsForgottenIsRefused()
        => Assert.Contains(
            "was used",
            Refused(("SessionIdleTimeout", "01:00:00"), ("SessionBetweenLastUsedWrites", "01:00:00")),
            StringComparison.Ordinal);

    [Fact]
    public void ASessionForgottenJustAfterTheConfiguredWriteIsRead()
        => Assert.Equal(
            TimeSpan.FromHours(1) + TimeSpan.FromSeconds(1),
            ReadSession(("SessionIdleTimeout", "01:00:01"), ("SessionBetweenLastUsedWrites", "01:00:00")).IdleTimeout);

    [Fact]
    public void BothHalvesOfTheLoginLimitAreReadAsWritten()
    {
        LoginRatePolicy read = ReadLogin(
            ("LoginFailuresBeforeRefusing", "3"),
            ("LoginWindow", "00:10:00"));

        Assert.Equal(3, read.FailuresBeforeRefusing);
        Assert.Equal(TimeSpan.FromMinutes(10), read.Window);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-1.00:00:00")]
    public void ASessionGivenNoTimeAtAllIsRefused(string setting)
        => Assert.Contains(
            "longer than nothing",
            Refused(("SessionAbsoluteLifetime", setting)),
            StringComparison.Ordinal);

    [Fact]
    public void ASessionLengthThatIsNotADurationIsRefused()
        => Assert.Contains(
            "reads a duration",
            Refused(("SessionAbsoluteLifetime", "a fortnight")),
            StringComparison.Ordinal);

    [Fact]
    public void ASessionNothingWouldEverForgetIsRefused()
        => Assert.Contains(
            "cannot be longer than",
            Refused(("SessionAbsoluteLifetime", "366.00:00:00")),
            StringComparison.Ordinal);

    [Fact]
    public void TheLongestSessionAnythingStillForgetsIsRead()
        => Assert.Equal(
            TimeSpan.FromDays(365),
            ReadSession(("SessionAbsoluteLifetime", "365.00:00:00")).AbsoluteLifetime);

    [Fact]
    public void ASessionLeftIdleForLongerThanItLivesIsRefused()
        => Assert.Contains(
            "cannot outlast",
            Refused(("SessionAbsoluteLifetime", "7.00:00:00"), ("SessionIdleTimeout", "8.00:00:00")),
            StringComparison.Ordinal);

    [Fact]
    public void ASessionLeftIdleForExactlyAsLongAsItLivesIsRead()
    {
        SessionPolicy read = ReadSession(
            ("SessionAbsoluteLifetime", "7.00:00:00"),
            ("SessionIdleTimeout", "7.00:00:00"));

        Assert.Equal(read.AbsoluteLifetime, read.IdleTimeout);
    }

    [Fact]
    public void ASessionForgottenBeforeItCouldSayItWasUsedIsRefused()
        => Assert.Contains(
            "was used",
            Refused(("SessionIdleTimeout", "00:05:00")),
            StringComparison.Ordinal);

    [Fact]
    public void ASessionForgottenJustAfterItCouldSayItWasUsedIsRead()
        => Assert.Equal(
            TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1),
            ReadSession(("SessionIdleTimeout", "00:05:01")).IdleTimeout);

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void ALoginLimitThatCountsNoWrongPasswordIsRefused(string setting)
        => Assert.Contains(
            "at least one",
            Refused(("LoginFailuresBeforeRefusing", setting)),
            StringComparison.Ordinal);

    [Fact]
    public void ALoginLimitOfOneWrongPasswordIsRead()
        => Assert.Equal(1, ReadLogin(("LoginFailuresBeforeRefusing", "1")).FailuresBeforeRefusing);

    [Fact]
    public void ALoginLimitThatIsNotAWholeNumberIsRefused()
        => Assert.Contains(
            "reads a whole number",
            Refused(("LoginFailuresBeforeRefusing", "a handful")),
            StringComparison.Ordinal);

    [Fact]
    public void ALoginLimitSoHighThatItRefusesNobodyIsRefused()
        => Assert.Contains(
            "cannot be more than",
            Refused(("LoginFailuresBeforeRefusing", "101")),
            StringComparison.Ordinal);

    [Fact]
    public void TheHighestLoginLimitThatStillRefusesSomebodyIsRead()
        => Assert.Equal(100, ReadLogin(("LoginFailuresBeforeRefusing", "100")).FailuresBeforeRefusing);

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:10")]
    public void ALoginWindowThatCountsNothingIsRefused(string setting)
        => Assert.Contains(
            "longer than nothing",
            Refused(("LoginWindow", setting)),
            StringComparison.Ordinal);

    [Fact]
    public void TheShortestLoginWindowThatCountsAnythingIsRead()
        => Assert.Equal(TimeSpan.FromSeconds(1), ReadLogin(("LoginWindow", "00:00:01")).Window);

    [Fact]
    public void ALoginWindowThatIsNotADurationIsRefused()
        => Assert.Contains(
            "reads a duration",
            Refused(("LoginWindow", "five minutes")),
            StringComparison.Ordinal);

    [Fact]
    public void ALoginWindowWrittenAsABareNumberOfDaysIsRefused()
        => Assert.Contains(
            "cannot be longer than",
            Refused(("LoginWindow", "30")),
            StringComparison.Ordinal);

    [Fact]
    public void TheLongestLoginWindowIsRead()
        => Assert.Equal(TimeSpan.FromDays(1), ReadLogin(("LoginWindow", "1.00:00:00")).Window);

    private static string Refused(params (string Key, string Value)[] settings)
    {
        ValidateOptionsResult verdict = new AuthValidation().Validate(null, Options(settings));

        Assert.True(verdict.Failed);

        return verdict.FailureMessage!;
    }

    private static SessionPolicy ReadSession(params (string Key, string Value)[] settings)
        => Options(settings).ReadSession();

    private static LoginRatePolicy ReadLogin(params (string Key, string Value)[] settings)
        => Options(settings).ReadLogin();

    private static AuthOptions Options((string Key, string Value)[] settings)
    {
        var options = new AuthOptions();

        options.ReadFrom(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>($"{AuthOptions.Section}:{setting.Key}", setting.Value)))
            .Build());

        return options;
    }
}
