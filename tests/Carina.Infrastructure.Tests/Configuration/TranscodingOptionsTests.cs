using Carina.Domain.Streaming;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class TranscodingOptionsTests
{
    [Fact]
    public void NothingConfiguredMeansTheDefaultCeiling()
    {
        TranscodeBudgetSettings read = Read();

        Assert.Equal(new TranscodeBudgetSettings().AtOnce, read.AtOnce);
    }

    [Fact]
    public void TheCeilingReachesTheBudgetThatUsesIt()
    {
        Assert.Equal(6, Read(("Transcoding:AtOnce", "6")).AtOnce);
        Assert.Equal(1, Read(("Transcoding:AtOnce", "1")).AtOnce);
    }

    [Theory]
    [InlineData("Transcoding:AtOnce", "0")]
    [InlineData("Transcoding:AtOnce", "-1")]
    [InlineData("Transcoding:AtOnce", "four")]
    [InlineData("Transcoding:AtOnce", "2.5")]
    public void ACeilingNothingCouldRunUnderIsRefusedByName(string key, string value)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read((key, value)));

        Assert.Equal("AtOnce", refusal.ParamName);
        Assert.Contains("Transcoding:AtOnce", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSameRefusalIsWhatStopsTheProcessStarting()
    {
        TranscodingOptions options = new();
        options.ReadFrom(Configuration(("Transcoding:AtOnce", "0")));

        Assert.True(new TranscodingValidation().Validate(null, options).Failed);
        Assert.True(new TranscodingValidation().Validate(null, new TranscodingOptions()).Succeeded);
    }

    [Fact]
    public void ValidatingNothingIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => new TranscodingValidation().Validate(null, null!));
        Assert.Throws<ArgumentNullException>(() => new TranscodingOptions().ReadFrom(null!));
    }

    private static TranscodeBudgetSettings Read(params (string Key, string Value)[] settings)
    {
        TranscodingOptions options = new();
        options.ReadFrom(Configuration(settings));

        return options.Read();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
