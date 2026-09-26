namespace Carina.Conventions.Tests;

public sealed class VarConventionRuleSelfCheckTests
{
    private const string InAFile = "Carina.Infrastructure/Widgets/Widget.cs";

    public static TheoryData<string, string> EveryWayOfHidingATypeBehindVar => new()
    {
        { "var id = Guid.NewGuid();", "id" },
        { "var enabled = tuners.Where(tuner => tuner.IsOn).ToList();", "enabled" },
        { "using var reading = CancellationTokenSource.CreateLinkedTokenSource(token);", "reading" },
        { "var answer = DvbPropertyList.Asking(asked);", "answer" },
    };

    public static TheoryData<string> EveryWayOfNamingItAlready =>
    [
        "TunerSnapshot enabled = tuners.Where(tuner => tuner.IsOn).ToList();",
        "Guid id = Guid.NewGuid();",
        "using CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(token);",
    ];

    public static TheoryData<string> EveryWayOfConstructingThatSpeaksForItself =>
    [
        "var stream = new FileStream(path, FileMode.Open);",
        "await using var writing = new StreamWriter(stream);",
        "var made = new[] { 1, 2, 3 };",
        "var cast = (Recording)found;",
        "var text = \"held\";",
        "var count = 5;",
        "foreach (var tuner in tuners) { Use(tuner); }",
    ];

    [Fact]
    public void AnAnonymousProjectionIsLeftAlone()
    {
        using SourceTree tree = new();
        tree.Write(InAFile, "var finished = rows.Select(row => new { row.Id, row.Name }).ToList();");

        Assert.Empty(VarConventionRules.NonApparentVarDeclarations(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfHidingATypeBehindVar))]
    public void DetectsThisWayOfHidingATypeBehindVar(string source, string name)
    {
        using SourceTree tree = new();
        tree.Write(InAFile, source);

        Assert.Equal([$"/{InAFile} {name}"], VarConventionRules.NonApparentVarDeclarations(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfNamingItAlready))]
    public void DoesNotReportADeclarationThatAlreadyNamesItsType(string source)
    {
        using SourceTree tree = new();
        tree.Write(InAFile, source);

        Assert.Empty(VarConventionRules.NonApparentVarDeclarations(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfConstructingThatSpeaksForItself))]
    public void DoesNotReportAConstructionThatSpeaksForItself(string source)
    {
        using SourceTree tree = new();
        tree.Write(InAFile, source);

        Assert.Empty(VarConventionRules.NonApparentVarDeclarations(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-var-convention-rules-");

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
