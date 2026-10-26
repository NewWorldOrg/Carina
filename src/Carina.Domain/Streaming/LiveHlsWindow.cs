namespace Carina.Domain.Streaming;

public sealed record LiveHlsSegment
{
    public LiveHlsSegment(long number, TimeSpan length, int bytes)
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
/// The segments held, oldest first, with the target in whole seconds, whether the session has ended and the codecs.
/// </summary>
public sealed record LiveHlsWindow(IReadOnlyList<LiveHlsSegment> Segments, int TargetSeconds, bool Ended, string? Codecs)
{
    public static LiveHlsWindow Empty { get; } = new([], 0, false, null);

    public long FirstNumber => Segments.Count is 0 ? 0 : Segments[0].Number;
}
