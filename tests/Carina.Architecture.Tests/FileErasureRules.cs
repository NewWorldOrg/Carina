using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class FileErasureRules
{
    public static readonly IReadOnlyList<string> TheErasersThatAskTheDriver =
    [
        "/Carina.Infrastructure/Integrity/DriverStrayFileEraser.cs",
        "/Carina.Infrastructure/Recordings/DriverRecordingFileEraser.cs",
    ];

    public static readonly IReadOnlyList<string> TheWayAFindingIsThrownAway =
    [
        "/Carina.Api/Controllers/Recordings/DeleteIntegrityFindingAction.cs",
        "/Carina.Api/Services/IntegrityService.cs",
        "/Carina.Domain/Integrity/StrayFileDisposal.cs",
        "/Carina.Infrastructure/Integrity/DriverStrayFileEraser.cs",
    ];

    public static IReadOnlyList<string> WhatAsksTheDriverToEraseAFile(string directory)
        => Scanned(directory)
            .Where(file => AsksTheDriverToErase().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatImplementsAnErasurePort(string directory)
        => Scanned(directory)
            .Where(file => ImplementsAnErasurePort().IsMatch(file.Source))
            .Select(file => file.Relative)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> AsksTheDriverToEraseIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return AsksTheDriverToErase()
            .Matches(source)
            .Select(match => Spaces().Replace(match.Value, string.Empty))
            .ToArray();
    }

    public static bool ImplementsAnErasurePortIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return ImplementsAnErasurePort().IsMatch(source);
    }

    public static string Read(string directory, string relative)
        => File.ReadAllText(Path.Combine(directory, relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

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

    [GeneratedRegex(@"\.\s*Erase(?:Recording|StrayFile)Async\s*\(")]
    private static partial Regex AsksTheDriverToErase();

    [GeneratedRegex(@"(?:\)|\bclass\s+\w+)\s*:\s*(?:[\w.<>]+\s*,\s*)*I(?:RecordingFile|StrayFile)Eraser\b")]
    private static partial Regex ImplementsAnErasurePort();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private readonly record struct SourceFile(string Relative, string Source);
}
