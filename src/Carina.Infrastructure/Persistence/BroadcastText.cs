using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence;

public static class BroadcastText
{
    public const string Compatibility = ProgrammeSearchText.Compatibility;

    public static string Normalised(string text, string form)
        => throw new NotSupportedException(
            "Normalising is the store's own work and stands here only to be translated into a query.");

    internal static void Declare(ModelBuilder modelBuilder)
        => modelBuilder
            .HasDbFunction(() => Normalised(default!, default!))
            .HasName("normalize")
            .HasSchema("pg_catalog");
}
