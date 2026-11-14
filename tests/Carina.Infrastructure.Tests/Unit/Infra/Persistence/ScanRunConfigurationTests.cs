using Carina.Domain.Scans;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using static Carina.Infrastructure.Tests.Fixtures.Persistence.CarinaModel;

namespace Carina.Infrastructure.Tests.Unit.Infra.Persistence;

[Trait("Category", "Unit")]
public sealed class ScanRunConfigurationTests
{
    [Fact]
    public void OnlyOneScanCanBeRunning()
    {
        using CarinaDbContext context = Context();

        IIndex index = Assert.Single(
            Entity<ScanRun>(context).GetIndexes(),
            run => run.GetDatabaseName() == "ux_scan_run_running");

        Assert.True(index.IsUnique);
        Assert.Equal("state = 'Running'", index.GetFilter());
    }

    [Fact]
    public void AFailedOrCancelledScanCarriesAReasonTheDatabaseInsistsOn()
    {
        using CarinaDbContext context = Context();

        IEnumerable<string?> names = Entity<ScanRun>(context).GetCheckConstraints().Select(constraint => constraint.Name);

        Assert.Contains("ck_scan_run_reason", names);
    }
}
