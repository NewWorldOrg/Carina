using Carina.Domain.Migration;

namespace Carina.Infrastructure.Persistence.Configurations;

internal static class MigrationVocabulary
{
    public const int NameLength = 32;

    public static string Of<T>()
        where T : struct, Enum
        => Listed(Enum.GetNames<T>());

    public static string Naming<T>(IReadOnlyList<T> values)
        where T : struct, Enum
        => Listed([.. values.Select(value => value.ToString())]);

    public static string Counted()
        => Listed([.. MigrationPopulations.Counted.Select(population => population.ToString())]);

    private static string Listed(IReadOnlyList<string> names) => string.Join(", ", names.Select(name => $"'{name}'"));
}
