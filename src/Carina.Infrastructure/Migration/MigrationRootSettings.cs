using System.Diagnostics.CodeAnalysis;

using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Infrastructure.Configuration;

namespace Carina.Infrastructure.Migration;

public sealed class MigrationRootSettings
{
    public const string DeclarationVariable = $"{IntegrityOptions.Section}__{nameof(IntegrityOptions.OutputRoots)}";

    private MigrationRootSettings(OutputRoot root) => Root = root;

    public OutputRoot Root { get; }

    public static bool TryRead(
        Func<string, string?> environment,
        string into,
        [NotNullWhen(true)] out MigrationRootSettings? read,
        out string problem)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(into);

        read = null;
        IntegritySettings declared;

        try
        {
            declared = new IntegrityOptions { OutputRoots = environment(DeclarationVariable) }.Read();
        }
        catch (ArgumentException unusable)
        {
            problem = $"{DeclarationVariable} does not say which output roots this installation holds: "
                + unusable.Message;

            return false;
        }

        if (!declared.WalksAnything)
        {
            problem = $"{DeclarationVariable} declares no output root, and a recording carried into a root "
                + "nothing declares can afterwards be neither played, swept, weighed nor deleted. Declare the "
                + "root this installation holds and run this again.";

            return false;
        }

        string wanted = Settled(into);
        StorageRootPath? mounted = declared.OutputRoots
            .FirstOrDefault(candidate => string.Equals(Settled(candidate.Path), wanted, StringComparison.Ordinal));

        if (mounted is null)
        {
            problem = $"'{into}' is none of the output roots {DeclarationVariable} declares "
                + $"({string.Join(", ", declared.OutputRoots.Select(root => root.Path))}), and a recording "
                + "carried into a root nothing declares can afterwards be neither played, swept, weighed nor "
                + "deleted. Carry into one of the declared roots, or declare this one, and run this again.";

            return false;
        }

        read = new MigrationRootSettings(mounted.Root);
        problem = string.Empty;

        return true;
    }

    private static string Settled(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
