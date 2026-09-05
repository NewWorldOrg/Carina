using Carina.Domain.Encodings;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class EncodingOptionsTests
{
    [Fact]
    public void NothingConfiguredMeansWorkIsWrittenBesideTheArtefact()
    {
        Assert.Null(Read().WorkedIn);
    }

    [Fact]
    public void AWorkingDirectoryReachesTheThingThatUsesIt()
    {
        Assert.Equal("/srv/encoding", Read(("Encodings:WorkedIn", "/srv/encoding")).WorkedIn);
    }

    [Theory]
    [InlineData("srv/encoding")]
    [InlineData("./encoding")]
    public void AWorkingDirectoryIsAbsoluteOrItIsRefusedNamingTheSetting(string path)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read(("Encodings:WorkedIn", path)));

        Assert.Contains("Encodings:WorkedIn", refusal.Message, StringComparison.Ordinal);

        var options = new EncodingOptions { WorkedIn = path };
        ValidateOptionsResult validated = new EncodingValidation().Validate(null, options);

        Assert.True(validated.Failed);
    }

    [Fact]
    public void NothingConfiguredAsksForTheProcessorAndGivesAJobThreeAttempts()
    {
        EncodeSettings settings = Read();

        Assert.Equal(EncodeEncoder.Software, settings.Prefer);
        Assert.Equal(2, settings.MostCores);
        Assert.Equal(3, settings.MostAttempts);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.BetweenLooks);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.StalledAfter);
    }

    [Fact]
    public void EachRunSettingReachesTheThingThatUsesIt()
    {
        EncodeSettings settings = Read(
            ("Encodings:Prefer", "vaapi"),
            ("Encodings:MostCores", "4"),
            ("Encodings:MostAttempts", "5"),
            ("Encodings:BetweenLooks", "00:01:00"),
            ("Encodings:StalledAfter", "00:20:00"));

        Assert.Equal(EncodeEncoder.Vaapi, settings.Prefer);
        Assert.Equal(4, settings.MostCores);
        Assert.Equal(5, settings.MostAttempts);
        Assert.Equal(TimeSpan.FromMinutes(1), settings.BetweenLooks);
        Assert.Equal(TimeSpan.FromMinutes(20), settings.StalledAfter);
    }

    [Theory]
    [InlineData("Encodings:Prefer", "quicksync")]
    [InlineData("Encodings:Prefer", "3")]
    [InlineData("Encodings:MostCores", "0")]
    [InlineData("Encodings:MostCores", "two")]
    [InlineData("Encodings:MostAttempts", "0")]
    [InlineData("Encodings:MostAttempts", "three")]
    [InlineData("Encodings:BetweenLooks", "00:00:00")]
    [InlineData("Encodings:BetweenLooks", "-00:00:01")]
    [InlineData("Encodings:StalledAfter", "soon")]
    public void ARunSettingThatCannotBeReadIsRefusedNamingTheSetting(string key, string value)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read((key, value)));

        Assert.Contains(key, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(value, refusal.Message, StringComparison.Ordinal);
    }

    private static EncodeSettings Read(params (string Key, string Value)[] settings)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();

        var options = new EncodingOptions();
        options.ReadFrom(configuration);

        return options.Read();
    }
}
