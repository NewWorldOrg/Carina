namespace Carina.Domain.Segments;

public sealed class LearningDataUnplaceableException(ExtractionFailure failure, string message)
    : InvalidOperationException(message)
{
    public ExtractionFailure Failure { get; } = failure;
}
