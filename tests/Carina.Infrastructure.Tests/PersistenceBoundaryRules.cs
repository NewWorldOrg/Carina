using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Carina.Infrastructure.Tests;

public enum PersistenceFamily
{
    Unrelated,
    Reservations,
    ChannelDefinitions,
    ProgrammeCache,
    Recordings,
    Integrity,
    Encodings,
}

public static class PersistenceBoundaryRules
{
    private static readonly Dictionary<string, PersistenceFamily> FamiliesByFeature =
        new(StringComparer.Ordinal)
        {
            ["Reservations"] = PersistenceFamily.Reservations,
            ["Rules"] = PersistenceFamily.Reservations,
            ["Channels"] = PersistenceFamily.ChannelDefinitions,
            ["Scans"] = PersistenceFamily.ChannelDefinitions,
            ["Programmes"] = PersistenceFamily.ProgrammeCache,
            ["Recordings"] = PersistenceFamily.Recordings,
            ["Integrity"] = PersistenceFamily.Integrity,
            ["Encodings"] = PersistenceFamily.Encodings,
            ["Auth"] = PersistenceFamily.Unrelated,
        };

    public static IReadOnlyList<string> BoundaryBreakingForeignKeys(IModel model)
        => model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetForeignKeys())
            .Where(BreaksABoundary)
            .Select(foreignKey =>
                $"{TableName(foreignKey.DeclaringEntityType)} -> {TableName(foreignKey.PrincipalEntityType)}")
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> UnclassifiedEntityTypes(IModel model)
        => model.GetEntityTypes()
            .Where(entityType => FamilyOf(entityType) is null)
            .Select(entityType => $"{EntityName(entityType)} ({TableName(entityType)})")
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> TablesOf(IModel model, PersistenceFamily family)
        => model.GetEntityTypes()
            .Where(entityType => FamilyOf(entityType) == family)
            .Select(TableName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool BreaksABoundary(IForeignKey foreignKey)
    {
        PersistenceFamily? declaring = FamilyOf(foreignKey.DeclaringEntityType);
        PersistenceFamily? principal = FamilyOf(foreignKey.PrincipalEntityType);

        if (declaring is PersistenceFamily.Reservations && principal is PersistenceFamily.ChannelDefinitions)
        {
            return true;
        }

        if (declaring is PersistenceFamily.ProgrammeCache && principal is PersistenceFamily.ChannelDefinitions)
        {
            return true;
        }

        if (declaring is PersistenceFamily.Recordings
            && principal is PersistenceFamily.ChannelDefinitions or PersistenceFamily.Reservations)
        {
            return true;
        }

        if (declaring is PersistenceFamily.Encodings && principal is not PersistenceFamily.Encodings)
        {
            return true;
        }

        return principal is PersistenceFamily.ProgrammeCache && declaring is not PersistenceFamily.ProgrammeCache;
    }

    private static PersistenceFamily? FamilyOf(IEntityType entityType)
    {
        string? feature = FeatureOf(AggregateRootOf(entityType));

        if (feature is null)
        {
            return null;
        }

        return FamiliesByFeature.TryGetValue(feature, out PersistenceFamily family) ? family : null;
    }

    private static IEntityType AggregateRootOf(IEntityType entityType)
    {
        IEntityType current = entityType;

        while (current.FindOwnership() is { } ownership)
        {
            current = ownership.PrincipalEntityType;
        }

        return current;
    }

    private static string? FeatureOf(IEntityType entityType)
    {
        string? space = entityType.ClrType.Namespace;

        if (string.IsNullOrEmpty(space))
        {
            return null;
        }

        return space[(space.LastIndexOf('.') + 1)..];
    }

    private static string EntityName(IEntityType entityType)
        => entityType.ClrType.FullName ?? entityType.Name;

    private static string TableName(IEntityType entityType)
        => entityType.GetTableName() ?? entityType.ShortName();
}
