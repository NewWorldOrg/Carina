using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Carina.Infrastructure.Tests;

public static class PersistenceBoundaryRules
{
    private static readonly string[] ReservationPrefixes = ["reservation"];
    private static readonly string[] ChannelDefinitionPrefixes = ["channel"];
    private static readonly string[] ProgrammeCachePrefixes = ["programme", "epg"];

    public static IReadOnlyList<string> BoundaryBreakingForeignKeys(IModel model)
        => model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(BreaksABoundary)
            .Select(foreignKey =>
                $"{TableName(foreignKey.DeclaringEntityType)} -> {TableName(foreignKey.PrincipalEntityType)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool BreaksABoundary(IForeignKey foreignKey)
    {
        var declaring = TableName(foreignKey.DeclaringEntityType);
        var principal = TableName(foreignKey.PrincipalEntityType);

        if (Matches(declaring, ReservationPrefixes) && Matches(principal, ChannelDefinitionPrefixes))
        {
            return true;
        }

        return Matches(principal, ProgrammeCachePrefixes) && !Matches(declaring, ProgrammeCachePrefixes);
    }

    private static string TableName(IEntityType entityType)
        => entityType.GetTableName() ?? entityType.ShortName();

    private static bool Matches(string tableName, string[] prefixes)
        => prefixes.Any(prefix => tableName.StartsWith(prefix, StringComparison.Ordinal));
}
