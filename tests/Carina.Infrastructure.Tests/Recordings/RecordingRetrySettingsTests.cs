using Carina.Domain.Recordings;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingRetrySettingsTests
{
    [Fact]
    public void ConfigurationThatSaysNothingLeavesTheRetryAsItComes()
    {
        RetryPolicy read = Read(new Dictionary<string, string?>());

        Assert.Equal(RetryPolicy.Default, read);
        Assert.Equal(3, read.MostAttempts);
        Assert.Equal(TimeSpan.FromMinutes(1), read.BetweenAttempts);
    }

    [Fact]
    public void EverySettingTheSectionCarriesIsRead()
    {
        RetryPolicy read = Read(new Dictionary<string, string?>
        {
            ["RecordingRetry:MostAttempts"] = "5",
            ["RecordingRetry:BetweenAttempts"] = "00:02:30",
        });

        Assert.Equal(5, read.MostAttempts);
        Assert.Equal(TimeSpan.FromSeconds(150), read.BetweenAttempts);
    }

    [Fact]
    public void NoAttemptsAtAllIsARetryTheRecorderCanHold()
        => Assert.Equal(0, Read(new Dictionary<string, string?> { ["RecordingRetry:MostAttempts"] = "0" }).MostAttempts);

    [Theory]
    [InlineData("RecordingRetry:MostAttempts", "a few", "MostAttempts")]
    [InlineData("RecordingRetry:MostAttempts", "-1", "MostAttempts")]
    [InlineData("RecordingRetry:MostAttempts", "21", "mostAttempts")]
    [InlineData("RecordingRetry:BetweenAttempts", "every minute", "BetweenAttempts")]
    [InlineData("RecordingRetry:BetweenAttempts", "00:00:00", "betweenAttempts")]
    [InlineData("RecordingRetry:BetweenAttempts", "1.00:00:01", "betweenAttempts")]
    public void ASettingTheRetryCouldNotHoldIsNamedAtStartup(string key, string value, string named)
        => Assert.Equal(
            named,
            Assert.Throws<ArgumentException>(() => Read(new Dictionary<string, string?> { [key] = value })).ParamName);

    [Fact]
    public void TheValidationSaysWhichSettingItRefused()
    {
        var options = new RecordingRetryOptions();
        options.ReadFrom(Configuration(new Dictionary<string, string?> { ["RecordingRetry:MostAttempts"] = "99" }));

        ValidateOptionsVerdict(options, succeeded: false);
        ValidateOptionsVerdict(new RecordingRetryOptions(), succeeded: true);
    }

    private static void ValidateOptionsVerdict(RecordingRetryOptions options, bool succeeded)
        => Assert.Equal(succeeded, new RecordingRetryValidation().Validate(null, options).Succeeded);

    private static RetryPolicy Read(IDictionary<string, string?> settings)
    {
        var options = new RecordingRetryOptions();
        options.ReadFrom(Configuration(settings));

        return options.Read();
    }

    private static IConfiguration Configuration(IDictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
