using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

/// <summary>
/// The readings of a recording made up for the tests: loud sound with channels much alike, ten frames a
/// second of mid grey that hardly move, and nothing in the corners, changed where a test says. The
/// readings reach as far as <c>reach</c>, which may lie past the length of the recording.
/// </summary>
internal sealed class SyntheticReadings
{
    public const byte Loud = 40;

    /// <summary>
    /// A pause, at -65 dBFS: quieter than the silence thresholds, far louder than the silence an edit leaves.
    /// </summary>
    public const byte Pause = 130;

    public const byte AlikeChannels = 190;

    /// <summary>
    /// The level speech drops to between its words, at -40 dBFS: far quieter than it speaks, louder than any silence.
    /// </summary>
    public const byte BetweenWords = 80;

    public const int FramesASecond = 10;

    public static readonly TimeSpan LoudnessStep = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * SoundLoudness.Hop / SoundReader.SampleRate);

    public static readonly TimeSpan ChannelStep = TimeSpan.FromTicks(TimeSpan.TicksPerSecond * ChannelDifference.Hop / SoundReader.SampleRate);

    private readonly TimeSpan length;

    private readonly byte[] loudness;

    private readonly ChannelDifference[] channels;

    private readonly FrameLight[] frames;

    private byte[] outlines = [];

    private byte[]? captions;

    public SyntheticReadings(double lengthSeconds, double? reachSeconds = null)
    {
        double reach = reachSeconds ?? lengthSeconds;

        length = TimeSpan.FromSeconds(lengthSeconds);
        loudness = [.. Enumerable.Repeat(Loud, (int)Math.Ceiling(reach / LoudnessStep.TotalSeconds))];
        channels = [.. Enumerable.Repeat(new ChannelDifference(AlikeChannels, 100), (int)Math.Ceiling(reach / ChannelStep.TotalSeconds))];
        frames = [.. Enumerable.Repeat(new FrameLight(128, 2), (int)Math.Floor(reach * FramesASecond) + 1)];
    }

    public SyntheticReadings Quiet(double from, double to, byte level = SoundLoudness.Silent)
    {
        for (long at = Reading(from, LoudnessStep); at < Reading(to, LoudnessStep) && at < loudness.Length; at++)
        {
            loudness[at] = level;
        }

        return this;
    }

    /// <summary>
    /// Silence of whole readings centred on <paramref name="at"/>, half of <paramref name="lasting"/> on
    /// either side, at <paramref name="level"/>.
    /// </summary>
    public SyntheticReadings QuietAround(double at, double lasting = 0.5, byte level = SoundLoudness.Silent)
    {
        long middle = Reading(at, LoudnessStep);
        long half = (long)Math.Round(lasting / 2 / LoudnessStep.TotalSeconds, MidpointRounding.AwayFromZero);

        for (long reading = Math.Max(0, middle - half); reading < middle + half && reading < loudness.Length; reading++)
        {
            loudness[reading] = level;
        }

        return this;
    }

    /// <summary>
    /// Speech from <paramref name="from"/> to <paramref name="to"/>: every three seconds, the last of them drops to
    /// <see cref="BetweenWords"/>, so its loudness range is 20 decibels where a song's is none.
    /// </summary>
    public SyntheticReadings Spoken(double from, double to)
    {
        for (double word = from; word + 2 < to; word += 3)
        {
            Quiet(word + 2, Math.Min(word + 3, to), BetweenWords);
        }

        return this;
    }

    public SyntheticReadings Cut(double at, double score)
    {
        long frame = Reading(at, TimeSpan.FromTicks(TimeSpan.TicksPerSecond / FramesASecond));

        if (frame < frames.Length)
        {
            frames[frame] = frames[frame] with { Change = (byte)Math.Min(255, Math.Ceiling(score * 255)) };
        }

        return this;
    }

    public SyntheticReadings Black(double from, double to, byte brightness = 0)
    {
        for (int frame = 0; frame < frames.Length; frame++)
        {
            double at = frame / (double)FramesASecond;

            if (at >= from - 1e-9 && at <= to + 1e-9)
            {
                frames[frame] = frames[frame] with { Brightness = brightness };
            }
        }

        return this;
    }

    public SyntheticReadings Channels(double from, double to, byte correlation)
    {
        for (long at = Reading(from, ChannelStep); at < Reading(to, ChannelStep) && at < channels.Length; at++)
        {
            channels[at] = channels[at] with { Correlation = correlation };
        }

        return this;
    }

    public SyntheticReadings Corners(byte[] drawn)
    {
        outlines = drawn;

        return this;
    }

    public SyntheticReadings Captioned(Func<int, bool> shown)
    {
        captions = [.. Enumerable.Range(0, (int)Math.Ceiling(length.TotalSeconds)).Select(second => shown(second) ? CaptionPresence.Shown : CaptionPresence.Hidden)];

        return this;
    }

    public SegmentEvidence Evidence()
        => SegmentEvidence.Of(
            length,
            loudness,
            channels,
            [.. frames.Select((light, at) => new PlacedFrame(TimeSpan.FromTicks(at * (TimeSpan.TicksPerSecond / FramesASecond)), light))],
            outlines,
            captions);

    private static long Reading(double at, TimeSpan step) => (long)Math.Round(at / step.TotalSeconds, MidpointRounding.AwayFromZero);
}
