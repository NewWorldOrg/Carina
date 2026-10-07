namespace Carina.Domain.Segments;

/// <summary>
/// Reads frames handed over in the shape <see cref="FrameLight"/> names, in pieces of any length,
/// and writes the light of each as soon as the whole of it has arrived. The first frame is read as
/// having changed by nothing.
/// </summary>
public sealed class PictureReader
{
    private byte[] frame = new byte[FrameLight.Pixels];

    private byte[] previous = new byte[FrameLight.Pixels];

    private int held;

    private bool seenOne;

    public long Frames { get; private set; }

    public void See(ReadOnlySpan<byte> frames, List<FrameLight> lights)
    {
        ArgumentNullException.ThrowIfNull(lights);

        ReadOnlySpan<byte> rest = frames;

        while (!rest.IsEmpty)
        {
            int taken = Math.Min(rest.Length, FrameLight.Pixels - held);
            rest[..taken].CopyTo(frame.AsSpan(held));
            held += taken;
            rest = rest[taken..];

            if (held == FrameLight.Pixels)
            {
                lights.Add(Light());
                (previous, frame) = (frame, previous);
                held = 0;
                seenOne = true;
                Frames++;
            }
        }
    }

    private FrameLight Light()
    {
        long brightness = 0;
        long change = 0;

        for (int pixel = 0; pixel < FrameLight.Pixels; pixel++)
        {
            brightness += frame[pixel];
            change += Math.Abs(frame[pixel] - previous[pixel]);
        }

        return new FrameLight(Mean(brightness), seenOne ? Mean(change) : (byte)0);
    }

    private static byte Mean(long sum) => (byte)((sum + (FrameLight.Pixels / 2)) / FrameLight.Pixels);
}
