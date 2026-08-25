using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class MeasurementRules
{
    public const string Counter = "ContinuityCounterTracker";

    public const string Packet = "TsPacket";

    public const string Reader = "TsPacketReader";

    public const string CounterDefinition = "ContinuityCounterTracker.cs";

    public const string DriverProject = "Carina.Driver";

    public const string RecordingFolder = "Carina.Driver/Recording/";

    public static readonly IReadOnlyList<string> AllowedToTakeTheStreamApart =
    [
        "Sessions/TunerSession.cs",
        "Transport/ContinuityCounterTracker.cs",
        "Transport/TsPacketReader.cs",
        "Tuning/FakeTunerDevice.cs",
    ];

    public static IReadOnlyList<string> PlacesThatTakeTheStreamApart(string directory)
    {
        string driver = Path.Combine(directory, DriverProject);

        return Directory.Exists(driver)
            ? Matching(driver, source => TakesAStreamApart().IsMatch(source))
            : Matching(directory, source => TakesAStreamApart().IsMatch(source));
    }

    public static IReadOnlyList<string> PlacesThatCountWhatTheTunerGave(string directory) =>
        [
            .. SourceScan
                .FilesMentioning(directory, Counter)
                .Where(file => !file.EndsWith(CounterDefinition, StringComparison.Ordinal)),
        ];

    public static IReadOnlyList<string> PacketsReadInsideTheRecordingWriter(string directory) =>
        [
            .. SourceScan
                .FilesMentioning(directory, Counter, Packet, Reader)
                .Where(file => file.Contains(RecordingFolder, StringComparison.Ordinal)),
        ];

    private static IReadOnlyList<string> Matching(string directory, Func<string, bool> holds) =>
        [
            .. Directory
                .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(file))
                .Where(file => holds(File.ReadAllText(file)))
                .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal)
               || segments.Contains("bin", StringComparer.Ordinal);
    }

    [GeneratedRegex(
        @"\bTsPacketReader\b"
        + @"|\bTsPacket\b"
        + @"|\bContinuityCounterTracker\b"
        + @"|0x47"
        + @"|(?<![\w.])188(?![\w])"
        + @"|&\s*0x1F\b"
        + @"|&\s*0x0F\b")]
    private static partial Regex TakesAStreamApart();
}
