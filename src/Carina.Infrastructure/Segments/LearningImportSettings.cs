namespace Carina.Infrastructure.Segments;

/// <summary>
/// Where the reduced copies of recordings are imported from: the directory that holds one directory a copy,
/// or nothing, when none is imported.
/// </summary>
public sealed record LearningImportSettings
{
    public static readonly LearningImportSettings None = new();

    public string? ImportFrom { get; init; }
}
