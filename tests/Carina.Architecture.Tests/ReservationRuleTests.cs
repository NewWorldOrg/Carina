namespace Carina.Architecture.Tests;

public sealed class ReservationRuleTests
{
    [Fact]
    public void NothingOutsideRecordingAndMigrationWritesTheClaimOrTheOutcome()
    {
        Assert.Empty(ReservationRules.WritersOfWhatRecordingOwns(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheReservationFeatureKeepsNoProgrammeMatcherOfItsOwn()
    {
        Assert.Empty(ReservationRules.ProgrammeMatchersOutsideTheGuide(RepositoryLayout.SourceDirectory));
    }

    [Fact]
    public void TheReservationFeatureIsOnDiskForThoseRulesToRead()
    {
        IReadOnlyList<string> feature = SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            "namespace Carina.Domain.Reservations;");

        Assert.Contains("Carina.Domain/Reservations/Reservation.cs", feature, StringComparer.Ordinal);
        Assert.NotEmpty(SourceScan.FilesMentioning(
            RepositoryLayout.SourceDirectory,
            "namespace Carina.Domain.Rules;"));
    }

    [Fact]
    public void TheColumnsRecordingOwnsAreDeclaredWhereTheReservationIsMapped()
    {
        IReadOnlyList<string> declaring = SourceScan.FilesMentioningAll(
            RepositoryLayout.SourceDirectory,
            [.. ReservationRules.RecordingOwnedColumns]);

        Assert.Contains(
            "Carina.Infrastructure/Persistence/Configurations/ReservationConfiguration.cs",
            declaring,
            StringComparer.Ordinal);
    }
}
