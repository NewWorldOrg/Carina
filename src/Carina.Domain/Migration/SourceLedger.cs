namespace Carina.Domain.Migration;

public sealed record SourceLedger
{
    public SourceLedger(
        MigrationSourceName name,
        IReadOnlyList<SourceRecording> recordings,
        IReadOnlyList<SourceRecordingFile> recordingFiles,
        IReadOnlyList<SourceRule> rules,
        IReadOnlyList<SourceReservation> reservations,
        IReadOnlyList<SourceChannelDefinition> channelDefinitions)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        Recordings = Kept(recordings, nameof(recordings));
        RecordingFiles = Kept(recordingFiles, nameof(recordingFiles));
        Rules = Kept(rules, nameof(rules));
        Reservations = Kept(reservations, nameof(reservations));
        ChannelDefinitions = Kept(channelDefinitions, nameof(channelDefinitions));
    }

    public MigrationSourceName Name { get; }

    public IReadOnlyList<SourceRecording> Recordings { get; }

    public IReadOnlyList<SourceRecordingFile> RecordingFiles { get; }

    public IReadOnlyList<SourceRule> Rules { get; }

    public IReadOnlyList<SourceReservation> Reservations { get; }

    public IReadOnlyList<SourceChannelDefinition> ChannelDefinitions { get; }

    private static IReadOnlyList<T> Kept<T>(IReadOnlyList<T> rows, string parameterName)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(rows, parameterName);

        foreach (T row in rows)
        {
            ArgumentNullException.ThrowIfNull(row, parameterName);
        }

        return [.. rows];
    }
}
