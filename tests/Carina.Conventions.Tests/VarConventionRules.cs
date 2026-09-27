using System.Text.RegularExpressions;

namespace Carina.Conventions.Tests;

/// <summary>
/// Reads production source for a local declared <c>var</c> whose initializer is not an object,
/// array or collection creation, or a cast. A declaration whose statement holds an anonymous type
/// is left alone. It reads source text, so it sees the ordinary spellings and no others.
/// </summary>
public static partial class VarConventionRules
{
    public static IReadOnlyList<string> NonApparentVarDeclarations(string directory)
        => Scanned(directory)
            .SelectMany(file => Violations(file.Source).Select(name => $"{file.Relative} {name}"))
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> Violations(string source)
    {
        foreach (Match declaration in Declaration().Matches(source))
        {
            string name = declaration.Groups["name"].Value;
            int rhsStart = declaration.Index + declaration.Length;
            string statement = StatementAt(source, rhsStart);

            if (!IsApparent(statement))
            {
                yield return name;
            }
        }
    }

    private static bool IsApparent(string statement)
    {
        string trimmed = statement.TrimStart();

        // An anonymous type, anywhere the statement projects into one, is the one shape a
        // declaration can never spell out.
        return AnonymousType().IsMatch(statement)
               || NamedConstruction().IsMatch(trimmed)
               || ImplicitArray().IsMatch(trimmed)
               || Cast().IsMatch(trimmed)
               || Literal().IsMatch(trimmed);
    }

    private static string StatementAt(string source, int start)
    {
        int depth = 0;

        for (int at = start; at < source.Length; at++)
        {
            char letter = source[at];

            switch (letter)
            {
                case '(' or '{' or '[':
                    depth++;

                    break;
                case ')' or '}' or ']':
                    depth--;

                    break;
                case ';' when depth <= 0:
                    return source[start..at];
            }
        }

        return source[start..];
    }

    private static IEnumerable<SourceFile> Scanned(string directory)
        => Directory
            .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .Select(file => new SourceFile(
                "/" + Path.GetRelativePath(directory, file).Replace('\\', '/'),
                File.ReadAllText(file)));

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal) || segments.Contains("bin", StringComparer.Ordinal);
    }

    [GeneratedRegex(
        @"(?:^|[\r\n])[ \t]*(?:await\s+)?using\s+var\s+(?<name>\w+)\s*=(?!>)|" +
        @"(?:^|[\r\n])[ \t]*var\s+(?<name>\w+)\s*=(?!>)",
        RegexOptions.Multiline)]
    private static partial Regex Declaration();

    [GeneratedRegex(@"new\s*\{")]
    private static partial Regex AnonymousType();

    [GeneratedRegex(@"^new\s+[\w.]")]
    private static partial Regex NamedConstruction();

    [GeneratedRegex(@"^new\s*\[")]
    private static partial Regex ImplicitArray();

    [GeneratedRegex(@"^\(\s*[\w.<>\[\],? ]+\s*\)\S")]
    private static partial Regex Cast();

    [GeneratedRegex(@"^(-?\d|""|'|true\b|false\b|null\b|default\b)")]
    private static partial Regex Literal();

    private readonly record struct SourceFile(string Relative, string Source);
}
