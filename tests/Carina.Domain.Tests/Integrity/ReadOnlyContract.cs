using System.Reflection;

namespace Carina.Domain.Tests.Integrity;

internal static class ReadOnlyContract
{
    private static readonly string[] CouldChangeAFile =
        ["Delete", "Remove", "Write", "Move", "Rename", "Truncate", "Create", "Purge", "Drop", "Prune"];

    public static IReadOnlyList<string> Names(Type contract)
        => contract
            .GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> MembersThatCouldChangeAFile(Type contract)
        => Names(contract)
            .Where(name => CouldChangeAFile.Any(word => name.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
