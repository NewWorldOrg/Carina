using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using static Carina.Infrastructure.Tests.Fixtures.Persistence.CarinaModel;

namespace Carina.Infrastructure.Tests.Unit.Infra.Persistence;

[Trait("Category", "Unit")]
public sealed class StreamVisitConfigurationTests
{
    [Fact]
    public void EveryWayAVisitCanEndIsAValueTheDatabaseKnows()
    {
        using CarinaDbContext context = Context();

        ICheckConstraint check = Assert.Single(
            Entity<StreamVisit>(context).GetCheckConstraints(),
            constraint => constraint.Name == "ck_stream_visit_outcome");

        foreach (string outcome in Enum.GetNames<VisitOutcome>())
        {
            Assert.Contains($"'{outcome}'", check.Sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ATallyGoesWithTheVisitItCounted()
    {
        using CarinaDbContext context = Context();

        IForeignKey foreignKey = Assert.Single(Entity<VisitTally>(context).GetForeignKeys());

        Assert.Equal("stream_visit", foreignKey.PrincipalEntityType.GetTableName());
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }
}
