using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Recordings;

using Microsoft.Extensions.Configuration;

namespace Carina.Infrastructure.Tests.Recordings;

public sealed class RecordingProgressSettingsTests
{
    [Fact]
    public void ConfigurationThatSaysNothingTellsTheScreensCountsAtMostEveryThirtySeconds()
    {
        RecordingProgressSettings read = Read(new Dictionary<string, string?>());

        Assert.Equal(RecordingProgressSettings.Default, read);
        Assert.Equal(TimeSpan.FromSeconds(30), read.AtMostEvery);
    }

    [Fact]
    public void ThePaceTheSectionCarriesIsRead()
        => Assert.Equal(
            TimeSpan.FromMinutes(2),
            Read(new Dictionary<string, string?> { ["RecordingProgress:AtMostEvery"] = "00:02:00" }).AtMostEvery);

    [Fact]
    public void APaceOfOneTickAndOneOfTheLongestAreBothHeld()
    {
        Assert.Equal(TimeSpan.FromTicks(1), new RecordingProgressSettings(TimeSpan.FromTicks(1)).AtMostEvery);
        Assert.Equal(
            TimeSpan.FromHours(1),
            new RecordingProgressSettings(RecordingProgressSettings.LongestPace).AtMostEvery);
    }

    [Theory]
    [InlineData("soon", "AtMostEvery")]
    [InlineData("00:00:00", "atMostEvery")]
    [InlineData("-00:00:10", "atMostEvery")]
    [InlineData("01:00:00.0000001", "atMostEvery")]
    public void APaceTheScreensCouldNotBeToldAtIsNamedAtStartup(string value, string named)
        => Assert.Equal(
            named,
            Assert.Throws<ArgumentException>(
                () => Read(new Dictionary<string, string?> { ["RecordingProgress:AtMostEvery"] = value })).ParamName);

    [Fact]
    public void TheValidationSaysWhenThePaceIsRefused()
    {
        var options = new RecordingProgressOptions();
        options.ReadFrom(Configuration(new Dictionary<string, string?> { ["RecordingProgress:AtMostEvery"] = "02:00:00" }));

        Assert.False(new RecordingProgressValidation().Validate(null, options).Succeeded);
        Assert.True(new RecordingProgressValidation().Validate(null, new RecordingProgressOptions()).Succeeded);
    }

    private static RecordingProgressSettings Read(IDictionary<string, string?> settings)
    {
        var options = new RecordingProgressOptions();
        options.ReadFrom(Configuration(settings));

        return options.Read();
    }

    private static IConfiguration Configuration(IDictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
}
