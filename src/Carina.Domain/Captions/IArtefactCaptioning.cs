namespace Carina.Domain.Captions;

/// <summary>
/// What a round of putting text tracks of captions into artefacts did.
/// </summary>
public sealed record ArtefactCaptioningRound(int Read, int Added, int Withheld, int Failed, bool Yielded);

public interface IArtefactCaptioning
{
    /// <summary>
    /// Puts a text track of captions into at most <paramref name="atMost"/> artefacts that await one, asking
    /// <paramref name="busy"/> before each and stopping once it answers true.
    /// </summary>
    Task<ArtefactCaptioningRound> CaptionAsync(int atMost, Func<CancellationToken, Task<bool>> busy, CancellationToken cancellationToken);
}
