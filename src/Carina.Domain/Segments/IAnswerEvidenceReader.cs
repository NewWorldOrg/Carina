namespace Carina.Domain.Segments;

/// <summary>
/// Reads what an evaluation would stand on now, from the answers kept: the confirmed recordings and the
/// boundaries their answers draw between a break and something else, as <see cref="EvaluationEvidence.Of"/>
/// counts them.
/// </summary>
public interface IAnswerEvidenceReader
{
    Task<EvaluationEvidence> ReadAsync(CancellationToken cancellationToken);
}
