namespace Carina.Conventions.Tests;

public sealed class RenderNodeConventionRuleSelfCheckTests
{
    private const string TheSettings = "Carina.Domain/Machines/MachineSettings.cs";

    private const string InAFile = "Carina.Infrastructure/Widgets/Widget.cs";

    private const string TheSettingsAsTheyStand = """
        namespace Carina.Domain.Machines;

        public sealed record MachineSettings
        {
            public const string TheRenderNode = "/dev/dri/renderD128";

            public string RenderNode { get; init; } = TheRenderNode;
        }
        """;

    public static TheoryData<string> EveryWayOfNamingTheDefault =>
    [
        "class Widget { const string Node = MachineSettings.TheRenderNode; }",
        "class Widget { string Node() => Carina.Domain.Machines.MachineSettings.TheRenderNode; }",
        "class Widget { string[] Device() => [\"-vaapi_device\", MachineSettings.TheRenderNode]; }",
        "using static Carina.Domain.Machines.MachineSettings; class Widget { string Node => TheRenderNode; }",
        "class Widget { string Node { get; init; } = MachineSettings.TheRenderNode; }",
        "record MachineSettings { string Other { get; init; } = TheRenderNode; }",
    ];

    public static TheoryData<string> EveryWayOfWritingANodeOut =>
    [
        "class Widget { const string Node = \"/dev/dri/renderD129\"; }",
        "class Widget { string[] Device() => [\"-vaapi_device\", \"/dev/dri/renderD128\"]; }",
        "class Widget { string Node(int n) => $\"/dev/dri/renderD{n}\"; }",
        "class Widget { const string Node = @\"/dev/dri/card0\"; }",
    ];

    [Fact]
    public void TheSettingsAsTheyStandPass()
    {
        using SourceTree tree = new();
        tree.Write(TheSettings, TheSettingsAsTheyStand);

        Assert.Empty(RenderNodeConventionRules.DefaultNamedOutsideTheSetting(tree.Root));
        Assert.Empty(RenderNodeConventionRules.RenderNodesWrittenOut(tree.Root));
        Assert.Empty(RenderNodeConventionRules.FilesThatOpenTheCard(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfNamingTheDefault))]
    public void TheDefaultNamedAnywhereButTheSettingIsReported(string source)
    {
        using SourceTree tree = new();
        tree.Write(TheSettings, TheSettingsAsTheyStand);
        tree.Write(InAFile, source);

        Assert.Equal([$"/{InAFile}:1"], RenderNodeConventionRules.DefaultNamedOutsideTheSetting(tree.Root));
    }

    [Fact]
    public void TheDefaultNamedASecondTimeInsideTheSettingsIsReportedToo()
    {
        using SourceTree tree = new();
        tree.Write(
            TheSettings,
            TheSettingsAsTheyStand.Replace(
                "    public string RenderNode",
                "    public string Fallback => TheRenderNode;\n\n    public string RenderNode",
                StringComparison.Ordinal));

        Assert.Equal([$"/{TheSettings}:7"], RenderNodeConventionRules.DefaultNamedOutsideTheSetting(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfWritingANodeOut))]
    public void ANodeWrittenOutIsReported(string source)
    {
        using SourceTree tree = new();
        tree.Write(InAFile, source);

        Assert.Equal([$"/{InAFile}:1"], RenderNodeConventionRules.RenderNodesWrittenOut(tree.Root));
    }

    [Fact]
    public void ANodeInACommentIsNotWrittenOut()
    {
        using SourceTree tree = new();
        tree.Write(InAFile, "class Widget { } // opens /dev/dri/renderD128");

        Assert.Empty(RenderNodeConventionRules.RenderNodesWrittenOut(tree.Root));
    }

    [Theory]
    [InlineData("-vaapi_device")]
    [InlineData("-init_hw_device")]
    [InlineData("-hwaccel")]
    [InlineData("-filter_hw_device")]
    public void AFileThatOpensTheCardIsNamed(string flag)
    {
        using SourceTree tree = new();
        tree.Write(InAFile, $"class Widget {{ string[] Device(string node) => [\"{flag}\", node]; }}");
        tree.Write("Carina.Infrastructure/Widgets/Other.cs", "class Other { string[] Plain() => [\"-c:v\", \"libx264\"]; }");

        Assert.Equal([$"/{InAFile}"], RenderNodeConventionRules.FilesThatOpenTheCard(tree.Root));
    }

    [Fact]
    public void AGeneratedFileIsLeftAlone()
    {
        using SourceTree tree = new();
        tree.Write("Carina.Infrastructure/obj/Widget.cs", "class Widget { string Node = MachineSettings.TheRenderNode + \"/dev/dri\" + \"-vaapi_device\"; }");

        Assert.Empty(RenderNodeConventionRules.DefaultNamedOutsideTheSetting(tree.Root));
        Assert.Empty(RenderNodeConventionRules.RenderNodesWrittenOut(tree.Root));
        Assert.Empty(RenderNodeConventionRules.FilesThatOpenTheCard(tree.Root));
    }

    [Fact]
    public void ANodeAssembledFromPiecesWalksPast()
    {
        using SourceTree tree = new();
        tree.Write(InAFile, "class Widget { string Node = \"/dev/\" + \"dri/renderD129\"; }");

        Assert.Empty(RenderNodeConventionRules.RenderNodesWrittenOut(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-render-node-convention-rules-");

        public string Root => directory.FullName;

        public void Write(string path, string source)
        {
            string full = Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
