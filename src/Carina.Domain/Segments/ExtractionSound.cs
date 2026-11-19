namespace Carina.Domain.Segments;

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
