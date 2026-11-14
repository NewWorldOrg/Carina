using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using static Carina.Infrastructure.Tests.Fixtures.Persistence.CarinaModel;

namespace Carina.Infrastructure.Tests.Unit.Infra.Persistence;

[Trait("Category", "Unit")]
public sealed class ProgrammeConfigurationTests
{
    [Fact]
    public void EveryTableAProgrammeCanComeFromIsAValueTheDatabaseKnows()
    {
        using CarinaDbContext context = Context();

        ICheckConstraint check = Assert.Single(
            Entity<Programme>(context).GetCheckConstraints(),
            constraint => constraint.Name == "ck_programme_source");

        foreach (string source in Enum.GetNames<ProgrammeSource>())
        {
            Assert.Contains($"'{source}'", check.Sql, StringComparison.Ordinal);
        }
    }
}
