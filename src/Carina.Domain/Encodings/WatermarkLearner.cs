namespace Carina.Domain.Encodings;

/// <summary>
/// Learns a station's watermark from the pictures of one recording, one picture at a time and
/// holding none of them: a pixel in a corner that was an edge in at least <see cref="SteadyShare"/>
/// of the pictures is part of the mark, because the programme moves behind a mark and the mark does
/// not. Advertisements carry no mark, which is why the share asked for is not higher. What comes out
/// is nothing when there were too few pictures to tell steady from moving, when too little of the
/// corners stayed put to be a mark, and when so much of them stayed put that what was learned is the
/// picture rather than a mark laid over it.
/// <para>
/// A recording is never judged by the mark learned from itself: the one that judges it
/// was learned ahead, from another recording of the same service.
/// </para>
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
