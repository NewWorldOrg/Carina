namespace Carina.Architecture.Tests;

public sealed class ReservationRuleSelfCheckTests
{
    private const string ClaimingUpdate = """
        const string Sql = "UPDATE reservation SET started_at = @at WHERE id = @id AND started_at IS NULL";
        """;

    private const string OutcomeUpdate = """
        const string Sql = "UPDATE reservation SET recording_outcome = @outcome WHERE id = @id";
        """;

    private const string ReadingSelect = """
        const string Sql = "SELECT id, started_at, recording_outcome FROM reservation WHERE started_at IS NULL";
        """;

    [Fact]
    public void DetectsAReservationServiceThatWritesTheClaimItself()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Reservations/ReservationService.cs", ClaimingUpdate);
        tree.Write("Carina.Infrastructure/Reservations/ReservationReader.cs", ReadingSelect);

        Assert.Equal(
            ["/Carina.Infrastructure/Reservations/ReservationService.cs"],
            ReservationRules.WritersOfWhatRecordingOwns(tree.Root));
    }

    [Fact]
    public void DetectsAnApiActionThatWritesTheOutcomeThroughTheChangeTracker()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Api/Reservations/AcknowledgeReservationAction.cs",
            "await reservations.ExecuteUpdateAsync(set => set.SetProperty(entity => entity.RecordingOutcome, done));");

        Assert.Equal(
            ["/Carina.Api/Reservations/AcknowledgeReservationAction.cs"],
            ReservationRules.WritersOfWhatRecordingOwns(tree.Root));
    }

    [Fact]
    public void LeavesRecordingAndMigrationAloneBecauseThoseTwoAreTheWriters()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Infrastructure/Recordings/RecordingClaim.cs", ClaimingUpdate);
        tree.Write("Carina.Infrastructure/Migration/ImportedRecordings.cs", OutcomeUpdate);
        tree.Write(
            "Carina.Infrastructure/Persistence/Repositories/ReservationRecordingContract.cs",
            ClaimingUpdate);

        Assert.Empty(ReservationRules.WritersOfWhatRecordingOwns(tree.Root));
    }

    [Fact]
    public void ReadingWhatRecordingOwnsIsNotWritingIt()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Reservations/ListReservationsAction.cs", ReadingSelect);
        tree.Write(
            "Carina.Infrastructure/Persistence/Configurations/ReservationConfiguration.cs",
            "builder.Property(reservation => reservation.StartedAt).HasColumnName(\"started_at\");");

        Assert.Empty(ReservationRules.WritersOfWhatRecordingOwns(tree.Root));
    }

    [Fact]
    public void DetectsARuleThatJudgesProgrammesForItself()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Rules/RuleMatcher.cs",
            "private static bool Matches(Programme programme, RuleQuery query) => programme.Name.Contains(query.Value);");
        tree.Write(
            "Carina.Infrastructure/Reservations/ReservationPlanner.cs",
            "private static Func<Programme, bool> Wanted(RuleQuery query) => programme => true;");

        Assert.Equal(
            [
                "/Carina.Infrastructure/Reservations/ReservationPlanner.cs",
                "/Carina.Infrastructure/Rules/RuleMatcher.cs",
            ],
            ReservationRules.ProgrammeMatchersOutsideTheGuide(tree.Root));
    }

    [Fact]
    public void LeavesTheGuideOwningItsOwnPredicate()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Programmes/ProgrammeSearchRepository.cs",
            "private static bool Matches(Programme programme, ProgrammeSearch search) => true;");
        tree.Write(
            "Carina.Infrastructure/Rules/RuleApplication.cs",
            "IReadOnlyList<ProgrammeMatch> hits = await search.SearchAsync(asked, cancellationToken);");

        Assert.Empty(ReservationRules.ProgrammeMatchersOutsideTheGuide(tree.Root));
    }

    [Fact]
    public void ReadsTheRepositoryOnDisk()
    {
        Assert.NotEmpty(Directory.EnumerateFiles(
            Path.Combine(RepositoryLayout.SourceDirectory, "Carina.Domain", "Reservations"),
            "*.cs"));
    }

    [Fact]
    public void DetectsTheClaimWrittenByAFileThatOnlyBorrowsTheContractsName()
    {
        using var tree = new SourceTree();
        tree.Write("Carina.Api/Reservations/ReservationRecordingContract.cs", ClaimingUpdate);

        Assert.Equal(
            ["/Carina.Api/Reservations/ReservationRecordingContract.cs"],
            ReservationRules.WritersOfWhatRecordingOwns(tree.Root));
    }

    [Fact]
    public void DetectsAMatcherInReservationCodeThatSitsOutsideTheFeatureFolders()
    {
        using var tree = new SourceTree();
        tree.Write(
            "Carina.Infrastructure/Persistence/Repositories/ReservationPlanRepository.cs",
            """
            using Carina.Domain.Reservations;
            internal static class ReservationPlanRepository
            {
                private static bool Matches(Programme programme, RuleQuery query) => true;
            }
            """);

        Assert.Equal(
            ["/Carina.Infrastructure/Persistence/Repositories/ReservationPlanRepository.cs"],
            ReservationRules.ProgrammeMatchersOutsideTheGuide(tree.Root));
    }

    private sealed class SourceTree : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("carina-reservation-rules-");

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
