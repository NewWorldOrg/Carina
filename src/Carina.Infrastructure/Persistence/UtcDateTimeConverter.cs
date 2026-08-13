using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Carina.Infrastructure.Persistence;

public sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
    value => RequireUtc(value),
    value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
{
    private static DateTime RequireUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                $"DateTime values must be UTC before they are persisted, but this one has Kind={value.Kind}.");
        }

        return value;
    }
}
