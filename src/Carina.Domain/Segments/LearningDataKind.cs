namespace Carina.Domain.Segments;

/// <summary>
/// The kinds of learning data, each kept chunk by chunk on its own. A kind's number is written
/// with its data.
/// </summary>
public enum LearningDataKind : byte
{
    SoundFingerprints = 1,
    Loudness = 2,
    ChannelDifferences = 3,
    FrameLights = 4,
    CornerOutlines = 5,
}
