namespace Carina.Domain.Base;

internal static class UtcTimes
{
    public static DateTime Required(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException(
                $"Times are kept in UTC, but this one has Kind={value.Kind}.",
                parameterName);
        }

        return value;
    }

    public static DateTime? Optional(DateTime? value, string parameterName)
        => value is null ? null : Required(value.Value, parameterName);
}
