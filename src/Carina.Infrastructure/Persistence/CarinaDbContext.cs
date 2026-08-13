using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence;

public class CarinaDbContext(DbContextOptions<CarinaDbContext> options) : DbContext(options)
{
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
}
