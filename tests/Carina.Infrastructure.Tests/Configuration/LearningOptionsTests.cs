using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Segments;

using Microsoft.Extensions.Configuration;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class LearningOptionsTests
{
    [Fact(DisplayName = "nothing configured means no reduced copy is imported")]
    public void NothingConfiguredMeansNothingImported()
    {
        Assert.Null(Read().ImportFrom);
        Assert.Null(Read(("Learning:ImportFrom", "  ")).ImportFrom);
    }

    [Fact(DisplayName = "the directory the reduced copies are imported from reaches the import")]
    public void TheDirectoryReachesTheImport()
        => Assert.Equal("/srv/reduced", Read(("Learning:ImportFrom", "/srv/reduced")).ImportFrom);

    [Fact(DisplayName = "a directory that is not absolute stops the process naming the setting")]
    public void ADirectoryThatIsNotAbsoluteStopsTheProcess()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read(("Learning:ImportFrom", "reduced")));

        Assert.Contains("Learning:ImportFrom", refusal.Message, StringComparison.Ordinal);
        Assert.False(new LearningValidation().Validate(null, Options(("Learning:ImportFrom", "reduced"))).Succeeded);
        Assert.True(new LearningValidation().Validate(null, Options(("Learning:ImportFrom", "/srv/reduced"))).Succeeded);
    }

    private static LearningImportSettings Read(params (string Key, string Value)[] settings) => Options(settings).Read();

    private static LearningOptions Options(params (string Key, string Value)[] settings)
    {
        LearningOptions options = new();
        options.ReadFrom(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build());

        return options;
    }
}
