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

    public static TheoryData<string, int> EveryWayOfNestingFourOrMore => new()
    {
        { "class Widget { void Run() { if (a) { if (b) { if (c) { if (d) { } } } } } }", 4 },
        { "class Widget { void Run() { if (a) { if (b) { if (c) { if (d) { if (e) { } } } } } } }", 5 },
        { "class Widget { void Run() { try { using (held) { while (more) { if (a) { } } } } finally { } } }", 4 },
        { "class Widget { void Run() { foreach (int one in all) { for (;;) { do { switch (one) { default: break; } } while (more); } } } }", 4 },
        { "class Widget { void Run() { lock (gate) { try { } catch { if (a) { while (more) { } } } } } }", 4 },
        { "class Widget { void Run() { try { } finally { if (a) { if (b) { foreach (int one in all) { } } } } } }", 4 },
        { "class Widget { void Run() { if (a) { } else { if (b) { if (c) { if (d) { } } } } } }", 4 },
        { "class Widget { void Run() { switch (one) { case 1: if (a) { while (more) { using (held) { } } } break; } } }", 4 },
        { "class Widget { void Run() { if (a) if (b) if (c) if (d) Go(); } }", 4 },
        { "class Widget { void Run() { lock (gate) { if (a) { fixed (byte* held = bytes) { if (b) { } } } } } }", 4 },
        { "class Widget { void Run() { unsafe { checked { unchecked { if (a) { } } } } } }", 4 },
        { "class Widget { int Size { get { if (a) { if (b) { if (c) { if (d) { } } } } return 1; } } }", 4 },
        { "class Widget { Widget() { if (a) { if (b) { if (c) { if (d) { } } } } } }", 4 },
        { "class Widget { void Run() { void Inner() { if (a) { if (b) { if (c) { if (d) { } } } } } } }", 4 },
        { "class Widget { void Run() { Go(() => { if (a) { if (b) { if (c) { if (d) { } } } } }); } }", 4 },
        { "class Widget { void Run() { Go(delegate { if (a) { if (b) { if (c) { if (d) { } } } } }); } }", 4 },
        { "if (a) { if (b) { if (c) { if (d) { } } } }", 4 },
    };

    public static TheoryData<string> EveryWayOfStayingWithinThree =>
    [
        "class Widget { void Run() { if (a) { if (b) { if (c) { } } } } }",
        "class Widget { void Run() { if (a) { if (b) { if (c) { } else if (d) { } else if (e) { } else { } } } } }",
        "class Widget { void Run() { try { using (held) { while (more) { using Held other = Open(); Go(); } } } finally { } } }",
        "class Widget { void Run() { try { } catch { if (a) { if (b) { } } } finally { if (c) { if (d) { } } } } }",
        "class Widget { void Run() { switch (one) { case 1: if (a) { if (b) { } } break; default: if (c) { if (d) { } } break; } } }",
        "class Widget { void Run() { if (a) { { if (b) { { if (c) { { Go(); } } } } } } } }",
        "class Widget { void Run() { unsafe { fixed (byte* held = bytes) { if (a) { int sum = checked(one + two); } } } } }",
        "class Widget { void Run() { if (a) { if (b) { if (c) { Go(() => { if (d) { if (e) { if (f) { } } } }); } } } } }",
        "class Widget { void Run() { if (a) { if (b) { if (c) { Inner(); void Inner() { if (d) { if (e) { if (f) { } } } } } } } } }",
        "class Widget { void Run() { if (a) { if (b) { if (c) { int band = one switch { 1 => 2, _ => 3 }; } } } } }",
        "class Widget { void Run() { if (a) { if (b) { if (c) { string said = \"if (d) { }\"; } } } } }",
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

    [Theory]
    [MemberData(nameof(EveryWayOfNestingFourOrMore))]
    public void DetectsThisWayOfNestingFourOrMore(string source, int depth)
    {
        using SourceTree tree = new();
        tree.Write(InAFile, source);

        Assert.Equal(
            [$"/{InAFile}:1 nests {depth}"],
            NestingConventionRules.BlocksNestedPastTheLimit(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryWayOfStayingWithinThree))]
    public void DoesNotReportAFunctionThatStaysWithinThree(string source)
    {
        using SourceTree tree = new();
        tree.Write(InAFile, source);

        Assert.Empty(NestingConventionRules.BlocksNestedPastTheLimit(tree.Root));
    }

    [Theory]
    [MemberData(nameof(EveryPlaceAGeneratedFileSits))]
    public void LeavesTheBlocksOfAGeneratedFileAlone(string path)
    {
        using SourceTree tree = new();
        tree.Write(path, "if (a) { if (b) { if (c) { if (d) { } } } }");

        Assert.Empty(NestingConventionRules.BlocksNestedPastTheLimit(tree.Root));
    }

    [Fact]
    public void NamesEachFunctionOnceByTheLineOfItsDeepestBlock()
    {
        using SourceTree tree = new();
        tree.Write(
            InAFile,
            """
            class Widget
            {
                void Shallow()
                {
                    if (a) { if (b) { if (c) { } } }
                }

                void Deep()
                {
                    while (more)
                    {
                        foreach (int one in all)
                        {
                            if (a)
                            {
                                if (b) { }
                                if (c)
                                {
                                    if (d) { }
                                }
                            }
                        }
                    }
                }

                void DeepToo()
                {
                    if (a) { if (b) { if (c) {
                        if (d) { }
                    } } }
                }
            }
            """);

        Assert.Equal(
            [$"/{InAFile}:19 nests 5", $"/{InAFile}:29 nests 4"],
            NestingConventionRules.BlocksNestedPastTheLimit(tree.Root));
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
