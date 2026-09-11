using System.Text;
using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class RecordingRules
{
    private const string FeatureFolder = "/Recordings/";

    private const string FeatureNamespace = "Carina.Domain.Recordings";

    private const string Construction = "new OutcomeDetail(";

    public static readonly IReadOnlyList<string> AllowedToFillTheNote =
    [
        "/Carina.Infrastructure/Persistence/Repositories/RecordingDirectory.cs",
    ];

    public static IReadOnlyList<string> ComposersOfTheOutcomeDetailNote(string directory)
        => Scanned(directory)
            .Where(file => !AllowedToFillTheNote.Contains(file.Relative, StringComparer.Ordinal))
            .Where(file => FillsTheNote(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> EitReadersInsideTheRecordingFeature(string directory)
        => Scanned(directory)
            .Where(BelongsToTheRecordingFeature)
            .Where(file => ReadsSections().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> GuideWritersInsideTheRecordingFeature(string directory)
        => Scanned(directory)
            .Where(BelongsToTheRecordingFeature)
            .Where(file => WritesTheGuide().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    private static bool FillsTheNote(string source)
    {
        for (int at = source.IndexOf(Construction, StringComparison.Ordinal);
             at >= 0;
             at = source.IndexOf(Construction, at + 1, StringComparison.Ordinal))
        {
            IReadOnlyList<string> given = Arguments(source, at + Construction.Length);

            if (given.Count > 2 && !string.Equals(given[2], "string.Empty", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<string> Arguments(string source, int from)
    {
        List<string> given = [];
        StringBuilder current = new();
        int depth = 0;

        for (int at = from; at < source.Length; at++)
        {
            char letter = source[at];

            if (letter is ')' && depth is 0)
            {
                given.Add(current.ToString().Trim());

                return given;
            }

            if (letter is '(' or '[')
            {
                depth++;
            }
            else if (letter is ')' or ']')
            {
                depth--;
            }
            else if (letter is ',' && depth is 0)
            {
                given.Add(current.ToString().Trim());
                current.Clear();

                continue;
            }

            current.Append(letter);
        }

        return given;
    }

    private static bool BelongsToTheRecordingFeature(SourceFile file)
        => file.Relative.Contains(FeatureFolder, StringComparison.Ordinal)
           || file.Source.Contains(FeatureNamespace, StringComparison.Ordinal);

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
        @"\bEventInformationTable\b"
        + @"|\bSectionReader\b"
        + @"|\bSectionAssembler\b"
        + @"|\bCarina\.Broadcast\.Sections\b"
        + @"|\bTableRead<")]
    private static partial Regex ReadsSections();

    [GeneratedRegex(
        @"\bIProgrammeRepository\b"
        + @"|\bProgrammeWriter\b"
        + @"|\b(Db)?Set<\s*Programme\s*>"
        + @"|(?i:INSERT\s+INTO\s+programme\b)"
        + @"|(?i:UPDATE\s+programme\b)")]
    private static partial Regex WritesTheGuide();

    private readonly record struct SourceFile(string Relative, string Source);
}
