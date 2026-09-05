using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public sealed record SessionEndCaller(string File, int Calls);

public static partial class SessionEndRules
{
    public const string TheOneWayAnEndMovesEarlier = "EndsNoLaterThan";

    public const string WhereItIsDeclared = "Carina.Driver/Sessions/TunerSession.cs";

    public const string WhereItIsCalled = "Carina.Driver/Sessions/TunerSessionManager.cs";

    public static IReadOnlyList<SessionEndCaller> CallersThatMoveAnEndEarlier(string directory)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => new SessionEndCaller(
                Path.GetRelativePath(directory, file).Replace('\\', '/'),
                Calls().Count(File.ReadAllText(file))))
            .Where(caller => caller.Calls > 0)
            .OrderBy(caller => caller.File, StringComparer.Ordinal)
            .ToArray();

    public static bool DeclaresTheMethod(string directory)
        => Declaration().IsMatch(
            File.ReadAllText(Path.Combine(directory, WhereItIsDeclared.Replace('/', Path.DirectorySeparatorChar))));

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal) || segments.Contains("bin", StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\." + TheOneWayAnEndMovesEarlier + @"\(")]
    private static partial Regex Calls();

    [GeneratedRegex(@"\bpublic\s+bool\s+" + TheOneWayAnEndMovesEarlier + @"\(")]
    private static partial Regex Declaration();
}
