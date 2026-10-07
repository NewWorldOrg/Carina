using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class FourierTransformTests
{
    [Fact(DisplayName = "the transform gives what the sum that defines it gives")]
    public void TheTransformGivesWhatItsDefinitionGives()
    {
        const int size = 64;
        Random random = new(7);
        double[] real = [.. Enumerable.Range(0, size).Select(_ => random.NextDouble() - 0.5)];
        double[] imaginary = [.. Enumerable.Range(0, size).Select(_ => random.NextDouble() - 0.5)];
        (double[] definedReal, double[] definedImaginary) = Defined(real, imaginary);

        new FourierTransform(size).Transform(real, imaginary);

        for (int bin = 0; bin < size; bin++)
        {
            Assert.Equal(definedReal[bin], real[bin], 1e-9);
            Assert.Equal(definedImaginary[bin], imaginary[bin], 1e-9);
        }
    }

    [Fact(DisplayName = "a wave that turns a whole number of times in the window lands in that one bin and its mirror")]
    public void AWaveLandsInItsBin()
    {
        const int size = 256;
        const int turns = 19;
        double[] real = [.. Enumerable.Range(0, size).Select(sample => Math.Cos(2 * Math.PI * turns * sample / size))];
        double[] imaginary = new double[size];

        new FourierTransform(size).Transform(real, imaginary);

        for (int bin = 0; bin < size; bin++)
        {
            double magnitude = Math.Sqrt((real[bin] * real[bin]) + (imaginary[bin] * imaginary[bin]));
            double expected = bin is turns or size - turns ? size / 2.0 : 0;
            Assert.Equal(expected, magnitude, 1e-9);
        }
    }

    [Theory(DisplayName = "a transform is made only for a power of two of at least two samples")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(100)]
    public void OnlyAPowerOfTwoIsTransformed(int size)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new FourierTransform(size));

    [Fact(DisplayName = "samples of another count than the transform's size are refused")]
    public void SamplesOfAnotherCountAreRefused()
    {
        FourierTransform transform = new(16);

        Assert.Throws<ArgumentException>(() => transform.Transform(new double[8], new double[16]));
        Assert.Throws<ArgumentException>(() => transform.Transform(new double[16], new double[32]));
    }

    private static (double[] Real, double[] Imaginary) Defined(double[] real, double[] imaginary)
    {
        int size = real.Length;
        double[] outReal = new double[size];
        double[] outImaginary = new double[size];

        for (int bin = 0; bin < size; bin++)
        {
            for (int sample = 0; sample < size; sample++)
            {
                double angle = -2 * Math.PI * bin * sample / size;
                outReal[bin] += (real[sample] * Math.Cos(angle)) - (imaginary[sample] * Math.Sin(angle));
                outImaginary[bin] += (real[sample] * Math.Sin(angle)) + (imaginary[sample] * Math.Cos(angle));
            }
        }

        return (outReal, outImaginary);
    }
}
