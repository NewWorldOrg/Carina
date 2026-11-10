namespace Carina.Infrastructure.Persistence.Configurations;

internal static class NotificationVocabulary
{
    public const int NameLength = 32;

    public static string Of<T>()
        where T : struct, Enum
        => string.Join(", ", Enum.GetNames<T>().Select(name => $"'{name}'"));

    public static string Listed<T>()
        where T : struct, Enum
        => "[" + string.Join(", ", Enum.GetNames<T>().Select(name => $"\"{name}\"")) + "]";

    public static string AName(string column)
        => $"btrim({column}) = {column} AND length({column}) > 0";
}
