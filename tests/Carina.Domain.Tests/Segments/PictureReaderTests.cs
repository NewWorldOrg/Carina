using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class PictureReaderTests
{
    [Fact(DisplayName = "a black frame reads dark and a white one bright")]
    public void ABlackFrameReadsDark()
    {
        List<FrameLight> lights = Read(Filled(16), Filled(235));

        Assert.Equal(16, lights[0].Brightness);
        Assert.Equal(235, lights[1].Brightness);
    }

    [Fact(DisplayName = "a frame like the one before it reads no change, and a cut reads the mean step between the two scenes")]
    public void ACutReadsTheStepBetweenScenes()
    {
        List<FrameLight> lights = Read(Filled(40), Filled(40), Scene(2), Scene(2));

        Assert.Equal([0, 0], lights.Take(2).Select(light => (int)light.Change));
        Assert.InRange(lights[2].Change, 60, 255);
        Assert.Equal(0, lights[3].Change);
    }

    [Fact(DisplayName = "a little movement inside a scene reads a small change")]
    public void LittleMovementReadsASmallChange()
    {
        List<FrameLight> lights = Read(Bar(10), Bar(11), Bar(12));

        Assert.All(lights.Skip(1), light => Assert.InRange(light.Change, 1, 10));
    }

    [Fact(DisplayName = "the first frame reads as having changed by nothing")]
    public void TheFirstFrameReadsNoChange()
        => Assert.Equal(0, Read(Filled(200))[0].Change);

    [Fact(DisplayName = "frames handed over in pieces of any length read the same as handed over whole")]
    public void PiecesReadTheSameAsWhole()
    {
        byte[] frames = [.. Enumerable.Range(0, 20).SelectMany(Scene)];
        List<FrameLight> whole = Read(frames);
        PictureReader reader = new();
        List<FrameLight> pieced = [];
        Random random = new(5);

        for (int at = 0; at < frames.Length;)
        {
            int length = Math.Min(frames.Length - at, random.Next(1, 3 * FrameLight.Pixels));
            reader.See(frames.AsSpan(at, length), pieced);
            at += length;
        }

        Assert.Equal(whole, pieced);
        Assert.Equal(20, reader.Frames);
    }

    [Fact(DisplayName = "a frame not yet whole is not read")]
    public void AFrameNotYetWholeIsNotRead()
    {
        PictureReader reader = new();
        List<FrameLight> lights = [];

        reader.See(new byte[FrameLight.Pixels - 1], lights);

        Assert.Empty(lights);
        Assert.Equal(2304, FrameLight.Pixels);
    }

    internal static byte[] Filled(byte shade)
    {
        byte[] frame = new byte[FrameLight.Pixels];
        Array.Fill(frame, shade);

        return frame;
    }

    internal static byte[] Scene(int seed)
    {
        Random random = new(seed);

        return [.. Enumerable.Range(0, FrameLight.Pixels).Select(_ => (byte)random.Next(0, 256))];
    }

    private static byte[] Bar(int left)
    {
        byte[] frame = Filled(60);

        for (int y = 0; y < FrameLight.Height; y++)
        {
            Array.Fill<byte>(frame, 200, (y * FrameLight.Width) + left, 8);
        }

        return frame;
    }

    private static List<FrameLight> Read(params byte[][] frames)
    {
        PictureReader reader = new();
        List<FrameLight> lights = [];

        foreach (byte[] frame in frames)
        {
            reader.See(frame, lights);
        }

        return lights;
    }
}
