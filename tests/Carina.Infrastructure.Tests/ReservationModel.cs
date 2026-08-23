using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests;

internal static class ReservationModel
{
    private const string Placeholder = "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder";

    public static DbContextOptions<CarinaDbContext> Options()
    {
        var builder = new DbContextOptionsBuilder<CarinaDbContext>();
        builder.UseCarinaDatabase(Placeholder);

        return builder.Options;
    }

    public static CarinaDbContext Carina() => new(Options());
}
