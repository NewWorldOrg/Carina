using System.Diagnostics.CodeAnalysis;

using Carina.Domain.Migration;

namespace Carina.Db;

public sealed class CarryArguments
{
    public const string Verb = "--carry";

    private CarryArguments(string from, string into, MigrationPass pass)
    {
        From = from;
        Into = into;
        Pass = pass;
    }

    public string From { get; }

    public string Into { get; }

    public MigrationPass Pass { get; }

    public static bool TryRead(
        IReadOnlyList<string> args,
        [NotNullWhen(true)] out CarryArguments? read,
        out string problem)
    {
        ArgumentNullException.ThrowIfNull(args);

        read = null;

        if (args.Count is 0 || !string.Equals(args[0], Verb, StringComparison.Ordinal))
        {
            problem = $"expected {Verb}.";

            return false;
        }

        string? from = null;
        string? into = null;
        bool forReal = false;

        for (int at = 1; at < args.Count; at++)
        {
            switch (args[at])
            {
                case "--from" when from is null && at + 1 < args.Count:
                    from = args[++at];

                    break;
                case "--into" when into is null && at + 1 < args.Count:
                    into = args[++at];

                    break;
                case "--for-real" when !forReal:
                    forReal = true;

                    break;
                default:
                    problem = $"'{args[at]}' is not one of {Verb}'s arguments, or was given twice.";

                    return false;
            }
        }

        if (from is null || into is null)
        {
            problem = $"{Verb} needs --from <source directory> and --into <new root directory>.";

            return false;
        }

        if (!Path.IsPathRooted(from) || !Path.IsPathRooted(into))
        {
            problem = "Both directories are named from the top of the disk down.";

            return false;
        }

        read = new CarryArguments(from, into, forReal ? MigrationPass.ForReal : MigrationPass.Rehearsal);
        problem = string.Empty;

        return true;
    }
}
