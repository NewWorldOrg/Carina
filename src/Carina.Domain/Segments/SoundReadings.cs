namespace Carina.Domain.Segments;

public sealed class SoundReadings
{
    public List<uint> Fingerprints { get; } = [];

    public List<byte> Loudness { get; } = [];

    public List<ChannelDifference> Channels { get; } = [];
}
