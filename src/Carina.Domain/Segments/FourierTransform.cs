using System.Numerics;

namespace Carina.Domain.Segments;

/// <summary>
/// The discrete Fourier transform of a fixed size, a power of two, worked in place on the real and
/// imaginary parts held apart. The order the samples are taken in and the turns they are multiplied
/// by are worked out once, when the transform is made.
/// </summary>
public sealed class FourierTransform
{
    private readonly int[] reversed;

    private readonly double[] cosines;

    private readonly double[] sines;

    public FourierTransform(int size)
    {
        if (size < 2 || !BitOperations.IsPow2(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "A transform is made for a power of two of at least two samples.");
        }

        Size = size;

        int bits = BitOperations.Log2((uint)size);
        reversed = new int[size];

        for (int at = 0; at < size; at++)
        {
            reversed[at] = (int)(ReverseBits((uint)at) >> (32 - bits));
        }

        cosines = new double[size / 2];
        sines = new double[size / 2];

        for (int turn = 0; turn < size / 2; turn++)
        {
            double angle = -2 * Math.PI * turn / size;
            cosines[turn] = Math.Cos(angle);
            sines[turn] = Math.Sin(angle);
        }
    }

    public int Size { get; }

    public void Transform(double[] real, double[] imaginary)
    {
        ArgumentNullException.ThrowIfNull(real);
        ArgumentNullException.ThrowIfNull(imaginary);
        Sized(real, nameof(real));
        Sized(imaginary, nameof(imaginary));

        Reorder(real, imaginary);

        for (int half = 1; half < Size; half <<= 1)
        {
            Combine(real, imaginary, half);
        }
    }

    private void Combine(double[] real, double[] imaginary, int half)
    {
        int step = Size / (half * 2);

        for (int start = 0; start < Size; start += half * 2)
        {
            for (int offset = 0; offset < half; offset++)
            {
                double cosine = cosines[offset * step];
                double sine = sines[offset * step];
                int near = start + offset;
                int far = near + half;
                double turnedReal = (cosine * real[far]) - (sine * imaginary[far]);
                double turnedImaginary = (cosine * imaginary[far]) + (sine * real[far]);

                real[far] = real[near] - turnedReal;
                imaginary[far] = imaginary[near] - turnedImaginary;
                real[near] += turnedReal;
                imaginary[near] += turnedImaginary;
            }
        }
    }

    private void Reorder(double[] real, double[] imaginary)
    {
        for (int at = 0; at < Size; at++)
        {
            int other = reversed[at];

            if (other > at)
            {
                (real[at], real[other]) = (real[other], real[at]);
                (imaginary[at], imaginary[other]) = (imaginary[other], imaginary[at]);
            }
        }
    }

    private void Sized(double[] part, string name)
    {
        if (part.Length != Size)
        {
            throw new ArgumentException($"This transform takes {Size} samples, and {part.Length} were handed over.", name);
        }
    }

    private static uint ReverseBits(uint value)
    {
        uint reversedValue = 0;

        for (int bit = 0; bit < 32; bit++)
        {
            reversedValue = (reversedValue << 1) | ((value >> bit) & 1);
        }

        return reversedValue;
    }
}
