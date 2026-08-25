namespace Carina.Architecture.Tests;

public static class MeasurementRules
{
    public const string Counter = "ContinuityCounterTracker";

    public const string Packet = "TsPacket";

    public const string CounterDefinition = "ContinuityCounterTracker.cs";

    public const string RecordingFolder = "Carina.Driver/Recording/";

    public static IReadOnlyList<string> PlacesThatCountWhatTheTunerGave(string directory) =>
        [
            .. SourceScan
                .FilesMentioning(directory, Counter)
                .Where(file => !file.EndsWith(CounterDefinition, StringComparison.Ordinal)),
        ];

    public static IReadOnlyList<string> PacketsReadInsideTheRecordingWriter(string directory) =>
        [
            .. SourceScan
                .FilesMentioning(directory, Counter, Packet)
                .Where(file => file.Contains(RecordingFolder, StringComparison.Ordinal)),
        ];
}
