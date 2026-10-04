using Carina.Domain.Encodings;
using Carina.Domain.Playback;

namespace Carina.TestSupport;

public sealed class HeldArtefactCodecs : IArtefactCodecReader
{
    public Dictionary<string, ArtefactCodecReading> Readings { get; } = new(StringComparer.Ordinal);

    public void ReadAs(EncodeFileName artefact, EncodeCodec codec, string? tag = null)
    {
        ArgumentNullException.ThrowIfNull(artefact);

        Readings[artefact.Value] = ArtefactCodecReading.Of(codec, tag);
    }

    public Task<ArtefactCodecReading> ReadAsync(PlaybackFile file, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);

        return Task.FromResult(
            Readings.TryGetValue(file.Name.Value, out ArtefactCodecReading? reading)
                ? reading
                : ArtefactCodecReading.Unread("nothing was said about this file"));
    }
}
