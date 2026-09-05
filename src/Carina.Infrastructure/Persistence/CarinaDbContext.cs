using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence;

public class CarinaDbContext(DbContextOptions<CarinaDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasSequence<long>(ProgrammeRevisions.Sequence).StartsAt(1).IncrementsBy(1);
        BroadcastText.Declare(modelBuilder);
        StoredOrder.Declare(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CarinaDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }
}
