using Carina.Domain.Programmes;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class ProgrammeFeedOptionsTests
{
    [Fact]
    public void NothingConfiguredLeavesEverySettingAtWhatItAlreadyWas()
        => Assert.Equal(new ProgrammeFeedSettings(), Read());

    [Fact]
    public void BothSettingsAreReadAsWritten()
    {
        ProgrammeFeedSettings read = Read(
            ("ConcurrentReaders", "9"),
            ("StatementTimeout", "00:00:45"));

        Assert.Equal(9, read.ConcurrentReaders);
        Assert.Equal(TimeSpan.FromSeconds(45), read.StatementTimeout);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void AFeedLimitThatLetsNobodyThroughIsRefused(string setting)
        => Assert.Contains(
            "at least one caller",
            Refused(("ConcurrentReaders", setting)),
            StringComparison.Ordinal);

    [Fact]
    public void AFeedLimitThatIsNotAWholeNumberIsRefused()
        => Assert.Contains(
            "reads a whole number",
            Refused(("ConcurrentReaders", "a few")),
            StringComparison.Ordinal);

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:10")]
    public void AStatementGivenNoTimeAtAllIsRefused(string setting)
        => Assert.Contains(
            "longer than nothing",
            Refused(("StatementTimeout", setting)),
            StringComparison.Ordinal);

    [Fact]
    public void AStatementTimeThatIsNotADurationIsRefused()
        => Assert.Contains(
            "reads a duration",
            Refused(("StatementTimeout", "half a minute")),
            StringComparison.Ordinal);

    [Theory]
    [InlineData("30")]
    [InlineData("24.20:31:23.648")]
    public void AStatementTimeLongerThanPostgresCanBeToldToWaitIsRefused(string setting)
        => Assert.Contains(
            "cannot be longer than",
            Refused(("StatementTimeout", setting)),
            StringComparison.Ordinal);

    [Fact]
    public void TheLongestStatementTimePostgresCanBeToldToWaitIsStillRead()
        => Assert.Equal(
            TimeSpan.FromMilliseconds(int.MaxValue),
            Read(("StatementTimeout", "24.20:31:23.647")).StatementTimeout);

    private static string Refused(params (string Key, string Value)[] settings)
    {
        ValidateOptionsResult verdict = new ProgrammeFeedValidation().Validate(null, Options(settings));

        Assert.True(verdict.Failed);

        return verdict.FailureMessage!;
    }

    private static ProgrammeFeedSettings Read(params (string Key, string Value)[] settings)
        => Options(settings).Read();

    private static ProgrammeFeedOptions Options((string Key, string Value)[] settings)
    {
        var options = new ProgrammeFeedOptions();

        options.ReadFrom(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>($"{ProgrammeFeedOptions.Section}:{setting.Key}", setting.Value)))
            .Build());

        return options;
    }
}
