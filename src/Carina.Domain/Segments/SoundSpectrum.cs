namespace Carina.Domain.Segments;

/// <summary>
/// How strong a window of sound is in each of <see cref="Bands"/> bands spaced evenly on a
/// logarithmic scale from <see cref="LowestHertz"/> to <see cref="HighestHertz"/>: the window is
/// weighted by a Hann window and transformed, and the power of the frequencies falling in a band
/// is summed. The <see cref="Window"/> samples are transformed as half as many complex ones.
/// </summary>
public sealed class SoundSpectrum
{
    public const int Window = 2048;

    public const int Bands = 33;

    public const double LowestHertz = 300;

    public const double HighestHertz = 2000;

    public const double BinHertz = (double)SoundReader.SampleRate / Window;

    private const int Half = Window / 2;

    private static readonly double[] Weights = [.. Enumerable.Range(0, Window).Select(Weight)];

    private static readonly int[] Starts = [.. Enumerable.Range(0, Bands + 1).Select(edge => (int)Math.Ceiling(Edge(edge) / BinHertz))];

    private static readonly double[] TurnCosines = [.. Enumerable.Range(0, Half).Select(bin => Math.Cos(-Math.PI * bin / Half))];

    private static readonly double[] TurnSines = [.. Enumerable.Range(0, Half).Select(bin => Math.Sin(-Math.PI * bin / Half))];

    private readonly FourierTransform transform = new(Half);

    private readonly double[] real = new double[Half];

    private readonly double[] imaginary = new double[Half];

    private readonly double[] window = new double[Window];

    public static double Edge(int edge)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(edge);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(edge, Bands);

        return edge switch
        {
            0 => LowestHertz,
            Bands => HighestHertz,
            _ => LowestHertz * Math.Pow(HighestHertz / LowestHertz, (double)edge / Bands),
        };
    }

    public static int BandOf(double hertz)
    {
        for (int band = 0; band < Bands; band++)
        {
            if (hertz >= Edge(band) && hertz < Edge(band + 1))
            {
                return band;
            }
        }

        return -1;
    }

    public void Measure(ReadOnlySpan<double> samples, Span<double> bands)
    {
        if (samples.Length != Window)
        {
            throw new ArgumentException($"A spectrum is measured over {Window} samples, and {samples.Length} were handed over.", nameof(samples));
        }

        if (bands.Length != Bands)
        {
            throw new ArgumentException($"A spectrum is measured in {Bands} bands, and room was made for {bands.Length}.", nameof(bands));
        }

        samples.CopyTo(window);

        for (int pair = 0; pair < Half; pair++)
        {
            real[pair] = window[2 * pair] * Weights[2 * pair];
            imaginary[pair] = window[(2 * pair) + 1] * Weights[(2 * pair) + 1];
        }

        transform.Transform(real, imaginary);

        for (int band = 0; band < Bands; band++)
        {
            double power = 0;

            for (int bin = Starts[band]; bin < Starts[band + 1]; bin++)
            {
                power += Power(bin);
            }

            bands[band] = power;
        }
    }

    private double Power(int bin)
    {
        int mirror = (Half - bin) % Half;
        double evenReal = (real[bin] + real[mirror]) / 2;
        double evenImaginary = (imaginary[bin] - imaginary[mirror]) / 2;
        double oddReal = (imaginary[bin] + imaginary[mirror]) / 2;
        double oddImaginary = (real[mirror] - real[bin]) / 2;
        double cosine = TurnCosines[bin];
        double sine = TurnSines[bin];
        double wholeReal = evenReal + (cosine * oddReal) - (sine * oddImaginary);
        double wholeImaginary = evenImaginary + (cosine * oddImaginary) + (sine * oddReal);

        return (wholeReal * wholeReal) + (wholeImaginary * wholeImaginary);
    }

    private static double Weight(int sample) => 0.5 - (0.5 * Math.Cos(2 * Math.PI * sample / Window));
}
