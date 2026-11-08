namespace Carina.Conventions.Tests;

public sealed class RenderNodeConventionRuleTests
{
    private static readonly string[] TheBuildersThatOpenTheCard =
    [
        "/Carina.Infrastructure/Encodings/FfmpegEncodeInvocation.cs",
        "/Carina.Infrastructure/Machines/NvencProbeInvocation.cs",
        "/Carina.Infrastructure/Machines/VaapiProbeInvocation.cs",
        "/Carina.Infrastructure/Streaming/FfmpegLiveInvocation.cs",
    ];

    [Fact]
    public void TheDefaultRenderNodeIsNamedOnlyAsTheDefaultOfTheSetting()
    {
        Assert.Empty(RenderNodeConventionRules.DefaultNamedOutsideTheSetting(RepositoryPaths.SourceDirectory));
    }

    [Fact]
    public void NoRenderNodeIsWrittenOutButTheDefault()
    {
        Assert.Empty(RenderNodeConventionRules.RenderNodesWrittenOut(RepositoryPaths.SourceDirectory));
    }

    [Fact]
    public void NoCudaDeviceIsWrittenOutButTheOneDeclared()
    {
        Assert.Empty(RenderNodeConventionRules.CudaDevicesWrittenOut(RepositoryPaths.SourceDirectory));
    }

    [Fact]
    public void OnlyTheBuildersOpenTheCard()
    {
        Assert.Equal(TheBuildersThatOpenTheCard, RenderNodeConventionRules.FilesThatOpenTheCard(RepositoryPaths.SourceDirectory));
    }
}
