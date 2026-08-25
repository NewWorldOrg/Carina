namespace Carina.Architecture.Tests;

public static class MeasurementRules
{
    public const string Counter = "ContinuityCounterTracker";

    public const string Packet = "TsPacket";

    public const string Reader = "TsPacketReader";

    public const string CounterDefinition = "ContinuityCounterTracker.cs";

    public const string RecordingFolder = "Carina.Driver/Recording/";

    public static readonly IReadOnlyList<string> AllowedToTakeTheStreamApart =
    [
        "Carina.Driver/Sessions/TunerSession.cs",
        "Carina.Driver/Transport/ContinuityCounterTracker.cs",
        "Carina.Driver/Transport/TsPacketReader.cs",
        "Carina.Driver/Tuning/FakeTunerDevice.cs",
    ];

    public static IReadOnlyList<string> PlacesThatTakeTheStreamApart(string directory) =>
        SourceScan.FilesMentioning(directory, Reader, Packet);

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
}
