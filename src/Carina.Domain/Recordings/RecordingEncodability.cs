namespace Carina.Domain.Recordings;

/// <summary>
/// Whether a recording has something to encode as it stands.
/// </summary>
public enum RecordingEncodability
{
    Encodable = 1,

    StillBeingWritten = 2,

    Failed = 3,
}
