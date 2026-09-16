namespace Carina.Architecture.Tests;

public sealed class LibraryQualityRuleSelfCheckTests
{
    private const string WhereARowIsBuilt = "/Carina.Api/Responder/Recordings/RecordingResponder.cs";

    private const string WhereARowIsAskedFor = "/Carina.Api/Controllers/Recordings/ListRecordingsAction.cs";

    public static TheoryData<string> EveryWayOfDecidingAStandingOfItsOwn => new()
    {
        """QualityLevel Read(double share, ThresholdBand band) => Of(ThresholdEvaluator.Judge(share, band));""",
        """if (verdict.Standing is QualityStanding.Unsupported) { }""",
        """ThresholdVerdict verdict = Judge(share, band);""",
        """QualityThresholdKey key = QualityThresholdKey.PacketsLostWarning;""",
        """QualityThresholdShape shape = QualityThresholdShapes.Of(key);""",
        """QualityBands bands = QualityThresholdStanding.Bands(standings);""",
        """ThresholdBand band = bands.For(QualityMetric.PacketsLost);""",
    };

    public static TheoryData<string> EveryWayOfWritingAShareARecordingIsJudgedAgainst => new()
    {
        """if ((double)dropped / total > 0.0002) { }""",
        """const double Unwatchable = 1e-3;""",
        """const double Warning = 5.0d;""",
        """if (left / (double)total >= 0.01f) { }""",
    };

    [Theory]
    [MemberData(nameof(EveryWayOfDecidingAStandingOfItsOwn))]
    public void DetectsThisWayOfDecidingAStanding(string source)
        => Assert.NotEmpty(LibraryQualityRules.WhatDecidesAStandingIn(source));

    [Theory]
    [MemberData(nameof(EveryWayOfWritingAShareARecordingIsJudgedAgainst))]
    public void DetectsThisShareARecordingCouldBeJudgedAgainst(string source)
        => Assert.NotEmpty(LibraryQualityRules.SharesIn(source));

    [Fact]
    public void CarryingAVerdictSomebodyElseReachedWalksStraightPastTheseMarks()
        => Assert.Empty(
            LibraryQualityRules.WhatDecidesAStandingIn(
                """new RecordingDropsResponder(quality.Overall, quality.Scrambled, recording.Counters.Measured);"""));

    [Fact]
    public void APageSizeIsNotAShareARecordingIsJudgedAgainst()
        => Assert.Empty(LibraryQualityRules.SharesIn("""public const int MostPerPage = 200;"""));

    [Fact]
    public void ARowBuiltFromTheVerdictAloneLeavesEveryRuleEmpty()
    {
        using SourceTree tree = new();

        Assert.Empty(LibraryQualityRules.FilesMissingFromTheRowPath(tree.Root));
        Assert.Empty(LibraryQualityRules.WhatDecidesAStandingWhereARowIsBuilt(tree.Root));
        Assert.Empty(LibraryQualityRules.SharesWhereARowIsBuilt(tree.Root));
        Assert.Empty(LibraryQualityRules.WhatStoresAStandingOnTheRecordingTable(tree.Root));
        Assert.NotEmpty(LibraryQualityRules.ComputedColumnsOnTheRecordingTable(tree.Root));
        Assert.Equal(LibraryQualityRules.TheFourLevelsTheLibraryReads, LibraryQualityRules.TheLevelsTheLibraryReads(tree.Root));
    }

    [Fact]
    public void AStandingDecidedWhereARowIsBuiltIsCaughtByTheWholeRule()
    {
        using SourceTree tree = new();
        tree.Write(WhereARowIsBuilt, """QualityLevel level = Of(ThresholdEvaluator.Judge(share, band));""");

        Assert.Equal(
            [$"{WhereARowIsBuilt} ThresholdEvaluator"],
            LibraryQualityRules.WhatDecidesAStandingWhereARowIsBuilt(tree.Root));
    }

    [Fact]
    public void AShareWrittenWhereARowIsAskedForIsCaughtByTheWholeRule()
    {
        using SourceTree tree = new();
        tree.Write(WhereARowIsAskedFor, """if (share > 0.0002) { }""");

        Assert.Equal([$"{WhereARowIsAskedFor} 0.0002"], LibraryQualityRules.SharesWhereARowIsBuilt(tree.Root));
    }

    [Fact]
    public void AStandingKeptOnTheRecordingTableIsCaught()
    {
        using SourceTree tree = new();
        tree.Write(
            LibraryQualityRules.WhereTheRecordingTableIsLaidOut,
            """builder.Property(recording => recording.Quality).HasColumnName("quality_level");""");

        Assert.NotEmpty(LibraryQualityRules.WhatStoresAStandingOnTheRecordingTable(tree.Root));
    }

    [Fact]
    public void AFifthLevelReachingTheLibraryIsCaught()
    {
        using SourceTree tree = new();
        tree.Write(
            LibraryQualityRules.WhereTheVerdictIsAskedFor,
            """
            public enum QualityLevel
            {
                Good = 1,

                Unmeasured = 2,

                Warning = 3,

                MayNotBeWatchable = 4,

                Unreachable = 5,
            }
            """);

        Assert.NotEqual(
            LibraryQualityRules.TheFourLevelsTheLibraryReads,
            LibraryQualityRules.TheLevelsTheLibraryReads(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private const string TheLevelsAndTheOneAsking =
            """
            public enum QualityLevel
            {
                Good = 1,

                Unmeasured = 2,

                Warning = 3,

                MayNotBeWatchable = 4,
            }

            public sealed record RecordingQuality
            {
                private static QualityLevel Read(long counted, long total, ThresholdBand band)
                    => Of(ThresholdEvaluator.Judge((double)counted / total, band).Standing);
            }
            """;

        private const string TheTableWithoutAStandingOnIt =
            """
            builder.Property(recording => recording.Searchable).HasComputedColumnSql(SearchableSql, stored: true);
            builder.Property(recording => recording.CcDroppedPackets).HasColumnName("cc_dropped_packets");
            """;

        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-library-quality-");

        public SourceTree()
        {
            foreach (string relative in LibraryQualityRules.WhatBuildsOrFiltersARow)
            {
                Write(relative, """new RecordingDropsResponder(quality.Overall, quality.Scrambled);""");
            }

            Write(LibraryQualityRules.WhereTheVerdictIsAskedFor, TheLevelsAndTheOneAsking);
            Write(LibraryQualityRules.WhereTheRecordingTableIsLaidOut, TheTableWithoutAStandingOnIt);
        }

        public string Root => directory.FullName;

        public void Write(string relative, string source)
        {
            string full = Path.Combine(Root, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, source);
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
