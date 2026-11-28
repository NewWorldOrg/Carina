namespace Carina.Domain.Streaming;

public sealed record LiveStill
{
    private LiveStill(LiveFrame? frame, string note)
    {
        Frame = frame;
        Note = note;
    }

    public LiveFrame? Frame { get; }

    public string Note { get; }

    public static LiveStill Of(LivePts at, ReadOnlyMemory<byte> jpeg)
    {
        ArgumentNullException.ThrowIfNull(at);

        if (!IsJpeg(jpeg.Span))
        {
            throw new ArgumentException("A still is a JPEG, and these bytes do not open as one.", nameof(jpeg));
        }

        return new LiveStill(new LiveFrame(LiveChannel.Still, at, jpeg), string.Empty);
    }

    public static LiveStill Missing(string note)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);

        return new LiveStill(null, note);
    }

    public static bool IsJpeg(ReadOnlySpan<byte> bytes) => bytes.StartsWith((ReadOnlySpan<byte>)[0xff, 0xd8, 0xff]);
}
