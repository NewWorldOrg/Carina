namespace Carina.Domain.Recordings;

public enum RecordingDisposalRefusal
{
    NoSuchRecording = 1,

    StillBeingWritten = 2,

    BeingEncoded = 3,
}

public sealed record RecordingDisposal
{
    private RecordingDisposal(Recording? recording, RecordingDisposalRefusal? refusal)
    {
        Recording = recording;
        Refusal = refusal;
    }

    public Recording? Recording { get; }

    public RecordingDisposalRefusal? Refusal { get; }

    public static RecordingDisposal Of(Recording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return new RecordingDisposal(recording, null);
    }

    public static RecordingDisposal Refused(RecordingDisposalRefusal refusal)
        => Enum.IsDefined(refusal)
            ? new RecordingDisposal(null, refusal)
            : throw new ArgumentOutOfRangeException(nameof(refusal), refusal, "A refusal names why the recording stays.");
}
