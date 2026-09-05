using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Carina.Infrastructure.Tests;

public sealed class PersistenceBoundaryRuleTests
{
    private static CarinaDbContext Carina()
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase("Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");

        return new CarinaDbContext(builder.Options);
    }

    [Fact]
    public void TheCarinaModelDeclaresNoBoundaryBreakingForeignKeys()
    {
        using CarinaDbContext context = Carina();

        Assert.Empty(PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }

    [Fact]
    public void EveryEntityInTheCarinaModelDeclaresWhichFamilyItBelongsTo()
    {
        using CarinaDbContext context = Carina();

        Assert.Empty(PersistenceBoundaryRules.UnclassifiedEntityTypes(context.Model));
    }

    [Fact]
    public void BothSidesOfTheReservationBoundaryAreInTheModelForTheRuleToWeigh()
    {
        using CarinaDbContext context = Carina();

        Assert.Equal(
            ["reservation", "reservation_outcome", "rule"],
            PersistenceBoundaryRules.TablesOf(context.Model, PersistenceFamily.Reservations));

        Assert.NotEmpty(PersistenceBoundaryRules.TablesOf(context.Model, PersistenceFamily.ChannelDefinitions));
        Assert.NotEmpty(PersistenceBoundaryRules.TablesOf(context.Model, PersistenceFamily.ProgrammeCache));
    }

    [Fact]
    public void TheLedgerIsInTheModelForThoseRulesToWeigh()
    {
        using CarinaDbContext context = Carina();

        Assert.Equal(
            ["recording"],
            PersistenceBoundaryRules.TablesOf(context.Model, PersistenceFamily.Recordings));
    }

    [Fact]
    public void WhatTheLedgerCheckWritesIsItsOwnFamilyAndHoldsNoKeyIntoTheLedger()
    {
        using CarinaDbContext context = Carina();

        Assert.Equal(
            ["integrity_check", "integrity_finding"],
            PersistenceBoundaryRules.TablesOf(context.Model, PersistenceFamily.Integrity));

        IReadOnlyList<string> pointing = [.. context.Model
            .GetEntityTypes()
            .Single(entityType => entityType.GetTableName() == "integrity_finding")
            .GetForeignKeys()
            .Select(key => key.PrincipalEntityType.GetTableName() ?? string.Empty)];

        Assert.Equal(["integrity_check"], pointing);
    }

    [Fact(DisplayName = "BR-D-004: the encode ledger is four tables, and its foreign keys never leave it")]
    public void TheEncodeLedgerIsFourTablesAndItsForeignKeysNeverLeaveIt()
    {
        using CarinaDbContext context = Carina();

        Assert.Equal(
            ["encode_destination", "encode_job", "encode_profile", "encode_scratch_file"],
            PersistenceBoundaryRules.TablesOf(context.Model, PersistenceFamily.Encodings));

        IReadOnlyList<string> pointing = [.. context.Model
            .GetEntityTypes()
            .Where(entityType => entityType.GetTableName() is { } table && table.StartsWith("encode_", StringComparison.Ordinal))
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Select(key => $"{key.DeclaringEntityType.GetTableName()} -> {key.PrincipalEntityType.GetTableName()}")
            .Order(StringComparer.Ordinal)];

        Assert.Equal(
            [
                "encode_destination -> encode_profile",
                "encode_job -> encode_destination",
                "encode_job -> encode_profile",
                "encode_scratch_file -> encode_job",
            ],
            pointing);
    }

    [Fact(DisplayName = "BR-D-004: an encode job reaches the recording it encodes by value, not by key")]
    public void AnEncodeJobReachesTheRecordingItEncodesByValue()
    {
        using CarinaDbContext context = Carina();

        IEntityType job = context.Model.GetEntityTypes().Single(entityType => entityType.GetTableName() == "encode_job");

        Assert.Contains("recording_id", job.GetProperties().Select(property => property.GetColumnName()), StringComparer.Ordinal);
        Assert.DoesNotContain(job.GetForeignKeys(), key => key.PrincipalEntityType.GetTableName() == "recording");
    }

    [Fact]
    public void ARecordingStillReachesTheServiceAndTheProgrammeItRecordedByValue()
    {
        using CarinaDbContext context = Carina();

        IReadOnlyList<string> columns = [.. context.Model
            .GetEntityTypes()
            .Single(entityType => entityType.GetTableName() == "recording")
            .GetProperties()
            .Select(property => property.GetColumnName())];

        Assert.Contains("network_id", columns, StringComparer.Ordinal);
        Assert.Contains("service_id", columns, StringComparer.Ordinal);
        Assert.Contains("event_id", columns, StringComparer.Ordinal);
        Assert.Contains("programme_start_at", columns, StringComparer.Ordinal);
        Assert.Contains("reservation_id", columns, StringComparer.Ordinal);
    }

    [Fact]
    public void AReservationStillReachesTheServiceItRecordsByValue()
    {
        using CarinaDbContext context = Carina();

        IReadOnlyList<string> columns = [.. context.Model
            .GetEntityTypes()
            .Single(entityType => entityType.GetTableName() == "reservation")
            .GetProperties()
            .Select(property => property.GetColumnName())];

        Assert.Contains("network_id", columns, StringComparer.Ordinal);
        Assert.Contains("service_id", columns, StringComparer.Ordinal);
        Assert.Contains("event_id", columns, StringComparer.Ordinal);
        Assert.Contains("programme_start_at", columns, StringComparer.Ordinal);
    }
}
