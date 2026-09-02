using System.Buffers.Binary;

namespace Carina.Domain.Streaming;

public static class LiveCaptions
{
    public const int CanvasLength = 4;

    public const int PlacementLength = 8;

    public static LiveFrame Canvas(VideoSize canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);

        if (canvas.Width > CaptionPicture.FurthestEdge || canvas.Height > CaptionPicture.FurthestEdge)
        {
            throw new ArgumentOutOfRangeException(nameof(canvas), canvas, "The wire measures the canvas in two bytes a side.");
        }

        byte[] payload = new byte[CanvasLength];

        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)canvas.Width);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), (ushort)canvas.Height);

        return new LiveFrame(LiveChannel.CaptionHeader, LivePts.Start, payload);
    }

    public static VideoSize? CanvasOf(LiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Channel is not LiveChannel.CaptionHeader || frame.Payload.Length is not CanvasLength)
        {
            return null;
        }

        ReadOnlySpan<byte> payload = frame.Payload.Span;
        int width = BinaryPrimitives.ReadUInt16BigEndian(payload);
        int height = BinaryPrimitives.ReadUInt16BigEndian(payload[2..]);

        return width > 0 && height > 0 ? new VideoSize(width, height) : null;
    }

    public static LiveFrame Shown(LivePts at, CaptionPicture picture)
    {
        ArgumentNullException.ThrowIfNull(at);
        ArgumentNullException.ThrowIfNull(picture);

        byte[] payload = new byte[PlacementLength + picture.Png.Length];

        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)picture.Left);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(2), (ushort)picture.Top);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4), (ushort)picture.Width);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(6), (ushort)picture.Height);
        picture.Png.Span.CopyTo(payload.AsSpan(PlacementLength));

        return new LiveFrame(LiveChannel.Caption, at, payload);
    }

    public static LiveFrame Cleared(LivePts at)
    {
        ArgumentNullException.ThrowIfNull(at);

        return new LiveFrame(LiveChannel.Caption, at, ReadOnlyMemory<byte>.Empty);
    }

    public static bool Clears(LiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return frame.Channel is LiveChannel.Caption && frame.Payload.IsEmpty;
    }

    public static CaptionPicture? PictureOf(LiveFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Channel is not LiveChannel.Caption || frame.Payload.Length <= PlacementLength)
        {
            return null;
        }

        ReadOnlySpan<byte> payload = frame.Payload.Span;
        int width = BinaryPrimitives.ReadUInt16BigEndian(payload[4..]);
        int height = BinaryPrimitives.ReadUInt16BigEndian(payload[6..]);

        if (width is 0 || height is 0)
        {
            return null;
        }

        return new CaptionPicture(
            BinaryPrimitives.ReadUInt16BigEndian(payload),
            BinaryPrimitives.ReadUInt16BigEndian(payload[2..]),
            width,
            height,
            frame.Payload[PlacementLength..]);
    }
}
