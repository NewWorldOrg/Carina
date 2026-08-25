using System.Text.RegularExpressions;

namespace Carina.Architecture.Tests;

public static partial class MeasurementRules
{
    public const string Counter = "ContinuityCounterTracker";

    public const string Packet = "TsPacket";

    public const string Reader = "TsPacketReader";

    public const string CounterDefinition = "ContinuityCounterTracker.cs";

    public const string RecordingFolder = "Carina.Driver/Recording/";

    public const int MarksThatMakeAParser = 2;

    public static readonly IReadOnlyList<string> ParsersByTrade = ["Carina.Broadcast"];

    public static readonly IReadOnlyList<string> AllowedToTakeTheStreamApart =
    [
        "Carina.Driver/Transport/TsPacketReader.cs",
        "Carina.Driver/Tuning/FakeTunerDevice.cs",
    ];

    public static IReadOnlyList<Regex> Marks =>
        [Names(), SyncByte(), PacketStride(), PayloadStride(), PidMask(), CounterMask()];

    public static int MarksIn(string source) => Marks.Count(mark => mark.IsMatch(source));

    public static IReadOnlyList<string> PlacesThatShowTheMarksOfAParser(string directory) =>
        [
            .. Directory
                .EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(file))
                .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'))
                .Where(file => !PliesTheTrade(file))
                .Where(file =>
                    MarksIn(File.ReadAllText(Path.Combine(directory, file)))
                    >= MarksThatMakeAParser)
                .Order(StringComparer.Ordinal),
        ];

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

    private static bool PliesTheTrade(string relative) =>
        ParsersByTrade.Any(project =>
            relative.StartsWith(project + "/", StringComparison.Ordinal));

    private static bool IsBuildOutput(string path)
    {
        string[] segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return segments.Contains("obj", StringComparer.Ordinal)
               || segments.Contains("bin", StringComparer.Ordinal);
    }

    [GeneratedRegex(@"\bTsPacketReader\b|\bTsPacket\b|\bContinuityCounterTracker\b")]
    private static partial Regex Names();

    [GeneratedRegex(@"0x47")]
    private static partial Regex SyncByte();

    [GeneratedRegex(@"(?<![\w.])188(?![\w])")]
    private static partial Regex PacketStride();

    [GeneratedRegex(@"(?<![\w.])184(?![\w])")]
    private static partial Regex PayloadStride();

    [GeneratedRegex(@"0x1F{1,3}\b")]
    private static partial Regex PidMask();

    [GeneratedRegex(@"0x0?F\b")]
    private static partial Regex CounterMask();
}
