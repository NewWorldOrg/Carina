namespace Carina.Domain.Segments;

/// <summary>
/// Thrown when what was handed over cannot be placed on a recording's own time as learning data, with
/// the kind of failure the extraction records for it.
/// </summary>
public sealed class LearningDataUnplaceableException(ExtractionFailure failure, string message)
    : InvalidOperationException(message)
{
    public ExtractionFailure Failure { get; } = failure;
}
