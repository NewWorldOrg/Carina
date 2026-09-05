namespace Carina.Architecture.Tests;

public sealed class QualityThresholdRuleSelfCheckTests
{
    public static TheoryData<string, string> EachWayOfWritingTheThresholdDown() =>
        new()
        {
            { "a share written out", "private const double Warning = 0.0002;" },
            { "a share in exponent form", "private const double Warning = 2e-4;" },
            { "a share with a suffix", "private const float Warning = 0.01f;" },
            { "the numbers the quality domain keeps", "private static readonly QualityShare Share = QualityShares.PacketsLost;" },
            { "the counters the share is worked out from", "private long Lost(Recording it) => it.CcDroppedPackets ?? 0;" },
            { "the columns those counters sit in", "private const string Sql = \"cc_dropped_packets / cc_total_packets\";" },
        };

    [Theory]
    [MemberData(nameof(EachWayOfWritingTheThresholdDown))]
    public void EveryWayOfDecidingQualityHereIsCaughtWhereverTheFileSits(string how, string writes)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-threshold-");

        try
        {
            Write(directory, "Carina.Domain/Library/Reader.cs", Source("Carina.Domain.Library", writes));

            Assert.NotEmpty(
                QualityThresholdRules.QualityNumbersInsideTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        Assert.NotEmpty(QualityThresholdRules.NumbersIn(writes));
        Assert.False(string.IsNullOrWhiteSpace(how));
    }

    [Fact]
    public void AFileThatSitsOutsideTheFolderIsStillLibraryCodeWhenItDeclaresTheNamespace()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-threshold-space-");

        try
        {
            Write(
                directory,
                "Carina.Infrastructure/Persistence/Reader.cs",
                Source("Carina.Infrastructure.Library", "private const double Warning = 0.0002;"));

            Assert.Equal(
                ["/Carina.Infrastructure/Persistence/Reader.cs 0.0002"],
                QualityThresholdRules.QualityNumbersInsideTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AFileThatMerelyBuildsTheSearchCriteriaIsLibraryCodeToo()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-threshold-criteria-");

        try
        {
            Write(
                directory,
                "Carina.Api/Services/RecordingsService.cs",
                Source(
                    "Carina.Api.Services",
                    $"private static object? Read() => {QualityThresholdRules.CriteriaType}.For(null, null, null);",
                    "private const double Warning = 0.0002;"));

            Assert.NotEmpty(QualityThresholdRules.QualityNumbersInsideTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AThresholdKeptWhereTheQualityDomainOwnsItWalksPastThisRule()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-threshold-elsewhere-");

        try
        {
            Write(
                directory,
                "Carina.Domain/Recordings/RecordingQuality.cs",
                Source("Carina.Domain.Recordings", "private const double Warning = 0.0002;"));

            Assert.Empty(QualityThresholdRules.QualityNumbersInsideTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    public static TheoryData<string> NumbersThatDecideNothingAboutQuality() =>
        new()
        {
            "private const int MostPerPage = 200;",
            "private const int LongestKeyword = 100;",
            "private static readonly TimeSpan Longest = TimeSpan.FromDays(366);",
            "private const string Version = \"10.0.10\";",
            "private const long Bytes = 1_000_000;",
        };

    [Theory]
    [MemberData(nameof(NumbersThatDecideNothingAboutQuality))]
    public void APlainWholeNumberIsNotAThreshold(string writes)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-threshold-plain-");

        try
        {
            Write(directory, "Carina.Domain/Library/Reader.cs", Source("Carina.Domain.Library", writes));

            Assert.Empty(QualityThresholdRules.QualityNumbersInsideTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string Source(string space, params string[] lines)
        => $"namespace {space};\n\npublic sealed class Reader\n{{\n    "
            + string.Join("\n    ", lines)
            + "\n}\n";

    private static void Write(DirectoryInfo directory, string relative, string source)
    {
        string path = Path.Combine(directory.FullName, relative);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, source);
    }
}
