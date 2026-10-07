namespace Carina.Domain.Segments;

/// <summary>
/// What a <see cref="SoundReader"/> has written so far, each kind in the order of the sound.
/// </summary>
public sealed class SoundReadings
{
    public List<uint> Fingerprints { get; } = [];

    public List<byte> Loudness { get; } = [];

    public List<ChannelDifference> Channels { get; } = [];
}
