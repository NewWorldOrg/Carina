namespace Carina.Infrastructure.Segments;

public sealed record LearningImportSettings
{
    public static readonly LearningImportSettings None = new();

    public string? ImportFrom { get; init; }
}
