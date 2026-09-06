namespace Carina.Architecture.Tests;

public sealed class EncodeLedgerRuleSelfCheckTests
{
    public static TheoryData<string, string> EachWayOfReachingIntoTheLedger() =>
        new()
        {
            { "the job entity", "private static object? Read(EncodeJob job) => job;" },
            { "the ledger's own repository", "private IEncodeJobRepository Jobs { get; init; } = null!;" },
            { "the table behind it", "private const string Sql = \"JOIN encode_job ON encode_job.recording_id = r.id\";" },
            { "the profile table", "private const string Sql = \"SELECT * FROM encode_profile\";" },
            { "the artefact's file name", "private static object? Name() => EncodeFileName.ArtefactExtension;" },
            { "the work file ledger", "private static object? Owed(EncodeScratchFile file) => file;" },
        };

    [Theory]
    [MemberData(nameof(EachWayOfReachingIntoTheLedger))]
    public void EveryWayOfReadingTheLedgerHereIsCaughtWhereverTheFileSits(string how, string writes)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-ledger-");

        try
        {
            Write(directory, "Carina.Domain/Library/Reader.cs", Source("Carina.Domain.Library", writes));

            Assert.NotEmpty(EncodeLedgerRules.TheEncodeLedgerReachedIntoFromTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }

        Assert.NotEmpty(EncodeLedgerRules.ReachesIn(writes));
        Assert.False(string.IsNullOrWhiteSpace(how));
    }

    public static TheoryData<string> WhatTheEncodeDomainPublishesForOtherDomainsToAsk() =>
        new()
        {
            "private IEncodeStandingReader Encoding { get; init; } = null!;",
            "private static object? Board(EncodeStandingBoard board) => board;",
            "private static object? Where(EncodeStanding standing) => standing;",
        };

    [Theory]
    [MemberData(nameof(WhatTheEncodeDomainPublishesForOtherDomainsToAsk))]
    public void AskingTheEncodeDomainThroughWhatItPublishesWalksPastThisRule(string writes)
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-door-");

        try
        {
            Write(directory, "Carina.Domain/Library/Reader.cs", Source("Carina.Domain.Library", writes));

            Assert.Empty(EncodeLedgerRules.TheEncodeLedgerReachedIntoFromTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void AFileThatSitsOutsideTheFolderIsStillLibraryCodeWhenItDeclaresTheNamespace()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-space-");

        try
        {
            Write(
                directory,
                "Carina.Infrastructure/Persistence/Reader.cs",
                Source("Carina.Infrastructure.Library", "private static object? Read(EncodeJob job) => job;"));

            Assert.Equal(
                ["/Carina.Infrastructure/Persistence/Reader.cs EncodeJob"],
                EncodeLedgerRules.TheEncodeLedgerReachedIntoFromTheLibraryFeature(directory.FullName));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void TheLedgerReadWhereTheEncodeDomainOwnsItWalksPastThisRule()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-encode-elsewhere-");

        try
        {
            Write(
                directory,
                "Carina.Infrastructure/Persistence/Repositories/EncodeJobRepository.cs",
                Source("Carina.Infrastructure.Persistence.Repositories", "private static object? Read(EncodeJob job) => job;"));

            Assert.Empty(EncodeLedgerRules.TheEncodeLedgerReachedIntoFromTheLibraryFeature(directory.FullName));
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
