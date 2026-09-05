using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence;

public static class StoredOrder
{
    public static int Between(Guid left, Guid right)
        => throw new NotSupportedException(
            "Putting two identifiers in order is the store's own work, and this stands here only to be translated into a query.");

    internal static void Declare(ModelBuilder modelBuilder)
        => modelBuilder
            .HasDbFunction(() => Between(default, default))
            .HasName("uuid_cmp")
            .HasSchema("pg_catalog");
}
