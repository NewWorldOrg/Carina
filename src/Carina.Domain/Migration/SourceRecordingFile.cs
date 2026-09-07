namespace Carina.Domain.Migration;

public sealed record SourceRecordingFile
{
    public SourceRecordingFile(long recordingId, string path, SourceFileKind kind, long sizeRecorded)
    {
        RecordingId = SourceRow.Of(recordingId, nameof(recordingId));
        Path = SourcePath.Of(path, nameof(path));

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A file of the source system is one it can name.");
        }

        if (sizeRecorded < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sizeRecorded),
                sizeRecorded,
                "A file is not smaller than empty.");
        }

        Kind = kind;
        SizeRecorded = sizeRecorded;
    }

    public long RecordingId { get; }

    public string Path { get; }

    public SourceFileKind Kind { get; }

    public long SizeRecorded { get; }
}
