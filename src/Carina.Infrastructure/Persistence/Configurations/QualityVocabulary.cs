namespace Carina.Infrastructure.Persistence.Configurations;

internal static class QualityVocabulary
{
    public const int NameLength = 32;

    public const int LowestBroadcastId = 0;

    public const int HighestBroadcastId = 65535;

    public static string Of<T>()
        where T : struct, Enum
        => string.Join(", ", Enum.GetNames<T>().Select(name => $"'{name}'"));

    public static string ABroadcastIdentifier(string column)
        => $"{column} BETWEEN {LowestBroadcastId} AND {HighestBroadcastId}";

    public static string AnEmptyList(string column) => $"{column} = '[]'::jsonb";
}
