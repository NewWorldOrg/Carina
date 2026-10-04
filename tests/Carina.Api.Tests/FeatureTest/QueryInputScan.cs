using System.Text.RegularExpressions;

namespace Carina.Api.Tests.FeatureTest;

internal readonly record struct QueryRead(string File, string Surface, string Name)
{
    public override string ToString() => $"{Surface} {Name}";
}

internal static partial class QueryInputScan
{
    private const string RootMarker = "Carina.slnx";

    public static string ApiDirectory { get; } = Path.Combine(Root(), "src", "Carina.Api");

    public static IReadOnlyList<QueryRead> WhatEachSurfaceReads(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        return
        [
            .. Scanned(directory)
                .SelectMany(file => Reads(file).Where(read => read.Placed).Select(read => read.Read))
                .Distinct()
                .OrderBy(read => read.ToString(), StringComparer.Ordinal),
        ];
    }

    public static IReadOnlyList<string> WhatTheScanCouldNotPlace(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        return
        [
            .. Scanned(directory)
                .SelectMany(file => Reads(file).Where(read => !read.Placed).Select(read => read.Unplaced))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static IEnumerable<PlacedRead> Reads(SourceFile file)
    {
        string? surface = SurfaceOf(file.Source);
        Dictionary<string, string> spellings = Constant()
            .Matches(file.Source)
            .ToDictionary(match => match.Groups["name"].Value, match => match.Groups["value"].Value, StringComparer.Ordinal);

        foreach (Match read in Read().Matches(file.Source))
        {
            string key = read.Groups["key"].Value;
            string? name = Literal().Match(key) is { Success: true } literal
                ? literal.Groups["literal"].Value
                : spellings.GetValueOrDefault(key);

            yield return name is null || surface is null
                ? new PlacedRead(false, default, $"{file.Relative} {(surface is null ? "no route for" : "cannot resolve")} {key}")
                : new PlacedRead(true, new QueryRead(file.Relative, surface, name), string.Empty);
        }
    }

    private static string? SurfaceOf(string source)
    {
        if (PathConstant().Match(source) is { Success: true } declared)
        {
            return "/" + declared.Groups["path"].Value.TrimStart('/');
        }

        return RouteAttribute().Match(source) is { Success: true } routed
            ? "/" + routed.Groups["route"].Value.TrimStart('/')
            : null;
    }

    [GeneratedRegex(
        @"\.\s*Query\s*(?:\[\s*(?<key>[^\]]+?)\s*\]|\.\s*TryGetValue\(\s*(?<key>[^,]+?)\s*,)")]
    private static partial Regex Read();

    [GeneratedRegex(@"const\s+string\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*""(?<value>[^""]*)""\s*;")]
    private static partial Regex Constant();

    [GeneratedRegex(@"const\s+string\s+Path\s*=\s*""(?<path>[^""]*)""\s*;")]
    private static partial Regex PathConstant();

    [GeneratedRegex(@"\[Route\(\s*""(?<route>[^""]*)""\s*\)\]")]
    private static partial Regex RouteAttribute();

    [GeneratedRegex(@"^""(?<literal>[^""]*)""$")]
    private static partial Regex Literal();

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

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, RootMarker)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root: no {RootMarker} found above {AppContext.BaseDirectory}.");
    }

    private readonly record struct SourceFile(string Relative, string Source);

    private readonly record struct PlacedRead(bool Placed, QueryRead Read, string Unplaced);
}
