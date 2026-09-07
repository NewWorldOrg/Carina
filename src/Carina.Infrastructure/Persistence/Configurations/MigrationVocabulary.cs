using Carina.Domain.Migration;

namespace Carina.Infrastructure.Persistence.Configurations;

internal static class MigrationVocabulary
{
    public const int NameLength = 32;

    public static string Of<T>()
        where T : struct, Enum
        => Listed(Enum.GetNames<T>());

    public static string Counted()
        => Listed([.. MigrationPopulations.Counted.Select(population => population.ToString())]);

    public static string EachThingLeftAlone(string subjectColumn, string groundColumn)
        => string.Join(
            "\nAND ",
            MigrationOmissionSubjects.All.Select(subject =>
                $"({subjectColumn} <> '{subject}' OR {groundColumn} = '{MigrationOmission.GroundOf(subject)}')"));

    private static string Listed(IReadOnlyList<string> names) => string.Join(", ", names.Select(name => $"'{name}'"));
}
