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
