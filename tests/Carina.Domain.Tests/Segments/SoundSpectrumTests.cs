using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SoundSpectrumTests
{
    private const double Amplitude = 8000;

    [Theory(DisplayName = "a tone puts its power in the band that holds its pitch and next to none in the others")]
    [InlineData(2)]
    [InlineData(16)]
    [InlineData(31)]
    public void AToneLandsInItsBand(int band)
    {
        double hertz = Math.Sqrt(SoundSpectrum.Edge(band) * SoundSpectrum.Edge(band + 1));
        double[] bands = Measured(hertz);
        double tonePower = 3 * Amplitude * Amplitude * SoundSpectrum.Window * SoundSpectrum.Window / 32;

        Assert.Equal(band, SoundSpectrum.BandOf(hertz));
        Assert.InRange(bands[band], tonePower * 0.98, tonePower * 1.02);
        Assert.True(bands.Sum() - bands[band] < tonePower * 0.01);
    }

    [Theory(DisplayName = "a tone below the lowest band or above the highest puts next to no power in any band")]
    [InlineData(120)]
    [InlineData(3000)]
    public void AToneOutsideTheBandsLandsInNone(double hertz)
    {
        double tonePower = 3 * Amplitude * Amplitude * SoundSpectrum.Window * SoundSpectrum.Window / 32;

        Assert.Equal(-1, SoundSpectrum.BandOf(hertz));
        Assert.True(Measured(hertz).Sum() < tonePower * 1e-3);
    }

    [Fact(DisplayName = "the bands run from 300 Hz to 2000 Hz, each wider than the one below it by the same share")]
    public void TheBandsAreEvenOnALogarithmicScale()
    {
        double ratio = SoundSpectrum.Edge(1) / SoundSpectrum.Edge(0);

        Assert.Equal(300, SoundSpectrum.Edge(0));
        Assert.Equal(2000, SoundSpectrum.Edge(SoundSpectrum.Bands));
        Assert.All(
            Enumerable.Range(1, SoundSpectrum.Bands),
            edge => Assert.Equal(ratio, SoundSpectrum.Edge(edge) / SoundSpectrum.Edge(edge - 1), 1e-9));
        Assert.Equal(0, SoundSpectrum.BandOf(300));
        Assert.Equal(SoundSpectrum.Bands - 1, SoundSpectrum.BandOf(1999.9));
        Assert.Equal(-1, SoundSpectrum.BandOf(2000));
    }

    [Fact(DisplayName = "a window of any other length, or room for any other count of bands, is refused")]
    public void OtherShapesAreRefused()
    {
        SoundSpectrum spectrum = new();

        Assert.Throws<ArgumentException>(() => spectrum.Measure(new double[SoundSpectrum.Window - 1], new double[SoundSpectrum.Bands]));
        Assert.Throws<ArgumentException>(() => spectrum.Measure(new double[SoundSpectrum.Window], new double[SoundSpectrum.Bands + 1]));
    }

    private static double[] Measured(double hertz)
    {
        double[] samples = [.. Enumerable.Range(0, SoundSpectrum.Window).Select(sample => Amplitude * Math.Sin(2 * Math.PI * hertz * sample / SoundReader.SampleRate))];
        double[] bands = new double[SoundSpectrum.Bands];

        new SoundSpectrum().Measure(samples, bands);

        return bands;
    }
}
