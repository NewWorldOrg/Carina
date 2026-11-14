using Carina.Domain.Scans;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using static Carina.Infrastructure.Tests.Fixtures.Persistence.CarinaModel;

namespace Carina.Infrastructure.Tests.Unit.Infra.Persistence;

[Trait("Category", "Unit")]
public sealed class ScanRunAttemptConfigurationTests
{
    [Fact]
    public void AnAttemptCarriesTheTuningItUsedRatherThanTheCandidateItCameFrom()
    {
        using CarinaDbContext context = Context();

        IEntityType attempt = Entity<ScanRunAttempt>(context);

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
        using CarinaDbContext context = Context();

        ICheckConstraint check = Assert.Single(
            Entity<ScanRunAttempt>(context).GetCheckConstraints(),
            constraint => constraint.Name == "ck_scan_run_attempt_outcome");

        foreach (string outcome in Enum.GetNames<ScanAttemptOutcome>())
        {
            Assert.Contains($"'{outcome}'", check.Sql, StringComparison.Ordinal);
        }
    }
}
