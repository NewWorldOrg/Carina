using Carina.Domain.Channels;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using static Carina.Infrastructure.Tests.Fixtures.Persistence.CarinaModel;

namespace Carina.Infrastructure.Tests.Unit.Infra.Persistence;

[Trait("Category", "Unit")]
public sealed class CandidateChannelConfigurationTests
{
    [Fact(DisplayName = "a candidate keeps its latest score as one reading, not a history of them")]
    public void ACandidateKeepsItsLatestScoreAsOneReading()
    {
        using CarinaDbContext context = Context();
        IEntityType candidate = Entity<CandidateChannel>(context);

        Assert.Equal(
            [
                "score_bit_error_rate_highest",
                "score_cnr_lowest_milli_decibels",
                "score_evaluated_at",
                "score_lock_rate",
                "score_measured_from",
                "score_measured_until",
                "score_samples",
            ],
            ColumnsOf(candidate, nameof(CandidateChannel.Score)).Order(StringComparer.Ordinal));
        Assert.Contains("ck_candidate_channel_score", candidate.GetCheckConstraints().Select(check => check.Name));
    }

    [Fact]
    public void ACandidateChannelPointsAtItsServiceAndNothingPointsBack()
    {
        using CarinaDbContext context = Context();

        IForeignKey foreignKey = Assert.Single(Entity<CandidateChannel>(context).GetForeignKeys());

        Assert.Equal("broadcast_service", foreignKey.PrincipalEntityType.GetTableName());
        Assert.Equal(
            ["network_id", "service_id"],
            foreignKey.Properties.Select(property => property.GetColumnName()));
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void OnlyOneCandidatePerServiceCanBeSelected()
    {
        using CarinaDbContext context = Context();

        IIndex index = Assert.Single(
            Entity<CandidateChannel>(context).GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ux_candidate_channel_selected");

        Assert.True(index.IsUnique);
        Assert.Equal("is_selected", index.GetFilter());
        Assert.Equal(
            ["network_id", "service_id"],
            index.Properties.Select(property => property.GetColumnName()));
    }

    [Fact]
    public void ADefinitionThatCannotBeReceivedIsRefusedByTheDatabaseToo()
    {
        using CarinaDbContext context = Context();

        IEnumerable<string?> names = Entity<CandidateChannel>(context).GetCheckConstraints().Select(constraint => constraint.Name);

        Assert.Contains("ck_candidate_channel_tuning", names);
    }

    [Fact]
    public void SelectionCarriesItsSourceAndTheReadingTakenAtTheTime()
    {
        using CarinaDbContext context = Context();

        Assert.Contains(
            "selected_cnr_milli_decibels",
            ColumnsOf(Entity<CandidateChannel>(context), nameof(CandidateChannel.SelectionMeasurement)));
        Assert.Contains("selection_source", Entity<CandidateChannel>(context)
            .GetProperties()
            .Select(property => property.GetColumnName()));
    }

    [Fact]
    public void LeavingRotationIsAColumnRatherThanSomethingToInferFromLogs()
    {
        using CarinaDbContext context = Context();

        string[] columns = Entity<CandidateChannel>(context)
            .GetProperties()
            .Select(property => property.GetColumnName())
            .ToArray();

        Assert.Contains("rotation_state", columns);
        Assert.Contains("consecutive_failures", columns);
        Assert.Contains("next_attempt_at", columns);
        Assert.Contains("needs_attention_since", columns);
        Assert.Contains("needs_revalidation", columns);
    }
}
