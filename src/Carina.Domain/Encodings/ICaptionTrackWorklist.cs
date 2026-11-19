namespace Carina.Domain.Encodings;

public sealed record CaptionTrackSubject(EncodeJob Job, DateTime CaptionsMadeAt);

public interface ICaptionTrackWorklist
{
    Task<IReadOnlyList<CaptionTrackSubject>> AwaitingAsync(int atMost, CancellationToken cancellationToken);

    Task<bool> StandsWithNothingInHandAsync(EncodeJob job, CancellationToken cancellationToken);
}
