namespace Carina.Domain.Recordings;

/// <summary>
/// Why a recording is not thrown away as asked.
/// </summary>
public enum RecordingDisposalRefusal
{
    NoSuchRecording = 1,

    StillBeingWritten = 2,

    BeingEncoded = 3,
}

/// <summary>
/// What asking to throw a recording away comes to before anything is removed: the recording to throw away, or why it
/// stays.
/// </summary>
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
