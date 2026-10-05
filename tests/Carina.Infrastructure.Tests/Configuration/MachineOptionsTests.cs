using Carina.Domain.Machines;
using Carina.Infrastructure.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests.Configuration;

public sealed class MachineOptionsTests
{
    [Fact]
    public void NothingConfiguredMeansTheFirstRenderNode()
    {
        Assert.Equal("/dev/dri/renderD128", Read().RenderNode);
        Assert.Equal(new MachineSettings(), Read());
    }

    [Fact]
    public void TheRenderNodeAskedForReachesTheSettingsEveryCardUserReads()
        => Assert.Equal("/dev/dri/renderD129", Read(("Machine:RenderNode", "/dev/dri/renderD129")).RenderNode);

    [Fact]
    public void OnlyTheRenderNodeIsReadAndTheRestKeepTheirDefaults()
    {
        MachineSettings read = Read(("Machine:RenderNode", "/dev/dri/renderD129"));

        Assert.Equal(new MachineSettings() with { RenderNode = "/dev/dri/renderD129" }, read);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankRenderNodeMeansTheDefault(string written)
        => Assert.Equal(new MachineSettings().RenderNode, Read(("Machine:RenderNode", written)).RenderNode);

    [Theory]
    [InlineData("renderD129")]
    [InlineData("dev/dri/renderD129")]
    [InlineData(" /dev/dri/renderD129")]
    [InlineData("/dev/dri/renderD129 ")]
    public void ARenderNodeThatIsNotAnAbsolutePathIsRefusedByName(string written)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Read(("Machine:RenderNode", written)));

        Assert.Equal("RenderNode", refusal.ParamName);
        Assert.Contains("Machine:RenderNode", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARenderNodeThatIsNotAnAbsolutePathIsWhatStopsTheProcessStartingToo()
    {
        MachineOptions options = new();
        options.ReadFrom(Configuration(("Machine:RenderNode", "renderD129")));

        ValidateOptionsResult validated = new MachineValidation().Validate(null, options);

        Assert.True(validated.Failed);
        Assert.Contains("Machine:RenderNode", validated.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsoluteRenderNodeLetsTheProcessStart()
    {
        MachineOptions options = new();
        options.ReadFrom(Configuration(("Machine:RenderNode", "/dev/dri/renderD129")));

        Assert.True(new MachineValidation().Validate(null, options).Succeeded);
    }

    private static MachineSettings Read(params (string Key, string Value)[] settings)
    {
        MachineOptions options = new();
        options.ReadFrom(Configuration(settings));

        return options.Read();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting =>
                new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
