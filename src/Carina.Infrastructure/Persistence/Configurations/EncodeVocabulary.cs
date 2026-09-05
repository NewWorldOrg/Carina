namespace Carina.Infrastructure.Persistence.Configurations;

internal static class EncodeVocabulary
{
    public static string Of<T>()
        where T : struct, Enum
        => string.Join(", ", Enum.GetNames<T>().Select(name => $"'{name}'"));

    public static string ASingleName(string column)
        => $"""
            btrim({column}) = {column}
            AND length({column}) > 0
            AND {column} <> '.'
            AND strpos({column}, '/') = 0
            AND strpos({column}, chr(92)) = 0
            AND strpos({column}, '..') = 0
            """;
}
