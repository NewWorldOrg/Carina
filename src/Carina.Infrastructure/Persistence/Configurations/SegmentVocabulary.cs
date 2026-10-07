using System.Globalization;
using System.Text.Json;

using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Carina.Infrastructure.Persistence.Configurations;

internal static class SegmentVocabulary
{
    public const int NameLength = 32;

    public static string Names<T>()
        where T : struct, Enum
        => string.Join(", ", Enum.GetNames<T>().Select(name => $"'{name}'"));

    public static string Numbers<T>()
        where T : struct, Enum
        => string.Join(
            ", ",
            Enum.GetValues<T>().Select(value => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)));

    public static string Written<T>(IReadOnlyList<T> list) => JsonSerializer.Serialize(list, ProgrammeJson.Options);

    public static IReadOnlyList<T> Read<T>(string stored)
        => JsonSerializer.Deserialize<List<T>>(stored, ProgrammeJson.Options) ?? [];

    public static ValueComparer<IReadOnlyList<T>> Compared<T>()
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
