namespace Carina.Conventions.Tests;

public sealed class RenderNodeConventionRuleTests
{
    private static readonly string[] TheBuildersHandedTheNode =
    [
        "/Carina.Infrastructure/Encodings/FfmpegEncodeInvocation.cs",
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
    public void OnlyTheBuildersHandedTheRenderNodeOpenTheCard()
    {
        Assert.Equal(TheBuildersHandedTheNode, RenderNodeConventionRules.FilesThatOpenTheCard(RepositoryPaths.SourceDirectory));
    }
}
