namespace Carina.Domain.Segments;

/// <summary>
/// Reads sound handed over as 16-bit samples, <see cref="SampleRate"/> a second, left and right
/// interleaved, the first sample at the recording's own time zero, in pieces of any length. A
/// reading is written as soon as every sample it covers has arrived: the fingerprint of the window
/// starting at every <see cref="SoundFingerprint.Hop"/>th sample, of the two channels added
/// together, the time before zero heard as silence; the loudness; and the channel difference.
/// <see cref="Finish"/> writes those the end of the sound cuts short, the time after it heard as
/// silence, and drops a left sample left without its right.
/// </summary>
public sealed class SoundReader
{
    public const int SampleRate = 8000;

    public const int Channels = 2;

    private readonly SoundSpectrum spectrum = new();

    private readonly double[] window = new double[SoundSpectrum.Window];

    private double[] before = new double[SoundSpectrum.Bands];

    private double[] now = new double[SoundSpectrum.Bands];

    private int held = SoundFingerprint.Hop;

    private long nextWindow = -1;

    private short? unpaired;

    private long loudSquares;

    private int loudSamples;

    private long leftSquares;

    private long rightSquares;

    private long products;

    private long differenceSquares;

    private int channelSamples;

    private bool finished;

    public long Samples { get; private set; }

    public void Hear(ReadOnlySpan<short> interleaved, SoundReadings readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        StillOpen();

        ReadOnlySpan<short> rest = interleaved;

        if (unpaired is short left && !rest.IsEmpty)
        {
            Take(left, rest[0], readings);
            unpaired = null;
            rest = rest[1..];
        }

        int pairs = rest.Length / Channels;

        for (int pair = 0; pair < pairs; pair++)
        {
            Take(rest[Channels * pair], rest[(Channels * pair) + 1], readings);
        }

        if (rest.Length % Channels is 1)
        {
            unpaired = rest[^1];
        }
    }

    public void Finish(SoundReadings readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        StillOpen();

        finished = true;
        unpaired = null;

        if (loudSamples > 0)
        {
            WriteLoudness(readings);
        }

        if (channelSamples > 0)
        {
            WriteChannels(readings);
        }

        while (nextWindow * SoundFingerprint.Hop < Samples)
        {
            Array.Clear(window, held, SoundSpectrum.Window - held);
            held = SoundSpectrum.Window;
            WriteFingerprint(readings);
        }
    }

    private void Take(short left, short right, SoundReadings readings)
    {
        Samples++;

        window[held++] = left + right;

        if (held == SoundSpectrum.Window)
        {
            WriteFingerprint(readings);
        }

        long leftSquare = (long)left * left;
        long rightSquare = (long)right * right;
        long difference = left - right;

        loudSquares += leftSquare + rightSquare;
        loudSamples++;
        leftSquares += leftSquare;
        rightSquares += rightSquare;
        products += (long)left * right;
        differenceSquares += difference * difference;
        channelSamples++;

        if (loudSamples == SoundLoudness.Hop)
        {
            WriteLoudness(readings);
        }

        if (channelSamples == ChannelDifference.Hop)
        {
            WriteChannels(readings);
        }
    }

    private void WriteFingerprint(SoundReadings readings)
    {
        spectrum.Measure(window, now);

        if (nextWindow >= 0)
        {
            readings.Fingerprints.Add(SoundFingerprint.Of(before, now));
        }

        (before, now) = (now, before);
        Array.Copy(window, SoundFingerprint.Hop, window, 0, SoundSpectrum.Window - SoundFingerprint.Hop);
        held = SoundSpectrum.Window - SoundFingerprint.Hop;
        nextWindow++;
    }

    private void WriteLoudness(SoundReadings readings)
    {
        readings.Loudness.Add(SoundLoudness.Of(loudSquares / ((double)Channels * loudSamples)));
        loudSquares = 0;
        loudSamples = 0;
    }

    private void WriteChannels(SoundReadings readings)
    {
        readings.Channels.Add(ChannelDifference.Of(leftSquares, rightSquares, products, differenceSquares, channelSamples));
        leftSquares = 0;
        rightSquares = 0;
        products = 0;
        differenceSquares = 0;
        channelSamples = 0;
    }

    private void StillOpen()
    {
        if (finished)
        {
            throw new InvalidOperationException("The sound has been finished, and nothing more is read after its end.");
        }
    }
}
