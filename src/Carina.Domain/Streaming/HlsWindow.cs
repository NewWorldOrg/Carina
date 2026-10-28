namespace Carina.Domain.Streaming;

public sealed record HlsSegment
{
    public HlsSegment(long number, TimeSpan length, int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        Number = number;
        Length = length;
        Bytes = bytes;
    }

    public long Number { get; }

    public TimeSpan Length { get; }

    public int Bytes { get; }
}

/// <summary>
/// Whether a playlist lists the newest segments alone, as a live channel's does, or every segment from the first, as
/// a recording's does.
/// </summary>
public enum HlsPlaylistType
{
    Sliding = 1,

    Event = 2,
}

/// <summary>
/// The segments listed, oldest first, with the target in whole seconds, whether the end has come, the codecs and
/// whether the playlist slides or lists every segment from the first.
/// </summary>
public sealed record HlsWindow(
    IReadOnlyList<HlsSegment> Segments,
    int TargetSeconds,
    bool Ended,
    string? Codecs,
    HlsPlaylistType Type = HlsPlaylistType.Sliding)
{
    public static HlsWindow Empty { get; } = new([], 0, false, null);

    public long FirstNumber => Segments.Count is 0 ? 0 : Segments[0].Number;
}
