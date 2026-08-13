using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

public sealed class PersistenceBoundaryRuleTests
{
    [Fact]
    public void TheCarinaModelDeclaresNoBoundaryBreakingForeignKeys()
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase("Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder");
        using var context = new CarinaDbContext(builder.Options);

        Assert.Empty(PersistenceBoundaryRules.BoundaryBreakingForeignKeys(context.Model));
    }
}
