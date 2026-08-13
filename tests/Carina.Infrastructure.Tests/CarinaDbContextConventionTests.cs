using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

internal sealed class ConventionProbe
{
    public int Id { get; set; }
    public DateTime RecordedAt { get; set; }
}

public sealed class CarinaDbContextConventionTests
{
    private sealed class ProbeDbContext(DbContextOptions<CarinaDbContext> options) : CarinaDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ConventionProbe>();
        }
    }

    private static ProbeDbContext Probe()
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase("Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");

        return new ProbeDbContext(builder.Options);
    }

    [Fact]
    public void NamesTablesAndColumnsInSnakeCase()
    {
        using var context = Probe();

        var entity = context.Model.FindEntityType(typeof(ConventionProbe))!;

        Assert.Equal("convention_probe", entity.GetTableName());
        Assert.Equal("recorded_at", entity.FindProperty(nameof(ConventionProbe.RecordedAt))!.GetColumnName());
    }

    [Fact]
    public void RoutesEveryDateTimeThroughTheUtcConverter()
    {
        using var context = Probe();

        var property = context.Model.FindEntityType(typeof(ConventionProbe))!
            .FindProperty(nameof(ConventionProbe.RecordedAt))!;

        Assert.IsType<UtcDateTimeConverter>(property.GetValueConverter());
    }
}
