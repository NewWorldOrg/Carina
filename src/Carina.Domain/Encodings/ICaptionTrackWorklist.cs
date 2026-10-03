namespace Carina.Domain.Encodings;

/// <summary>
/// An artefact that stands for its recording, and when the captions it is to have a text track made from were
/// taken from the recording.
/// </summary>
public sealed record CaptionTrackSubject(EncodeJob Job, DateTime CaptionsMadeAt);

public interface ICaptionTrackWorklist
{
    /// <summary>
    /// The artefacts that stand for a recording whose captions are ready and that await a text track of them,
    /// with no job of that recording waiting or running, newest recording first.
    /// </summary>
    Task<IReadOnlyList<CaptionTrackSubject>> AwaitingAsync(int atMost, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the job's artefact still stands for its recording, as the ledger says now, with no job of that
    /// recording waiting or running.
    /// </summary>
    Task<bool> StandsWithNothingInHandAsync(EncodeJob job, CancellationToken cancellationToken);
}
