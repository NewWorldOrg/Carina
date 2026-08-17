using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Carina.Infrastructure.Tests;

public sealed class ChannelSchemaTests
{
    private static CarinaDbContext Carina()
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase("Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");

        return new CarinaDbContext(builder.Options);
    }

    private static IModel Schema(CarinaDbContext context)
        => context.GetService<IDesignTimeModel>().Model;

    private static IEntityType Entity<TEntity>(CarinaDbContext context)
        => Schema(context).FindEntityType(typeof(TEntity))!;

    // Tuning and the signal readings are complex properties rather than owned entities:
    // they carry no identity, so two rows may hold the very same instance.
    private static IEnumerable<string> ColumnsOf(IEntityType entity, string valueObject)
        => entity.GetComplexProperties()
            .Single(property => property.Name == valueObject)
            .ComplexType.GetProperties()
            .Select(property => property.GetColumnName());

    private static IEnumerable<IProperty> EveryProperty(ITypeBase type)
        => [
            .. type.GetProperties(),
            .. type.GetComplexProperties().SelectMany(property => EveryProperty(property.ComplexType)),
        ];

    [Fact]
    public void AServiceIsKeyedByItsBroadcastIdentifiersAlone()
    {
        using var context = Carina();

        var key = Entity<BroadcastService>(context).FindPrimaryKey()!;

        Assert.Equal(
            ["network_id", "service_id"],
            key.Properties.Select(property => property.GetColumnName()));
    }

    [Fact]
    public void ACandidateChannelPointsAtItsServiceAndNothingPointsBack()
    {
        using var context = Carina();

        var foreignKey = Assert.Single(Entity<CandidateChannel>(context).GetForeignKeys());

        Assert.Equal("broadcast_service", foreignKey.PrincipalEntityType.GetTableName());
        Assert.Equal(
            ["network_id", "service_id"],
            foreignKey.Properties.Select(property => property.GetColumnName()));
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void OnlyOneCandidatePerServiceCanBeSelected()
    {
        using var context = Carina();

        var index = Assert.Single(
            Entity<CandidateChannel>(context).GetIndexes(),
            candidate => candidate.GetDatabaseName() == "ux_candidate_channel_selected");

        Assert.True(index.IsUnique);
        Assert.Equal("is_selected", index.GetFilter());
        Assert.Equal(
            ["network_id", "service_id"],
            index.Properties.Select(property => property.GetColumnName()));
    }

    [Fact]
    public void OnlyOneScanCanBeRunning()
    {
        using var context = Carina();

        var index = Assert.Single(
            Entity<ScanRun>(context).GetIndexes(),
            run => run.GetDatabaseName() == "ux_scan_run_running");

        Assert.True(index.IsUnique);
        Assert.Equal("state = 'Running'", index.GetFilter());
    }

    [Fact]
    public void AnAttemptCarriesTheTuningItUsedRatherThanTheCandidateItCameFrom()
    {
        using var context = Carina();

        var attempt = Entity<ScanRunAttempt>(context);

        Assert.Equal(
            ["scan_run"],
            attempt.GetForeignKeys().Select(key => key.PrincipalEntityType.GetTableName()));
        Assert.Contains(
            ColumnsOf(attempt, nameof(ScanRunAttempt.Tuning)),
            column => column == "tune_system");
    }

    [Fact]
    public void TheFourWaysOfFailingAreValuesTheDatabaseKnows()
    {
        using var context = Carina();

        var check = Assert.Single(
            Entity<ScanRunAttempt>(context).GetCheckConstraints(),
            constraint => constraint.Name == "ck_scan_run_attempt_outcome");

        foreach (var outcome in Enum.GetNames<ScanAttemptOutcome>())
        {
            Assert.Contains($"'{outcome}'", check.Sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AFailedOrCancelledScanCarriesAReasonTheDatabaseInsistsOn()
    {
        using var context = Carina();

        var names = Entity<ScanRun>(context).GetCheckConstraints().Select(constraint => constraint.Name);

        Assert.Contains("ck_scan_run_reason", names);
    }

    [Fact]
    public void ADefinitionThatCannotBeReceivedIsRefusedByTheDatabaseToo()
    {
        using var context = Carina();

        var names = Entity<CandidateChannel>(context).GetCheckConstraints().Select(constraint => constraint.Name);

        Assert.Contains("ck_candidate_channel_tuning", names);
    }

    [Fact]
    public void SelectionCarriesItsSourceAndTheReadingTakenAtTheTime()
    {
        using var context = Carina();

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
        using var context = Carina();

        var columns = Entity<CandidateChannel>(context)
            .GetProperties()
            .Select(property => property.GetColumnName())
            .ToArray();

        Assert.Contains("rotation_state", columns);
        Assert.Contains("consecutive_failures", columns);
        Assert.Contains("next_attempt_at", columns);
        Assert.Contains("needs_attention_since", columns);
        Assert.Contains("needs_revalidation", columns);
    }

    [Fact]
    public void EveryTableOfThisDomainIsNamedInSnakeCase()
    {
        using var context = Carina();

        Assert.Equal(
            [
                "broadcast_service",
                "candidate_channel",
                "satellite_transport_stream",
                "scan_run",
                "scan_run_attempt",
            ],
            Schema(context).GetEntityTypes()
                .Where(entity => entity.FindOwnership() is null)
                .Select(entity => entity.GetTableName()!)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryTimeOfThisDomainGoesThroughTheUtcConverter()
    {
        using var context = Carina();

        var times = Schema(context).GetEntityTypes()
            .SelectMany(EveryProperty)
            .Where(property => property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
            .ToArray();

        Assert.NotEmpty(times);
        Assert.All(times, property => Assert.IsType<UtcDateTimeConverter>(property.GetValueConverter()));
    }
}
