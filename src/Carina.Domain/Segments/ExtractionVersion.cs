namespace Carina.Domain.Segments;

public sealed record ExtractionVersion
{
    public ExtractionVersion(int number, ExtractionOrigin origin)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);

        if (!Enum.IsDefined(origin))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), origin, "Learning data is made from a recording's file or from a reduced copy of it.");
        }

        Number = number;
        Origin = origin;
    }

    public static ExtractionVersion Current { get; } = new(LearningData.ExtractionVersion, ExtractionOrigin.RecordingFile);

    public static ExtractionVersion CurrentFromReducedCopy { get; } = new(LearningData.ExtractionVersion, ExtractionOrigin.ReducedCopy);

    public int Number { get; }

    public ExtractionOrigin Origin { get; }
}
