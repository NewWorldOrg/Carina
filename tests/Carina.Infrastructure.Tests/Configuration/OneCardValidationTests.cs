using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class OneCardValidationTests
{
    [Theory]
    [InlineData("Software", "Software")]
    [InlineData("Vaapi", "Software")]
    [InlineData("Software", "Nvenc")]
    [InlineData("Nvenc", "Nvenc")]
    [InlineData("Vaapi", "Vaapi")]
    public void TwoSettingsThatNameOneCardOrNoneStartTheProcess(string encodes, string lives)
        => Assert.True(Validated(encodes, lives).Succeeded);

    [Theory]
    [InlineData("Vaapi", "Nvenc")]
    [InlineData("Nvenc", "Vaapi")]
    public void TwoSettingsThatNameDifferentCardsStopTheProcessNamingBoth(string encodes, string lives)
    {
        ValidateOptionsResult validated = Validated(encodes, lives);

        Assert.True(validated.Failed);
        Assert.Contains("Encodings:Prefer", validated.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Transcoding:Prefer", validated.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatingNothingIsRefused()
        => Assert.Throws<ArgumentNullException>(() => new OneCardValidation(Options.Create(new TranscodingOptions())).Validate(null, null!));

    private static ValidateOptionsResult Validated(string encodes, string lives)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>("Encodings:Prefer", encodes),
                new KeyValuePair<string, string?>("Transcoding:Prefer", lives),
            ])
            .Build();
        EncodingOptions encoding = new();
        encoding.ReadFrom(configuration);
        TranscodingOptions transcoding = new();
        transcoding.ReadFrom(configuration);

        return new OneCardValidation(Options.Create(transcoding)).Validate(null, encoding);
    }
}
