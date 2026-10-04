namespace Carina.Domain.Playback;

/// <summary>
/// Reads what the picture of an artefact on disk is coded in.
/// </summary>
public interface IArtefactCodecReader
{
    Task<ArtefactCodecReading> ReadAsync(PlaybackFile file, CancellationToken cancellationToken);
}
