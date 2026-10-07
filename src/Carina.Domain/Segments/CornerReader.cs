using Carina.Domain.Encodings;

namespace Carina.Domain.Segments;

/// <summary>
/// Reads pictures handed over in the shape <see cref="WatermarkFrame"/> names, in pieces of any
/// length, and writes the <see cref="CornerOutline"/> of each as soon as the whole of it has arrived.
/// </summary>
public sealed class CornerReader
{
    private readonly byte[] frame = new byte[WatermarkFrame.Pixels];

    private readonly byte[] outline = new byte[CornerOutline.Bytes];

    private int held;

    public long Pictures { get; private set; }

    public void Glimpse(ReadOnlySpan<byte> pictures, List<byte> outlines)
    {
        ArgumentNullException.ThrowIfNull(outlines);

        ReadOnlySpan<byte> rest = pictures;

        while (!rest.IsEmpty)
        {
            int taken = Math.Min(rest.Length, WatermarkFrame.Pixels - held);
            rest[..taken].CopyTo(frame.AsSpan(held));
            held += taken;
            rest = rest[taken..];

            if (held == WatermarkFrame.Pixels)
            {
                CornerOutline.Draw(frame, outline);
                outlines.AddRange(outline);
                held = 0;
                Pictures++;
            }
        }
    }
}
