namespace Carina.Conventions.Tests;

public sealed class NestingConventionRuleSelfCheckTests
{
    private const string InAFile = "Carina.Infrastructure/Widgets/Widget.cs";

    public static TheoryData<string, int> EveryWayOfJoiningThreeOrMore => new()
    {
        { "int band = cold ? 1 : warm ? 2 : hot ? 3 : 4;", 3 },
        { "int band = cold ? warm ? hot ? 1 : 2 : 3 : 4;", 3 },
        { "int band = (cold ? warm : hot) ? 1 : (dry ? 2 : 3);", 3 },
        { "int band = cold ? (warm ? 1 : 2) : (hot ? 3 : 4);", 3 },
        { "int band = cold ? 1 : warm ? 2 : hot ? 3 : dry ? 4 : 5;", 4 },
    };

    public static TheoryData<string> EveryWayOfStayingWithinTwo =>
    [
        "int band = cold ? 1 : 2;",
        "int band = cold ? 1 : warm ? 2 : 3;",
        "int band = Pick(cold ? 1 : warm ? 2 : 3, hot ? 1 : dry ? 2 : 3);",
        "int band = cold ? Pick(warm ? 1 : hot ? 2 : 3) : 4;",
        "int? band = cold ? reading?.Level ?? 1 : null;",
        "int band = (cold, warm, hot) switch { (true, _, _) => 1, (_, true, _) => 2, (_, _, true) => 3, _ => 4 };",
        "string said = \"cold ? 1 : warm ? 2 : hot ? 3 : 4\";",
    ];

    public static TheoryData<string> EveryPlaceAGeneratedFileSits =>
    [
        "Carina.Db/Migrations/20260101000000_Widget.cs",
        "Carina.Db/Widgets/Widget.Designer.cs",
        "Carina.Infrastructure/obj/Widget.cs",
        "Carina.Infrastructure/bin/Widget.cs",
    ];

    [Theory]
    [MemberData(nameof(EveryWayOfJoiningThreeOrMore))]
    public void DetectsThisWayOfJoiningThreeOrMore(string source, int joined)
    {
        using SourceTree tree = new();
        tree.Write(InAFile, source);

        Assert.Equal(
            [$"/{InAFile}:1 joins {joined}"],
            NestingConventionRules.ConditionalsJoinedPastTheLimit(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfStayingWithinTwo))]
    public void DoesNotReportAnExpressionThatStaysWithinTwo(string source)
    {
        using SourceTree tree = new();
        tree.Write(InAFile, source);

        Assert.Empty(NestingConventionRules.ConditionalsJoinedPastTheLimit(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryPlaceAGeneratedFileSits))]
    public void LeavesAGeneratedFileAlone(string path)
    {
        using SourceTree tree = new();
        tree.Write(path, "int band = cold ? 1 : warm ? 2 : hot ? 3 : 4;");

        Assert.Empty(NestingConventionRules.ConditionalsJoinedPastTheLimit(tree.Root));
    }

    [Fact]
    public void NamesTheLineARunStartsOn()
    {
        using SourceTree tree = new();
        tree.Write(InAFile, "int first = 1;\nint band = cold\n    ? 1\n    : warm ? 2 : hot ? 3 : 4;");

        Assert.Equal(
            [$"/{InAFile}:2 joins 3"],
            NestingConventionRules.ConditionalsJoinedPastTheLimit(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-nesting-convention-rules-");

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
