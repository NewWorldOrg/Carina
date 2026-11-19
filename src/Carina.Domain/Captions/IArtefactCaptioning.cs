namespace Carina.Domain.Captions;

public sealed record ArtefactCaptioningRound(int Read, int Added, int Withheld, int Failed, bool Yielded);

public interface IArtefactCaptioning
{
    Task<ArtefactCaptioningRound> CaptionAsync(int atMost, Func<CancellationToken, Task<bool>> busy, CancellationToken cancellationToken);
}
