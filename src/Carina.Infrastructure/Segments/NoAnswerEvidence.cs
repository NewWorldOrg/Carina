using Carina.Domain.Segments;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// Answers that no confirmed recording and no boundary stand behind an evaluation: no answers are kept.
/// </summary>
public sealed class NoAnswerEvidence : IAnswerEvidenceReader
{
    public Task<EvaluationEvidence> ReadAsync(CancellationToken cancellationToken)
        => Task.FromResult(new EvaluationEvidence(0, 0, null));
}
