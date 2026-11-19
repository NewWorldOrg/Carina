namespace Carina.Domain.Playback;

public interface IArtefactCodecReader
{
    Task<ArtefactCodecReading> ReadAsync(PlaybackFile file, CancellationToken cancellationToken);
}
