namespace Carina.Domain.Encodings;

public sealed class ArtefactRewriteDomainService(IArtefactOpenings openings, TimeProvider clock)
{
    public static readonly TimeSpan LeftAloneAfterAnOpening = TimeSpan.FromMinutes(30);

    public bool IsLeftAlone(EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return job.ArtefactName is { } name
               && openings.LastOpened(job.OutputRoot, name.Value) is { } opened
               && clock.GetUtcNow() - opened < LeftAloneAfterAnOpening;
    }
}
