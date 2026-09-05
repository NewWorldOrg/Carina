namespace Carina.Domain.Quality;

public sealed record QualityReading
{
    private QualityReading(bool supported, bool supplied, int subjects, int measured, int beyondThreshold)
    {
        Supported = supported;
        Supplied = supplied;
        Subjects = subjects;
        Measured = measured;
        BeyondThreshold = beyondThreshold;
    }

    public bool Supported { get; }

    public bool Supplied { get; }

    public int Subjects { get; }

    public int Measured { get; }

    public int BeyondThreshold { get; }

    public int Unmeasured => Subjects - Measured;

    public static QualityReading Of(int subjects, int measured, int beyondThreshold)
        => Of(supported: true, supplied: true, subjects, measured, beyondThreshold);

    public static QualityReading Unsupported() => Of(supported: false, supplied: true, 0, 0, 0);

    public static QualityReading NotSupplied(int subjects, int measured)
        => Of(supported: true, supplied: false, subjects, measured, 0);

    public static QualityReading Of(bool supported, bool supplied, int subjects, int measured, int beyondThreshold)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(subjects);
        ArgumentOutOfRangeException.ThrowIfNegative(measured);
        ArgumentOutOfRangeException.ThrowIfNegative(beyondThreshold);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(measured, subjects);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(beyondThreshold, measured);

        return new QualityReading(supported, supplied, subjects, measured, beyondThreshold);
    }
}
