namespace Carina.Domain.Streaming;

public sealed record TranscodeBudgetSettings
{
    public const int Fewest = 1;

    private readonly int atOnce = 4;

    public int AtOnce
    {
        get => atOnce;

        init => atOnce = value >= Fewest
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A ceiling of none is not a ceiling, it is a route that never plays.");
    }
}
