using Carina.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Carina.Infrastructure.Tests;

public static class RecordingOwnedColumnRules
{
    public static IReadOnlyList<string> WritableThroughTheChangeTracker(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return Owned(model)
            .Where(column => column.Property.GetBeforeSaveBehavior() is not PropertySaveBehavior.Ignore
                || column.Property.GetAfterSaveBehavior() is not PropertySaveBehavior.Ignore)
            .Select(Named)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<string> Found(IModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        return Owned(model).Select(Named).Order(StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<OwnedColumn> Owned(IModel model)
        => model.GetEntityTypes()
            .Where(HoldsBothOfThem)
            .SelectMany(entityType => entityType.GetProperties().Select(property => new OwnedColumn(entityType, property)))
            .Where(column => ReservationConfiguration.RecordingOwnedColumns.Contains(
                column.Property.GetColumnName(),
                StringComparer.Ordinal));

    private static bool HoldsBothOfThem(IEntityType entityType)
    {
        IReadOnlyList<string> columns = [.. entityType.GetProperties().Select(property => property.GetColumnName())];

        return ReservationConfiguration.RecordingOwnedColumns.All(
            owned => columns.Contains(owned, StringComparer.Ordinal));
    }

    private static string Named(OwnedColumn column)
        => $"{column.EntityType.GetTableName()}.{column.Property.GetColumnName()}";

    private readonly record struct OwnedColumn(IEntityType EntityType, IProperty Property);
}
