namespace Carina.Domain.Encodings;

/// <summary>
/// Learns a station's watermark from the pictures of one recording, one picture at a time and
/// holding none of them: a corner pixel that was an edge in at least <see cref="SteadyShare"/> of
/// the pictures is part of the mark. Nothing is learned from fewer than <see cref="FewestFrames"/>
/// pictures, or when fewer than <see cref="FewestPixels"/> pixels, or more than
/// <see cref="MostOfTheCorners"/> of the corner pixels, stayed put.
/// </summary>
public sealed class WatermarkLearner
{
    public const int FewestFrames = 60;

    public const double SteadyShare = 0.5;

    public const int FewestPixels = 12;

    public const double MostOfTheCorners = 0.1;

    private readonly int[] edges = new int[WatermarkFrame.Pixels];

    public int Frames { get; private set; }

    public void Pictured(ReadOnlySpan<byte> frame)
    {
        WatermarkFrame.Sized(frame);

        foreach (int pixel in WatermarkFrame.Corners)
        {
            if (WatermarkFrame.IsEdge(frame, pixel))
            {
                edges[pixel]++;
            }
        }

        Frames++;
    }

    public WatermarkMask? Learned()
    {
        if (Frames < FewestFrames)
        {
            return null;
        }

        List<int> steady = [];

        foreach (int pixel in WatermarkFrame.Corners)
        {
            if (edges[pixel] >= Frames * SteadyShare)
            {
                steady.Add(pixel);
            }
        }

        return steady.Count < FewestPixels || steady.Count > WatermarkFrame.CornerPixels * MostOfTheCorners
            ? null
            : WatermarkMask.Covering(steady);
    }
}
