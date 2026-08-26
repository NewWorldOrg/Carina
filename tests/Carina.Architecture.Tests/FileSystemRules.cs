using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class FileSystemRules
{
    public static IReadOnlyList<string> WhatCouldChangeWhatIsOnDisk(string directory)
        => Scanned(directory)
            .SelectMany(file => Reaches()
                .Matches(file.Source)
                .Select(match => $"{file.Relative} {Squeezed(match.Value)}"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static IReadOnlyList<string> WhatCouldChangeWhatIsOnDiskIn(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return Reaches()
            .Matches(source)
            .Select(match => Squeezed(match.Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string Squeezed(string matched) => Spaces().Replace(matched, string.Empty);

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
        @"\bFile\s*\.\s*(Delete|Create|CreateText|CreateSymbolicLink|Move|Replace|Copy|Open|OpenWrite|OpenHandle"
        + @"|AppendText|WriteAll\w*|AppendAll\w*|SetAttributes|SetUnixFileMode|SetLastWrite\w*|Encrypt|Decrypt)\b"
        + @"|\bDirectory\s*\.\s*(Delete|Move|CreateDirectory|CreateSymbolicLink|CreateTempSubdirectory)\b"
        + @"|\.\s*(Delete|MoveTo|CopyTo|Replace|SetLength|Encrypt|Decrypt)\s*\("
        + @"|\bnew\s+(FileStream|StreamWriter|BinaryWriter|SafeFileHandle)\b"
        + @"|\bFileMode\s*\."
        + @"|\bDllImport\b|\bLibraryImport\b|\bNativeLibrary\b"
        + @"|\bProcess\s*\.\s*Start\b|\bProcessStartInfo\b"
        + @"|\bunlink\b|\bftruncate\b"
        + @"|\bGetMethod\s*\(|\bGetMember\s*\(|\bActivator\s*\.\s*CreateInstance|\bCreateDelegate\b")]
    private static partial Regex Reaches();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private readonly record struct SourceFile(string Relative, string Source);
}
