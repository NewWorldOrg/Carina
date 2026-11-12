namespace Carina.Domain.Encodings;

/// <summary>
/// Decides whether an artefact already made may be written again now, with a text track of captions put in or with
/// new chapters: one somebody opened in the last <see cref="LeftAloneAfterAnOpening"/> is left alone, so that it is
/// not replaced under somebody who is still reading it.
/// </summary>
public sealed class ArtefactRewriteDomainService(IArtefactOpenings openings, TimeProvider clock)
{
    public static readonly TimeSpan LeftAloneAfterAnOpening = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Whether the artefact of <paramref name="job"/> was opened too lately to be written again now.
    /// </summary>
    public bool IsLeftAlone(EncodeJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return job.ArtefactName is { } name
               && openings.LastOpened(job.OutputRoot, name.Value) is { } opened
               && clock.GetUtcNow() - opened < LeftAloneAfterAnOpening;
    }
}
