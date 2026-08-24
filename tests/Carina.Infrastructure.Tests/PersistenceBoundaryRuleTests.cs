using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

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
