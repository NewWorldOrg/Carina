namespace Carina.Architecture.Tests;

public sealed class SessionEndRuleSelfCheckTests
{
    private const string Declaration = """
        public sealed class TunerSession
        {
            public bool EndsNoLaterThan(DateTimeOffset limit)
            {
                Interlocked.Exchange(ref endsAtTicks, limit.UtcTicks);
                return true;
            }
        }
        """;

    private const string SeatSwap = """
        private bool Demote(TunerSession outgoing, TunerSession taking)
        {
            outgoing.EndsNoLaterThan(taking.EndsAt);
            return true;
        }
        """;

    private const string ShorteningEndpoint = """
        private static async Task ShortenSession(HttpContext context, TunerSessionManager manager)
        {
            session.EndsNoLaterThan(request.EndsAt);
        }
        """;

    [Fact]
    public void ASecondFileThatMovesAnEndEarlierIsReported()
    {
        using var tree = new SourceTree();
        tree.Write(SessionEndRules.WhereItIsDeclared, Declaration);
        tree.Write(SessionEndRules.WhereItIsCalled, SeatSwap);
        tree.Write("Carina.Driver/Ipc/DriverApi.cs", ShorteningEndpoint);

        Assert.Equal(
            [
                new SessionEndCaller("Carina.Driver/Ipc/DriverApi.cs", 1),
                new SessionEndCaller(SessionEndRules.WhereItIsCalled, 1),
            ],
            SessionEndRules.CallersThatMoveAnEndEarlier(tree.Root));
    }

    [Fact]
    public void ASecondCallInTheFileAllowedOneIsReportedAsTwo()
    {
        using var tree = new SourceTree();
        tree.Write(SessionEndRules.WhereItIsDeclared, Declaration);
        tree.Write(SessionEndRules.WhereItIsCalled, SeatSwap + "\n" + ShorteningEndpoint);

        Assert.Equal(
            [new SessionEndCaller(SessionEndRules.WhereItIsCalled, 2)],
            SessionEndRules.CallersThatMoveAnEndEarlier(tree.Root));
    }

    [Fact]
    public void TheDeclarationItselfIsNotCountedAsACall()
    {
        using var tree = new SourceTree();
        tree.Write(SessionEndRules.WhereItIsDeclared, Declaration);

        Assert.Empty(SessionEndRules.CallersThatMoveAnEndEarlier(tree.Root));
        Assert.True(SessionEndRules.DeclaresTheMethod(tree.Root));
    }

    [Fact]
    public void ADeclarationThatWentAwayIsNoticed()
    {
        using var tree = new SourceTree();
        tree.Write(SessionEndRules.WhereItIsDeclared, "public sealed class TunerSession;");

        Assert.False(SessionEndRules.DeclaresTheMethod(tree.Root));
    }

    [Fact]
    public void AShorteningSpeltWithoutTheMethodWalksStraightPast()
    {
        using var tree = new SourceTree();
        tree.Write(SessionEndRules.WhereItIsDeclared, Declaration);
        tree.Write(
            "Carina.Driver/Sessions/TunerSessionManager.cs",
            """
            private void CutShort(TunerSession session, DateTimeOffset limit)
            {
                Interlocked.Exchange(ref session.endsAtTicks, limit.UtcTicks);
                session.Extend(limit);
                EndsNoLaterThan(limit);
            }
            """);

        Assert.Empty(SessionEndRules.CallersThatMoveAnEndEarlier(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-session-end-rules-");

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
