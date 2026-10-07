namespace Carina.Domain.Segments;

/// <summary>
/// The sound a reading of a recording took its learning data from: the number of the stream among
/// the programme's sounds, and how far its first sample lies from the first frame.
/// </summary>
public sealed record ExtractionSound
{
    public ExtractionSound(int stream, TimeSpan offset)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(stream);

        Stream = stream;
        Offset = offset;
    }

    public int Stream { get; }

    public TimeSpan Offset { get; }
}
