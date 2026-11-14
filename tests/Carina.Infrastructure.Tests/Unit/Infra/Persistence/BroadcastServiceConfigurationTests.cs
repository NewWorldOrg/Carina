using Carina.Domain.Channels;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using static Carina.Infrastructure.Tests.Fixtures.Persistence.CarinaModel;

namespace Carina.Infrastructure.Tests.Unit.Infra.Persistence;

[Trait("Category", "Unit")]
public sealed class BroadcastServiceConfigurationTests
{
    [Fact]
    public void AServiceIsKeyedByItsBroadcastIdentifiersAlone()
    {
        using CarinaDbContext context = Context();

        IKey key = Entity<BroadcastService>(context).FindPrimaryKey()!;

        Assert.Equal(
            ["network_id", "service_id"],
            key.Properties.Select(property => property.GetColumnName()));
    }
}
