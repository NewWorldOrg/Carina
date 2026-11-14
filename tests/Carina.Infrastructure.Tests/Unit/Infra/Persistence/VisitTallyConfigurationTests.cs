using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using static Carina.Infrastructure.Tests.Fixtures.Persistence.CarinaModel;

namespace Carina.Infrastructure.Tests.Unit.Infra.Persistence;

[Trait("Category", "Unit")]
public sealed class VisitTallyConfigurationTests
{
    [Fact]
    public void ATallyIsKeyedByTheStreamTheServiceAndTheTable()
    {
        using CarinaDbContext context = Context();

        IKey key = Entity<VisitTally>(context).FindPrimaryKey()!;

        Assert.Equal(
            ["network_id", "transport_stream_id", "service_id", "table_id"],
            key.Properties.Select(property => property.GetColumnName()));
    }
}
