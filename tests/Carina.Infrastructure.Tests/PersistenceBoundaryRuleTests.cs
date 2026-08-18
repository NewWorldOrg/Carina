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
}
